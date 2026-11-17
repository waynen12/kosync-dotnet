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

    // Asserts the message as well as the status, like the two below it: all
    // three ways a credential can fail now answer in the same words, and that
    // is the whole of issue #42.
    [Fact]
    public async Task Auth_WithoutCredentials_Returns401()
    {
        var response = await Client.GetAsync("/users/auth");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            Constants.UserAdmin.InvalidCredentials, body.GetProperty("message").GetString());
    }

    // The message, not just the status: this branch answered "User could not
    // be found" to a User who was there and healthy and had simply mistyped
    // their password, which is an answer that sends them to register a second
    // account rather than retype (issue #42). KOReader shows the server's
    // sentence verbatim, so here the sentence *is* the behaviour - which is
    // why there was nothing to fail when it was wrong.
    [Fact]
    public async Task Auth_WithAWrongAuthKey_Returns401_WithoutClaimingTheUserIsAbsent()
    {
        var response = await GetAsync("/users/auth", Constants.Admin.Username, "wrong-password");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            Constants.UserAdmin.InvalidCredentials, body.GetProperty("message").GetString());
    }

    // The other half of issue #42, for the reason given at the constant.
    // Asserted as "these two answers are indistinguishable" rather than
    // against the constant twice, because that is the property: a later branch
    // that told the two apart would still pass two separate equality
    // assertions.
    [Fact]
    public async Task Auth_WithAnUnknownUsername_IsAnsweredLikeAWrongAuthKey()
    {
        var unknownUser = await GetAsync("/users/auth", "nobody-by-that-name", "any-password");
        var wrongKey = await GetAsync("/users/auth", Constants.Admin.Username, "wrong-password");

        Assert.Equal(wrongKey.StatusCode, unknownUser.StatusCode);
        Assert.Equal(
            (await BodyAsync(wrongKey)).GetProperty("message").GetString(),
            (await BodyAsync(unknownUser)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Auth_WithValidAdminCredentials_ReturnsUsername()
    {
        var response = await GetAsync("/users/auth", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("admin", body.GetProperty("username").GetString());
    }

    // Issue #42 left this message alone on purpose: an Inactive User is
    // something the server does know, and it is only told to a caller whose
    // credential already checked out. Asserted to the letter so that
    // flattening the three refusals into one sentence fails here.
    [Fact]
    public async Task Auth_WithInactiveUser_Returns401_SayingTheUserIsInactive()
    {
        await PostAsync(
            "/manage/users",
            new UserCreateRequest { username = "reader", password = "pw" },
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);
        await PutAsync<object?>(
            "/manage/users/active?username=reader",
            null,
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);

        var response = await GetAsync("/users/auth", "reader", "pw");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Constants.UserAdmin.UserInactive, body.GetProperty("message").GetString());
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
