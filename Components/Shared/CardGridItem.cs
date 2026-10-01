namespace Kosync.Components.Shared;

// Shared shape behind CardGrid.razor (issue #11) - lets the Devices and
// Books tabs feed the same card-grid markup instead of duplicating it.
public record CardGridItem(
    string Href,
    string Title,
    bool ShowSplitFlagOnTitle,
    DateTime? LastSyncedAt,
    string Sub,
    string DocEmptyText,
    IReadOnlyList<CardGridDocRow> DocRows);

public record CardGridDocRow(string DocumentHash, decimal Percentage, bool ShowSplitFlag);
