using System.Net;
using Kosync.Database;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #5: the Devices card-grid landing page and its per-Device
// drill-in detail page.
public class DevicesViewTests : IntegrationTestBase
{
    [Fact]
    public async Task Dashboard_WithoutDevices_ShowsEmptyState()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.Dashboard)).Content.ReadAsStringAsync();

        Assert.Contains("No devices have synced yet.", html);
    }

    [Fact]
    public async Task Dashboard_ShowsDeviceCardWithLastSyncedAndPerDocumentProgress()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.Dashboard)).Content.ReadAsStringAsync();

        Assert.Contains("Kobo Nova", html);
        Assert.Contains("hash1", html);
        Assert.Contains("42%", html);
        Assert.Contains("just now", html);
        Assert.DoesNotContain("No devices have synced yet.", html);
    }

    [Fact]
    public async Task DeviceCard_LinksToItsRoutedDetailPage()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        var deviceId = await GetDeviceIdAsync("device-1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.Dashboard)).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"{AppRoutes.DeviceDetail(deviceId)}\"", html);
    }

    [Fact]
    public async Task DeviceDetail_ListsEveryDocumentPushedByThatDeviceWithHashAndProgress()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.10m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2",
            progress = "p2",
            percentage = 0.75m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        var deviceId = await GetDeviceIdAsync("device-1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DeviceDetail(deviceId))).Content.ReadAsStringAsync();

        Assert.Contains("Kobo Nova", html);
        Assert.Contains("hash1", html);
        Assert.Contains("10%", html);
        Assert.Contains("hash2", html);
        Assert.Contains("75%", html);
    }

    [Fact]
    public async Task DeviceDetail_FlagsADocumentBelongingToASplitBookInline()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        await MergeIntoOneBookAsync("hash1", "hash2");

        var deviceId = await GetDeviceIdAsync("device-1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DeviceDetail(deviceId))).Content.ReadAsStringAsync();

        Assert.Contains(Constants.SplitBookFlag, html);
    }

    [Fact]
    public async Task DeviceDetail_UnknownDevice_ShowsNotFoundMessage()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var response = await client.GetAsync(AppRoutes.DeviceDetail(999));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Device not found.", html);
    }

    [Fact]
    public async Task DeviceDetail_WithoutSession_RedirectsToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.DeviceDetail(1));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    private async Task<int> GetDeviceIdAsync(string deviceId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var device = await db.Devices.SingleAsync(d => d.DeviceId == deviceId);
        return device.Id;
    }
}
