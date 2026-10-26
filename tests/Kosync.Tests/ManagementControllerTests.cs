using System.Net;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

public class ManagementControllerTests : IntegrationTestBase
{
    [Fact]
    public async Task GetUsers_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/manage/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_NonAdmin_Returns401()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await GetAsync("/manage/users", "reader", "pw");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_Admin_ReturnsUsersWithDocumentCount()
    {
        await CreateSyncingUserAsync("reader", "pw");
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.1m,
            device = "Kobo",
            device_id = "device-1"
        }, "reader", "pw");

        var response = await GetAsync("/manage/users", "admin", "admin");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reader = body.EnumerateArray().Single(u => u.GetProperty("username").GetString() == "reader");
        Assert.Equal(1, reader.GetProperty("documentCount").GetInt32());
        Assert.False(reader.GetProperty("isAdministrator").GetBoolean());
        Assert.True(reader.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task CreateUser_NonAdmin_Returns401()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await PostAsync("/manage/users", new UserCreateRequest { username = "other", password = "pw" }, "reader", "pw");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // An Administrator typed this password and will hand it over, so it is
    // Provisional: it identifies the new User and grants them nothing until
    // they replace it (ADR 0006).
    [Fact]
    public async Task CreateUser_GivesTheNewUserAProvisionalPassword()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Returns400()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw2" }, "admin", "admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_SelfDelete_Returns200()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await DeleteAsync("/manage/users?username=reader", "reader", "pw");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_NonAdminDeletingOther_Returns401()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
        await PostAsync("/manage/users", new UserCreateRequest { username = "other", password = "pw" }, "admin", "admin");

        var response = await DeleteAsync("/manage/users?username=other", "reader", "pw");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_CannotDeleteAdmin_Returns400()
    {
        var response = await DeleteAsync("/manage/users?username=admin", "admin", "admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_Nonexistent_Returns404()
    {
        var response = await DeleteAsync("/manage/users?username=ghost", "admin", "admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocuments_SelfAccess_ReturnsOwnDocuments()
    {
        await CreateSyncingUserAsync("reader", "pw");
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "42%",
            percentage = 0.42m,
            device = "Kobo",
            device_id = "device-1"
        }, "reader", "pw");

        var response = await GetAsync("/manage/users/documents?username=reader", "reader", "pw");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = body.EnumerateArray().Single();
        Assert.Equal("hash1", document.GetProperty("documentHash").GetString());
        Assert.Equal("42%", document.GetProperty("progress").GetString());
        Assert.Equal(0.42m, document.GetProperty("percentage").GetDecimal());
        Assert.Equal("Kobo", document.GetProperty("device").GetString());
        Assert.Equal("device-1", document.GetProperty("deviceId").GetString());
    }

    // Issue #27 / ADR 0004: tenancy is absolute. An Administrator manages
    // accounts and never reads another User's library, so this is refused
    // with exactly the answer a non-Administrator gets - being an
    // Administrator is not a wider version of this endpoint, it is no
    // version of it.
    // The hand-written `d.UserId == user.Id` predicate this endpoint used to
    // carry was replaced by the tenant filters, so the scoping is worth
    // asserting through the new mechanism. A KOReader document hash is
    // derived from the file's contents, so two Users reading the same book
    // really do share one - which makes this the case where an unscoped read
    // returns somebody else's row rather than nothing.
    [Fact]
    public async Task GetDocuments_SelfAccess_ExcludesAnotherUsersSameHashedDocument()
    {
        await CreateSyncingUserAsync("reader", "pw");
        await PushSharedHashAsync("admin", "admin", percentage: 0.10m, deviceId: "admin-device");
        await PushSharedHashAsync("reader", "pw", percentage: 0.90m, deviceId: "reader-device");

        var body = await BodyAsync(await GetAsync("/manage/users/documents?username=admin", "admin", "admin"));

        var document = body.EnumerateArray().Single();
        Assert.Equal(0.10m, document.GetProperty("percentage").GetDecimal());
        Assert.Equal("admin-device", document.GetProperty("deviceId").GetString());
    }

    [Fact]
    public async Task GetDocuments_AdminRequestingAnotherUser_Returns401()
    {
        await CreateSyncingUserAsync("reader", "pw");
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "42%",
            percentage = 0.42m,
            device = "Kobo",
            device_id = "device-1"
        }, "reader", "pw");

        var response = await GetAsync("/manage/users/documents?username=reader", "admin", "admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("hash1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDocuments_OtherNonAdmin_Returns401()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
        await PostAsync("/manage/users", new UserCreateRequest { username = "other", password = "pw" }, "admin", "admin");

        var response = await GetAsync("/manage/users/documents?username=other", "reader", "pw");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUserDocument_Success_RemovesDocument()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.1m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var deleteResponse = await DeleteAsync("/manage/users/documents?username=admin&documentHash=hash1", "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var documentsResponse = await GetAsync("/manage/users/documents?username=admin", "admin", "admin");
        var body = await BodyAsync(documentsResponse);

        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task DeleteUserDocument_NotFound_Returns404()
    {
        var response = await DeleteAsync("/manage/users/documents?username=admin&documentHash=missing", "admin", "admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserActive_TogglesState()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var deactivate = await PutAsync<object?>("/manage/users/active?username=reader", null, "admin", "admin");
        var deactivateBody = await BodyAsync(deactivate);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal("User marked as inactive", deactivateBody.GetProperty("message").GetString());

        var reactivate = await PutAsync<object?>("/manage/users/active?username=reader", null, "admin", "admin");
        var reactivateBody = await BodyAsync(reactivate);
        Assert.Equal("User marked as active", reactivateBody.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateUserActive_CannotToggleAdmin_Returns400()
    {
        var response = await PutAsync<object?>("/manage/users/active?username=admin", null, "admin", "admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePassword_EmptyPassword_Returns400()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await PutAsync("/manage/users/password?username=reader", new PasswordChangeRequest { password = "   " }, "admin", "admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePassword_Success_AllowsAuthWithNewPassword()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        var response = await PutAsync("/manage/users/password?username=reader", new PasswordChangeRequest { password = "new-password" }, "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The password an Administrator just set is Provisional and refuses
        // sync of its own accord (ADR 0006) - asserted by its own test below.
        // What this one is about is which password now opens the account, so
        // the User is taken past the forced change before asking.
        Factory.SetPasswordState("reader", PasswordState.UserChosen);

        var authResponse = await GetAsync("/users/auth", "reader", "new-password");
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    // This endpoint used to refuse any admin target outright, which meant an
    // Administrator could not change their own password by any route at all -
    // and the seeded admin's password is Provisional, so replacing it is the
    // one thing the account has to be able to do (ADR 0006).
    [Fact]
    public async Task UpdatePassword_AdminChangingTheirOwn_AllowsAuthWithNewPassword()
    {
        var response = await PutAsync("/manage/users/password?username=admin", new PasswordChangeRequest { password = "new-password" }, "admin", "admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/users/auth", "admin", "new-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/users/auth", "admin", "admin")).StatusCode);
    }

    // The distinction that replaces the blanket refusal: a password you set
    // for yourself is your own choice and nothing forces you to replace it.
    [Fact]
    public async Task UpdatePassword_AdminChangingTheirOwn_MakesItTheirOwnChoice()
    {
        await PutAsync("/manage/users/password?username=admin", new PasswordChangeRequest { password = "new-password" }, "admin", "admin");

        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf("admin"));
    }

    // Whereas a password an Administrator sets for somebody else is exactly
    // what a Provisional Password is, however the account came to exist. The
    // User here had already replaced their initial password, so this is the
    // state being raised a second time - which is the thing a one-shot
    // "has changed it once" flag could not have expressed (ADR 0006).
    [Fact]
    public async Task UpdatePassword_AdminSettingAnothersPassword_ReturnsItToProvisional()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
        Factory.SetPasswordState("reader", PasswordState.UserChosen);

        await PutAsync("/manage/users/password?username=reader", new PasswordChangeRequest { password = "handed-over" }, "admin", "admin");

        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
    }

    // Pushes the one hash every caller of this shares, which is the point
    // rather than an incidental fixture value: a KOReader hash is derived
    // from the file's contents, so two Users reading the same book really do
    // hold the same hash.
    private Task<HttpResponseMessage> PushSharedHashAsync(string username, string password, decimal percentage, string deviceId)
    {
        return PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "shared-hash",
            progress = "p1",
            percentage = percentage,
            device = deviceId,
            device_id = deviceId,
        }, username, password);
    }
}
