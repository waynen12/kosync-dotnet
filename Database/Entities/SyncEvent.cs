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

    // Always true for now - no push is ever rejected yet. Regression protection
    // (issue #3) is what will start setting this false for superseded pushes.
    public bool IsCurrent { get; set; }
}
