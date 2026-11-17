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
// IsFlagged, not IsSplitBook, because the Flags column means different
// things in the two sections and only one of them is a Split Book. A Book
// row is flagged when it is one (GLOSSARY.md); a Device row is flagged when
// it holds a Document that belongs to one. The column says which.
//
// HasMergeSuggestion is a second, independent flag rather than a wider
// IsFlagged: a Book can be both, and the two mean opposite things to the
// reader - a Split Book is a Book already gone wrong, a Merge Suggestion is
// a Book that might be two (ADR 0007). Only a Book row ever carries one.
public record DashboardRow(
    string Icon,
    string Href,
    string Name,
    int DocumentCount,
    decimal Percentage,
    DateTime? LastSyncedAt,
    bool IsFlagged,
    bool HasMergeSuggestion,
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
        IsFlagged: device.Documents.Any(d => d.IsSplitBook),
        HasMergeSuggestion: false,
        SearchText: SearchIndex([device.DeviceName, device.DeviceId, .. device.Documents.Select(d => d.DocumentHash)]));

    public static DashboardRow FromBook(BookSummary book) => new(
        Icon: Constants.Icons.BookOpen,
        Href: AppRoutes.BookDetail(book.Id),
        // Already resolved through BookMetadataRule, including the `Book #N`
        // fallback - the row shows whatever the Book is currently called
        // rather than deciding that for itself.
        Name: book.Title,
        DocumentCount: book.Documents.Count,
        Percentage: book.Percentage,
        LastSyncedAt: book.LastSyncedAt,
        IsFlagged: book.IsSplitBook,
        HasMergeSuggestion: book.MergeSuggestionBookIds.Count > 0,
        // The author joins the index because the table has no column for it.
        // The id does not: it is in the index exactly when it *is* the name,
        // via the `Book #N` fallback Title already carries.
        SearchText: SearchIndex([
            book.Title,
            book.Author ?? string.Empty,
            .. book.Documents.Select(d => d.DocumentHash)]));

    // Whether the Flags column has nothing to put in this row, which is a
    // question about the row rather than about either flag - so the markup
    // asks it here instead of re-deriving it from both.
    public bool HasNoFlags => !IsFlagged && !HasMergeSuggestion;

    public string RelativeTime => DashboardFormatting.RelativeTime(LastSyncedAt, DateTime.UtcNow);

    public string ProgressText => DashboardFormatting.Percentage(Percentage);

    // What the toolbar's filter matches a row against, client-side. The
    // Document hashes are in here but in no column: a split Book is found by
    // pasting the hash KOReader showed you, which is the one lookup the
    // dashboard still owes Documents now that it has stopped listing them.
    private static string SearchIndex(IEnumerable<string> terms) =>
        string.Join(' ', terms.Where(term => !string.IsNullOrEmpty(term)));
}
