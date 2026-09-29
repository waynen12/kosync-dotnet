namespace Kosync.Database;

public static class SyncEventQueryExtensions
{
    public static SyncEvent? Current(this IEnumerable<SyncEvent> syncEvents)
    {
        return syncEvents
            .Where(s => s.IsCurrent)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefault();
    }

    // Full push history, most recent first - includes pushes that never
    // became current (IsCurrent false), unlike Current().
    public static IEnumerable<SyncEvent> History(this IEnumerable<SyncEvent> syncEvents)
    {
        return syncEvents.OrderByDescending(s => s.Timestamp);
    }
}
