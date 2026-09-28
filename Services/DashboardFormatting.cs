namespace Kosync.Services;

// Shared display formatting for the dashboard's card grid and drill-in
// pages (issue #5) - kept here so the Devices and (future) Books views
// render timestamps/percentages the same way.
public static class DashboardFormatting
{
    public static string Percentage(decimal percentage) => Math.Round(percentage * 100) + "%";

    public static string RelativeTime(DateTime? timestamp, DateTime utcNow)
    {
        if (timestamp is null)
        {
            return "Never synced";
        }

        var delta = utcNow - timestamp.Value;

        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return $"{(int)delta.TotalDays}d ago";
    }
}
