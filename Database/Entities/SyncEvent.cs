namespace Kosync.Database.Entities;

public class SyncEvent
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public int DeviceId { get; set; }
    public Device Device { get; set; } = default!;

    public string Progress { get; set; } = default!;

    public decimal Percentage { get; set; }

    public DateTime Timestamp { get; set; }

    // True if this push was at least as far along as the Document's current
    // progress at the time it was received, and so became the current
    // progress pointer. False for a blocked regression - still recorded in
    // history, just never promoted.
    public bool IsCurrent { get; set; }
}
