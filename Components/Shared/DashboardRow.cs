using Kosync.Services;

namespace Kosync.Components.Shared;

// One row of the dashboard's dense table (issue #18). Devices and Books are
// different things with different drill-ins, but the table that lists them is
// the same six columns either way, so the mapping happens here rather than
// twice in the markup.
//
// This is the permanent replacement for both `CardGridItem` (which carried a
// per-Document hash row, and the dashboard no longer lists Documents) and the
// prototype's `PrototypeDashboardItem` (which also carried a Meta sentence and
// an interleaved-stream Kind, neither of which the table has a column for).
public record DashboardRow(
    string Icon,
    string Href,
    string Name,
    int DocumentCount,
    decimal Percentage,
    DateTime? LastSyncedAt,
    bool IsSplitBook,
    string SearchText)
{
    public static DashboardRow FromDevice(DeviceSummary device) => new(
        Icon: Constants.Icons.Device,
        Href: AppRoutes.DeviceDetail(device.Id),
        Name: device.DeviceName,
        DocumentCount: device.Documents.Count,
        // A Device has no progress of its own; its furthest-along Document
        // stands in, matching the Book-level rule in docs/ROADMAP.md.
        Percentage: device.Documents.Count == 0 ? 0m : device.Documents.Max(d => d.Percentage),
        LastSyncedAt: device.LastSyncedAt,
        IsSplitBook: device.Documents.Any(d => d.IsSplitBook),
        SearchText: SearchIndex(device.DeviceName, device.DeviceId, device.Documents.Select(d => d.DocumentHash)));

    public static DashboardRow FromBook(BookSummary book) => new(
        Icon: Constants.Icons.BookOpen,
        Href: AppRoutes.BookDetail(book.Id),
        Name: string.Format(Constants.Dashboard.BookNameFormat, book.Id),
        DocumentCount: book.Documents.Count,
        Percentage: book.Percentage,
        LastSyncedAt: book.LastSyncedAt,
        IsSplitBook: book.IsSplitBook,
        SearchText: SearchIndex(
            string.Format(Constants.Dashboard.BookNameFormat, book.Id),
            documentHashes: book.Documents.Select(d => d.DocumentHash)));

    public string RelativeTime => DashboardFormatting.RelativeTime(LastSyncedAt, DateTime.UtcNow);

    public string ProgressText => DashboardFormatting.Percentage(Percentage);

    // What the toolbar's filter matches a row against, client-side. The
    // Document hashes are in here but in no column: a split Book is found by
    // pasting the hash KOReader showed you, which is the one lookup the
    // dashboard still owes Documents now that it has stopped listing them.
    private static string SearchIndex(string name, string? identifier = null, IEnumerable<string>? documentHashes = null) =>
        string.Join(' ', new[] { name, identifier }
            .Concat(documentHashes ?? [])
            .Where(term => !string.IsNullOrEmpty(term)));
}
