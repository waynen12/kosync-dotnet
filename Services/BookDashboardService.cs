namespace Kosync.Services;

public record SyncEventSummary(string DeviceName, decimal Percentage, DateTime Timestamp, bool IsCurrent, bool IsReset = false);

// Label is what to *call* this Document, resolved through DocumentLabelRule -
// the filename a Device observed, or a truncated hash. DocumentHash is the
// full hash regardless: it is what you need to diagnose a Split Book, so the
// label never replaces it (ADR 0007).
public record BookDocumentSummary(int DocumentId, string DocumentHash, string Label, string DeviceName, decimal Percentage, DateTime Timestamp, bool HasCurrentProgress, IReadOnlyList<SyncEventSummary> History);

// Title and Author are what to *show* for this Book, already resolved
// through BookMetadataRule - curated, then observed, then `Book #N`. The
// Curated* pair is what the owning User actually typed, which an edit form
// needs separately: it has to come up blank where the displayed title is
// only an observation showing through, or saving would silently curate it.
public record BookSummary(
    int Id,
    string Title,
    string? Author,
    string? CuratedTitle,
    string? CuratedAuthor,
    decimal Percentage,
    DateTime? LastSyncedAt,
    bool IsSplitBook,
    bool NeedsMetadataHint,
    IReadOnlyList<BookDocumentSummary> Documents);

public enum MergeBooksResult
{
    Success,
    SameBook,
    BookNotFound
}

public enum ResetProgressResult
{
    Success,
    DocumentNotFound
}

public enum CurateBookResult
{
    Success,
    BookNotFound
}

// Backs the Books dashboard view (issue #6): a Book card grid and its
// per-Book drill-in. A Book's displayed progress is the furthest-along
// current progress across its Documents (CONTEXT.md "Book") - whichever
// Document's current SyncEvent has the highest percentage wins, not
// whichever synced most recently.
public class BookDashboardService
{
    private readonly KosyncDbContext _db;

    public BookDashboardService(KosyncDbContext db)
    {
        _db = db;
    }

    public async Task<List<BookSummary>> GetBooksAsync()
    {
        var books = await _db.Books
            .Include(b => b.Documents)
                .ThenInclude(d => d.SyncEvents)
                    .ThenInclude(s => s.Device)
            .ToListAsync();

        return books
            .Select(Summarize)
            .OrderByDescending(b => b.LastSyncedAt)
            .ToList();
    }

    public async Task<BookSummary?> GetBookAsync(int id)
    {
        var book = await _db.Books
            .Include(b => b.Documents)
                .ThenInclude(d => d.SyncEvents)
                    .ThenInclude(s => s.Device)
            .FirstOrDefaultAsync(b => b.Id == id);

        return book is null ? null : Summarize(book);
    }

    // Manual Split Book merge action (issue #8): repoints every Document
    // from mergeBookId onto keepBookId - the resulting Book's Documents are
    // the union of both - then removes the now-orphaned Book. Discovery of
    // merge candidates is fully manual; there is no auto-suggestion here.
    public async Task<MergeBooksResult> MergeBooksAsync(int keepBookId, int mergeBookId)
    {
        if (keepBookId == mergeBookId)
        {
            return MergeBooksResult.SameBook;
        }

        var keepBook = await _db.Books.FirstOrDefaultAsync(b => b.Id == keepBookId);
        var mergeBook = await _db.Books
            .Include(b => b.Documents)
            .FirstOrDefaultAsync(b => b.Id == mergeBookId);

        if (keepBook is null || mergeBook is null)
        {
            return MergeBooksResult.BookNotFound;
        }

        // Both writes must land together - if the process dies or an
        // exception strikes between them, Documents must stay attached to
        // mergeBook rather than being left pointed at keepBook while
        // mergeBook still exists (issue #10).
        await using var transaction = await _db.Database.BeginTransactionAsync();

        foreach (var document in mergeBook.Documents)
        {
            document.BookId = keepBook.Id;
        }

        // Observations survive a merge by staying on their own Documents
        // (ADR 0007), but a Curated title lives on the Book about to be
        // removed and nothing can reproduce it - no Document observed it and
        // no push will bring it back. So it moves where the surviving Book
        // has no answer of its own, and never over one it has: two curated
        // titles are two human answers, and nothing here can tell which is
        // better than the Book its owner chose to keep.
        keepBook.CuratedTitle ??= mergeBook.CuratedTitle;
        keepBook.CuratedAuthor ??= mergeBook.CuratedAuthor;

        await _db.SaveChangesAsync();

        _db.Books.Remove(mergeBook);
        await _db.SaveChangesAsync();

        await transaction.CommitAsync();

        return MergeBooksResult.Success;
    }

