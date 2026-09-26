namespace Kosync.Database.Entities;

public class Device
{
    public int Id { get; set; }

    public string DeviceId { get; set; } = default!;

    public string DeviceName { get; set; } = default!;

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public List<SyncEvent> SyncEvents { get; set; } = new();
}
