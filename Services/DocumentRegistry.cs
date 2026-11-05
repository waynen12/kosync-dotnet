using Microsoft.Data.Sqlite;

namespace Kosync.Services;

// The Document a progress push is for, created if the server has never seen
// that hash from this User before (GLOSSARY "Document").
//
// A method of its own rather than a few lines in the sync endpoint, because
// the interesting case is not finding or creating - it is losing. Two Devices
// pushing the same brand-new hash in the same instant both find nothing and
// both insert, and the unique index on (UserId, DocumentHash) refuses the
// second (ADR 0008, issue #37). The refusal is the whole point: before the
// index, the database accepted both, and every later read of that hash threw
// on the pair forever.
public class DocumentRegistry
{
    private readonly KosyncDbContext _db;

    public DocumentRegistry(KosyncDbContext db)
    {
        _db = db;
    }

    // Takes the User with Documents already loaded - the caller needs them
    // for the rest of the push - and leaves the returned Document tracked
    // with its SyncEvents loaded either way, so the regression check behaves
    // the same whether this request created it, found it, or lost it.
    public Document GetOrCreate(User user, string documentHash)
    {
        // Safe as a Single rather than merely unwritten: the index is what
        // makes a second row with this hash impossible (ADR 0008).
        var existing = user.Documents.SingleOrDefault(d => d.DocumentHash == documentHash);
        if (existing is not null) { return existing; }

        var document = new Document()
        {
            DocumentHash = documentHash,
            // A brand-new Document gets its own Book until deliberately
            // merged into another (GLOSSARY "Book"), and that Book belongs
            // to the same User.
            Book = new Book() { User = user },
            User = user,
        };

        user.Documents.Add(document);

        // Saved here rather than with the SyncEvent that follows, because
        // the collision has to surface while there is still something to do
        // about it: the push can be redirected onto the Document that won,
        // which is not true once a SyncEvent has been built pointing at a
        // Document the database rejected. The cost is that a push failing
        // after this point leaves a Document with no progress on it - empty,
        // not broken, and the next push for that hash records against it.
        try
        {
            _db.SaveChanges();

            return document;
        }
        catch (DbUpdateException exception) when (WasRefusedAsADuplicate(exception, document))
        {
            Discard(document, user);

            return ReadTheDocumentThatWon(user, documentHash);
        }
    }

    // Only the Document's own refused insert counts as losing the race.
    // Anything else that failed in the same save, and any other constraint,
    // is a different fault and must not be swallowed as one.
    private static bool WasRefusedAsADuplicate(DbUpdateException exception, Document document)
    {
        return exception.InnerException is SqliteException
               {
                   SqliteExtendedErrorCode: Constants.Sqlite.UniqueConstraintViolation
               }
               && exception.Entries.Any(entry => ReferenceEquals(entry.Entity, document));
    }

    // The rejected Document and the Book it would have owned leave no trace:
    // the save was transactional, so neither row exists, and leaving them
    // tracked as Added would have the next SaveChanges retry the very insert
    // the database just refused. The Book is dropped with it rather than
    // kept - a Book no Document belongs to is a phantom in the reader's
    // library (ADR 0001), and the Document that won already has its own.
    private void Discard(Document document, User user)
    {
        var book = document.Book;

        _db.Entry(document).State = EntityState.Detached;
        _db.Entry(book).State = EntityState.Detached;

        user.Documents.Remove(document);
        user.Books.Remove(book);
    }

    // Must be there: it is the row the index refused this push for. Only its
    // owning User could have deleted it since, between two of their own
    // pushes a millisecond apart, which is not a window worth coding a retry
    // into - so this throws rather than quietly returning nothing.
    //
    // Says whose Document it wants rather than leaning on the tenant filter
    // for it (ADR 0004), because the invariant being read back is per User:
    // two Users with the same hash is two Documents and nothing wrong, and a
    // Single over an unscoped read would throw on the pair.
    private Document ReadTheDocumentThatWon(User user, string documentHash)
    {
        return _db.Documents
            .Include(d => d.SyncEvents)
            .Single(d => d.UserId == user.Id && d.DocumentHash == documentHash);
    }
}
