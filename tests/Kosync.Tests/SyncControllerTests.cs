using System.Net;
using System.Text.Json;
using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

public class SyncControllerTests : IntegrationTestBase
{
    [Fact]
    public async Task Index_ReturnsRunningMessage()
    {
        var response = await Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("kosync-dotnet server is running.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await Client.GetAsync("/healthcheck");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Auth_WithoutCredentials_Returns401()
    {
        var response = await Client.GetAsync("/users/auth");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_WithInvalidCredentials_Returns401()
    {
        var response = await GetAsync("/users/auth", "admin", "wrong-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Auth_WithValidAdminCredentials_ReturnsUsername()
    {
        var response = await GetAsync("/users/auth", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("admin", body.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Auth_WithInactiveUser_Returns401()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
        await PutAsync<object?>("/manage/users/active?username=reader", null, "admin", "admin");

        var response = await GetAsync("/users/auth", "reader", "pw");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // `POST /users/create` has its own file - SelfRegistrationTests - because
    // every one of its cases now turns on whether the installer opted in
    // (issue #23).

    [Fact]
    public async Task SyncProgress_Unauthenticated_Returns401()
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.5m,
            device = "Kobo",
            device_id = "device-1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncProgress_Success_ReturnsDocumentAndTimestamp()
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.5m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hash1", body.GetProperty("document").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task GetProgress_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/syncs/progress/hash1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Was GetProgress_WhenDocumentDoesNotExist_Returns502: kept as the
    // regression guard rather than replaced, now asserting the contract the
    // reference server actually has
    // (Constants.SyncProtocol.EmptyProgressBody, issue #41).
    [Fact]
    public async Task GetProgress_WhenDocumentDoesNotExist_Returns200WithEmptyBody()
    {
        var response = await GetAsync("/syncs/progress/does-not-exist", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
        Assert.Empty(body.EnumerateObject());
    }

    // The ordering a client that pulls before it pushes uses: one "sync"
    // action, pulling first and pushing second. That first pull is every
    // book's first sync, so a client reading it as a hard failure never
    // reaches the push - which is how the 502 stopped a reader syncing at
    // all (issue #41).
    [Fact]
    public async Task GetProgress_PullBeforeFirstPush_DoesNotBlockThePush()
    {
        var firstPull = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var firstPullBody = await BodyAsync(firstPull);

        Assert.Equal(HttpStatusCode.OK, firstPull.StatusCode);
        Assert.Empty(firstPullBody.EnumerateObject());

        var push = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Xteink",
            device_id = "device-1"
        }, "admin", "admin");

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var secondPull = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var secondPullBody = await BodyAsync(secondPull);

        Assert.Equal(HttpStatusCode.OK, secondPull.StatusCode);
        Assert.Equal("hash1", secondPullBody.GetProperty("document").GetString());
        Assert.Equal("p1", secondPullBody.GetProperty("progress").GetString());
        Assert.Equal(0.25m, secondPullBody.GetProperty("percentage").GetDecimal());
        Assert.Equal("device-1", secondPullBody.GetProperty("device_id").GetString());
    }

    // The other way a Document reaches "no current progress": it has synced
    // before and a Progress Reset cleared the pointer. Same branch, same
    // answer - a reader who restarts a book is back to the first-sync case,
    // not looking at a server fault.
    [Fact]
    public async Task GetProgress_AfterProgressReset_Returns200WithEmptyBody()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        await ResetProgressAsync("hash1");

        var response = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body.EnumerateObject());
    }

    [Fact]
    public async Task GetProgress_ReturnsFieldsFromLastPush()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.75m,
            device = "Kobo2",
            device_id = "device-2"
        }, "admin", "admin");

        var response = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hash1", body.GetProperty("document").GetString());
        Assert.Equal("p2", body.GetProperty("progress").GetString());
        Assert.Equal(0.75m, body.GetProperty("percentage").GetDecimal());
        Assert.Equal("Kobo2", body.GetProperty("device").GetString());
        Assert.Equal("device-2", body.GetProperty("device_id").GetString());
        Assert.True(body.GetProperty("timestamp").GetInt64() > 0);
    }

    [Fact]
    public async Task SyncProgress_IncreasingPush_BecomesCurrent()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hash1", body.GetProperty("document").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));

        var progress = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var progressBody = await BodyAsync(progress);
        Assert.Equal("p2", progressBody.GetProperty("progress").GetString());
        Assert.Equal(0.50m, progressBody.GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task SyncProgress_EqualPush_BecomesCurrent()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.50m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kobo2",
            device_id = "device-2"
        }, "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hash1", body.GetProperty("document").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));

        var progress = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var progressBody = await BodyAsync(progress);
        Assert.Equal("p2", progressBody.GetProperty("progress").GetString());
        Assert.Equal("device-2", progressBody.GetProperty("device_id").GetString());
    }

    [Fact]
    public async Task SyncProgress_RegressingPush_DoesNotBecomeCurrent_ButResponseIsIdentical()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.75m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.25m,
            device = "Kobo2",
            device_id = "device-2"
        }, "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hash1", body.GetProperty("document").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));

        var progress = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var progressBody = await BodyAsync(progress);
        Assert.Equal("p1", progressBody.GetProperty("progress").GetString());
        Assert.Equal(0.75m, progressBody.GetProperty("percentage").GetDecimal());
        Assert.Equal("device-1", progressBody.GetProperty("device_id").GetString());
    }

    [Fact]
    public async Task SyncProgress_RegressionProtection_IsScopedToSameDocument()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.90m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2",
            progress = "p1",
            percentage = 0.10m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var progress = await GetAsync("/syncs/progress/hash2", "admin", "admin");
        var progressBody = await BodyAsync(progress);
        Assert.Equal("p1", progressBody.GetProperty("progress").GetString());
        Assert.Equal(0.10m, progressBody.GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task SyncProgress_PromotingANewPush_SupersedesThePreviousCurrentEvent()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var events = await db.SyncEvents
            .Where(e => e.Document.DocumentHash == "hash1")
            .ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.Single(events, e => e.IsCurrent);
        Assert.Equal("p2", events.Single(e => e.IsCurrent).Progress);
    }
}
