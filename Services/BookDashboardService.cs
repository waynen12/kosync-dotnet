namespace Kosync.Services;

public record SyncEventSummary(string DeviceName, decimal Percentage, DateTime Timestamp, bool IsCurrent);

public record BookDocumentSummary(string DocumentHash, string DeviceName, decimal Percentage, DateTime Timestamp, IReadOnlyList<SyncEventSummary> History);

public record BookSummary(int Id, decimal Percentage, DateTime? LastSyncedAt, bool IsSplitBook, IReadOnlyList<BookDocumentSummary> Documents);

public enum MergeBooksResult
{
    Success,
    SameBook,
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

        foreach (var document in mergeBook.Documents)
        {
            document.BookId = keepBook.Id;
        }
        await _db.SaveChangesAsync();

        _db.Books.Remove(mergeBook);
        await _db.SaveChangesAsync();

        return MergeBooksResult.Success;
    }

    private static BookSummary Summarize(Book book)
    {
        var documents = book.Documents
            .Select(d => new
            {
                d.DocumentHash,
                History = d.SyncEvents.History().Select(s => new SyncEventSummary(s.Device.DeviceName, s.Percentage, s.Timestamp, s.IsCurrent)).ToList()
            })
            .Select(x => new { x.DocumentHash, x.History, Current = x.History.FirstOrDefault(h => h.IsCurrent) })
            .Where(x => x.Current is not null)
            .Select(x => new BookDocumentSummary(x.DocumentHash, x.Current!.DeviceName, x.Current.Percentage, x.Current.Timestamp, x.History))
            .OrderByDescending(d => d.Percentage)
            .ToList();

        var percentage = documents.Count == 0 ? 0m : documents.Max(d => d.Percentage);
        var lastSyncedAt = documents.Count == 0 ? (DateTime?)null : documents.Max(d => d.Timestamp);
        var isSplitBook = SplitBookRule.IsSplit(book);

        return new BookSummary(book.Id, percentage, lastSyncedAt, isSplitBook, documents);
    }
}
