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
    //
    // Id breaks a tie on the timestamp, so this is arrival order and not
    // merely timestamp order: two pushes stamped in the same instant have no
    // "more recent" between them, and ProgressRegressionRule replays the
    // promotion rule over this sequence - a pair handed back the other way
    // round would be judged against the wrong ceiling.
    public static IEnumerable<SyncEvent> History(this IEnumerable<SyncEvent> syncEvents)
    {
        return syncEvents
            .OrderByDescending(s => s.Timestamp)
            .ThenByDescending(s => s.Id);
    }
}
