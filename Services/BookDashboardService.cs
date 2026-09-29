namespace Kosync.Services;

public record SyncEventSummary(string DeviceName, decimal Percentage, DateTime Timestamp, bool IsCurrent, bool IsReset = false);

public record BookDocumentSummary(int DocumentId, string DocumentHash, string DeviceName, decimal Percentage, DateTime Timestamp, bool HasCurrentProgress, IReadOnlyList<SyncEventSummary> History);

public record BookSummary(int Id, decimal Percentage, DateTime? LastSyncedAt, bool IsSplitBook, IReadOnlyList<BookDocumentSummary> Documents);

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

        foreach (var document in mergeBook.Documents)
        {
            document.BookId = keepBook.Id;
        }
        await _db.SaveChangesAsync();

        _db.Books.Remove(mergeBook);
        await _db.SaveChangesAsync();

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

    private static BookSummary Summarize(Book book)
    {
        var documents = book.Documents
            .Select(d => new { d.Id, d.DocumentHash, History = BuildHistory(d) })
            .Where(x => x.History.Count > 0)
            .Select(x => new { x.Id, x.DocumentHash, x.History, Current = x.History.FirstOrDefault(h => h.IsCurrent) })
            .Select(x => x.Current is not null
                ? new BookDocumentSummary(x.Id, x.DocumentHash, x.Current.DeviceName, x.Current.Percentage, x.Current.Timestamp, HasCurrentProgress: true, x.History)
                // No current push - either freshly reset, or (in principle)
                // never synced. Stays visible in its Book at 0% (rather than
                // being hidden entirely) until the next push lands.
                : new BookDocumentSummary(x.Id, x.DocumentHash, string.Empty, 0m, x.History[0].Timestamp, HasCurrentProgress: false, x.History))
            .OrderByDescending(d => d.Percentage)
            .ToList();

        var percentage = documents.Count == 0 ? 0m : documents.Max(d => d.Percentage);
        var lastSyncedAt = documents.Count == 0 ? (DateTime?)null : documents.Max(d => d.Timestamp);
        var isSplitBook = SplitBookRule.IsSplit(book);

        return new BookSummary(book.Id, percentage, lastSyncedAt, isSplitBook, documents);
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
