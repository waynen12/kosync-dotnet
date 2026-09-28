namespace Kosync.Services;

public record DeviceDocumentSummary(string DocumentHash, decimal Percentage, DateTime Timestamp);

public record DeviceSummary(int Id, string DeviceName, string DeviceId, DateTime? LastSyncedAt, IReadOnlyList<DeviceDocumentSummary> Documents);

// Backs the Devices dashboard view (issue #5): a Device card grid and its
// per-Device drill-in. "Progress" here is always this Device's own most
// recent push for a Document, not the Document's global current pointer -
// another Device may have since pushed further and become current, but this
// view answers "how far did this Device get."
public class DeviceDashboardService
{
    private readonly KosyncDbContext _db;

    public DeviceDashboardService(KosyncDbContext db)
    {
        _db = db;
    }

    public async Task<List<DeviceSummary>> GetDevicesAsync()
    {
        var devices = await _db.Devices
            .Include(d => d.SyncEvents)
                .ThenInclude(s => s.Document)
            .ToListAsync();

        return devices
            .Select(Summarize)
            .OrderByDescending(d => d.LastSyncedAt)
            .ToList();
    }

    public async Task<DeviceSummary?> GetDeviceAsync(int id)
    {
        var device = await _db.Devices
            .Include(d => d.SyncEvents)
                .ThenInclude(s => s.Document)
            .FirstOrDefaultAsync(d => d.Id == id);

        return device is null ? null : Summarize(device);
    }

    private static DeviceSummary Summarize(Device device)
    {
        var documents = device.SyncEvents
            .GroupBy(s => s.DocumentId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).ThenByDescending(s => s.Id).First())
            .OrderByDescending(s => s.Timestamp)
            .Select(s => new DeviceDocumentSummary(s.Document.DocumentHash, s.Percentage, s.Timestamp))
            .ToList();

        var lastSyncedAt = device.SyncEvents.Count == 0
            ? (DateTime?)null
            : device.SyncEvents.Max(s => s.Timestamp);

        return new DeviceSummary(device.Id, device.DeviceName, device.DeviceId, lastSyncedAt, documents);
    }
}
