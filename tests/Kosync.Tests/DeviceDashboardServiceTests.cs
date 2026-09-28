using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Unit-level coverage for the aggregation logic behind the Devices view
// (issue #5) that's awkward to pin down through rendered HTML alone: which
// SyncEvent wins per Document, and how Devices are ordered.
public class DeviceDashboardServiceTests : IntegrationTestBase
{
    [Fact]
    public async Task GetDevicesAsync_PerDocumentProgressIsTheDevicesMostRecentPush()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p2", percentage = 0.30m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var devices = await GetDevicesAsync();

        var device = Assert.Single(devices);
        var doc = Assert.Single(device.Documents);
        Assert.Equal("hash1", doc.DocumentHash);
        Assert.Equal(0.30m, doc.Percentage);
    }

    [Fact]
    public async Task GetDevicesAsync_OrdersDevicesByMostRecentlySyncedFirst()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Old Device", device_id = "device-old"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.10m, device = "New Device", device_id = "device-new"
        }, "admin", "admin");

        var devices = await GetDevicesAsync();

        Assert.Equal(["New Device", "Old Device"], devices.Select(d => d.DeviceName));
    }

    [Fact]
    public async Task GetDeviceAsync_UnknownId_ReturnsNull()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new DeviceDashboardService(db);

        var device = await service.GetDeviceAsync(999);

        Assert.Null(device);
    }

    private async Task<List<DeviceSummary>> GetDevicesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new DeviceDashboardService(db);
        return await service.GetDevicesAsync();
    }
}
