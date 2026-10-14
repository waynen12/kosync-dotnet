using Kosync.Services;

namespace Kosync.Components.Prototype;

// THROWAWAY PROTOTYPE - one shape for both Devices and Books so the three
// dashboard variants can render real data without each re-deriving it.
// Variant C in particular needs them interleaved in a single stream.
// Delete with Components/Prototype/.
public record PrototypeDashboardItem(
    string Kind,
    string Href,
    string Title,
    string Meta,
    decimal Percentage,
    DateTime? LastSyncedAt,
    bool IsFlagged,
    IReadOnlyList<PrototypeDocRow> Documents,
    string SearchText)
{
    public const string DeviceKind = "device";
    public const string BookKind = "book";

    public string RelativeTime => DashboardFormatting.RelativeTime(LastSyncedAt, DateTime.UtcNow);

    public static PrototypeDashboardItem FromDevice(DeviceSummary device)
    {
        var docs = device.Documents
            .Select(d => new PrototypeDocRow(d.DocumentHash, d.Percentage, d.IsSplitBook))
            .ToList();

        return new PrototypeDashboardItem(
            Kind: DeviceKind,
            Href: AppRoutes.DeviceDetail(device.Id),
            Title: device.DeviceName,
            Meta: $"{DashboardFormatting.Pluralize(device.Documents.Count, "document")} synced",
            // A Device has no progress of its own; furthest-along Document
            // stands in, matching the Book-level rule in docs/ROADMAP.md.
            Percentage: docs.Count == 0 ? 0m : docs.Max(d => d.Percentage),
            LastSyncedAt: device.LastSyncedAt,
            IsFlagged: docs.Any(d => d.IsSplit),
            Documents: docs,
            SearchText: $"{device.DeviceName} {device.DeviceId} {string.Join(' ', docs.Select(d => d.Hash))}");
    }

    public static PrototypeDashboardItem FromBook(BookSummary book)
    {
        var docs = book.Documents
            .Select(d => new PrototypeDocRow(d.DocumentHash, d.Percentage, IsSplit: false))
            .ToList();

        return new PrototypeDashboardItem(
            Kind: BookKind,
            Href: AppRoutes.BookDetail(book.Id),
            // Phase 3 (ADR 0007) replaces this with observed/curated metadata.
            Title: $"Book #{book.Id}",
            Meta: $"{DashboardFormatting.Pluralize(book.Documents.Count, "document")} · {DashboardFormatting.Percentage(book.Percentage)} {PrototypeConstants.ProgressSuffix}",
            Percentage: book.Percentage,
            LastSyncedAt: book.LastSyncedAt,
            IsFlagged: book.IsSplitBook,
            Documents: docs,
            SearchText: $"Book #{book.Id} {string.Join(' ', docs.Select(d => d.Hash))}");
    }
}

public record PrototypeDocRow(string Hash, decimal Percentage, bool IsSplit);