    // "Delete progress" dashboard action (issue #9): clears the Document's
    // current-progress pointer so the reader can deliberately restart a
    // book, without touching SyncEvent history underneath. Unlike
    // DELETE /manage/users/documents (unmodified, unrelated), this never
    // removes SyncEvents - the next push simply has nothing to regress
    // against and becomes current regardless of percentage.
    public async Task<ResetProgressResult> ResetProgressAsync(int documentId)
    {
        var document = await _db.Documents
            .Include(d => d.SyncEvents)
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (document is null)
        {
            return ResetProgressResult.DocumentNotFound;
        }

        var current = document.SyncEvents.Current();
        if (current is not null)
        {
            current.IsCurrent = false;
        }

        document.LastResetAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ResetProgressResult.Success;
    }

    // Sets a Book's Curated Metadata, or clears it (issue #30): a blank
    // title or author is "I have nothing to say about this", which drops the
    // Book back to whatever its Documents observed. One method for both,
    // because the form that sets a title is the same form that empties it.
    //
    // Scoped by the caller's tenant like every other read here (ADR 0004):
    // another User's Book id filters out and comes back BookNotFound, which
    // is also this action's whole authorisation check.
    public async Task<CurateBookResult> CurateBookAsync(int bookId, string? title, string? author)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId);

        if (book is null)
        {
            return CurateBookResult.BookNotFound;
        }

        // Blank is stored as no value at all (MetadataText), so "   " clears
        // rather than curating a Book whose title is three spaces.
        book.CuratedTitle = MetadataText.TrimmedOrNull(title);
        book.CuratedAuthor = MetadataText.TrimmedOrNull(author);
        await _db.SaveChangesAsync();

        return CurateBookResult.Success;
    }

    private static BookSummary Summarize(Book book)
    {
        var documents = book.Documents
            .Select(d => new { d.Id, d.DocumentHash, Label = DocumentLabelRule.Resolve(d), History = BuildHistory(d) })
            .Where(x => x.History.Count > 0)
            .Select(x => new { x.Id, x.DocumentHash, x.Label, x.History, Current = x.History.FirstOrDefault(h => h.IsCurrent) })
            .Select(x => x.Current is not null
                ? new BookDocumentSummary(x.Id, x.DocumentHash, x.Label, x.Current.DeviceName, x.Current.Percentage, x.Current.Timestamp, HasCurrentProgress: true, x.History)
                // No current push - either freshly reset, or (in principle)
                // never synced. Stays visible in its Book at 0% (rather than
                // being hidden entirely) until the next push lands.
                : new BookDocumentSummary(x.Id, x.DocumentHash, x.Label, string.Empty, 0m, x.History[0].Timestamp, HasCurrentProgress: false, x.History))
            .OrderByDescending(d => d.Percentage)
            .ToList();

        var percentage = documents.Count == 0 ? 0m : documents.Max(d => d.Percentage);
        var lastSyncedAt = documents.Count == 0 ? (DateTime?)null : documents.Max(d => d.Timestamp);
        var isSplitBook = SplitBookRule.IsSplit(book);

        return new BookSummary(
            book.Id,
            BookMetadataRule.ResolveTitle(book),
            BookMetadataRule.ResolveAuthor(book),
            book.CuratedTitle,
            book.CuratedAuthor,
            percentage,
            lastSyncedAt,
            isSplitBook,
            BookMetadataRule.NeedsMetadataHint(book),
            documents);
    }

    // Merges a Document's push history with its reset marker (issue #9)
    // into one timeline. The reset marker isn't a SyncEvent - CONTEXT.md
    // ties SyncEvent to a Device push - but it still needs to appear
    // alongside them as a distinct, identifiable entry rather than a
    // separate view.
    private static List<SyncEventSummary> BuildHistory(Document document)
    {
        var pushes = document.SyncEvents.History()
            .Select(s => new SyncEventSummary(s.Device.DeviceName, s.Percentage, s.Timestamp, s.IsCurrent))
            .ToList();

        if (document.LastResetAt is not DateTime resetAt)
        {
            return pushes;
        }

        return pushes
            .Append(new SyncEventSummary(string.Empty, 0m, resetAt, IsCurrent: false, IsReset: true))
            .OrderByDescending(h => h.Timestamp)
            .ToList();
    }
}
