namespace Kosync.Services;

public record SyncEventSummary(string DeviceName, decimal Percentage, DateTime Timestamp, bool IsCurrent);

public record BookDocumentSummary(string DocumentHash, string DeviceName, decimal Percentage, DateTime Timestamp, IReadOnlyList<SyncEventSummary> History);

public record BookSummary(int Id, decimal Percentage, DateTime? LastSyncedAt, IReadOnlyList<BookDocumentSummary> Documents);

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

        return new BookSummary(book.Id, percentage, lastSyncedAt, documents);
    }
}
