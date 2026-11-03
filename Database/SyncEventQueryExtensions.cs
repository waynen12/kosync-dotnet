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
}
