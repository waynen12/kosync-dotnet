namespace Kosync.Services;

// Shared between BookDashboardService and DeviceDashboardService so the
// Split Book flag (CONTEXT.md "Split Book") means the same thing wherever
// it's shown inline (issue #8).
public static class SplitBookRule
{
    public static bool IsSplit(Book book)
    {
        var currentPercentages = book.Documents
            .Select(d => d.SyncEvents.Current()?.Percentage)
            .Where(p => p.HasValue)
            .Select(p => p!.Value);

        return IsSplit(currentPercentages);
    }

    public static bool IsSplit(IEnumerable<decimal> currentPercentagesAcrossDocuments)
    {
        var distinctPercentages = currentPercentagesAcrossDocuments.Distinct().Take(2).Count();
        return distinctPercentages > 1;
    }
}
