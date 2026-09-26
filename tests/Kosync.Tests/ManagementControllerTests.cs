using System.Net;
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
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
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
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");
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

        var authResponse = await GetAsync("/users/auth", "reader", "new-password");
        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatePassword_CannotUpdateAdmin_Returns400()
    {
        var response = await PutAsync("/manage/users/password?username=admin", new PasswordChangeRequest { password = "new-password" }, "admin", "admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
