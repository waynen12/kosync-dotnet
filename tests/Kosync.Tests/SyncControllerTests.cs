using System.Net;
using Kosync.Models;
using Kosync.Tests.TestSupport;

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

    [Fact]
    public async Task CreateUser_WhenRegistrationDisabled_Returns402()
    {
        using var scope = new EnvironmentVariableScope("REGISTRATION_DISABLED", "true");

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Success_Returns201_AndCanAuthenticateWithRawKey()
    {
        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "some-hashed-key" });
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("newuser", body.GetProperty("username").GetString());

        var authRequest = new HttpRequestMessage(HttpMethod.Get, "/users/auth");
        authRequest.SetCredentialsWithRawKey("newuser", "some-hashed-key");
        var authResponse = await Client.SendAsync(authRequest);

        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithExistingUsername_Returns402()
    {
        await PostAsync("/users/create", new UserCreateRequest { username = "dupe", password = "hash" });

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "dupe", password = "hash2" });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
    }

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

    [Fact]
    public async Task GetProgress_WhenDocumentDoesNotExist_Returns502()
    {
        var response = await GetAsync("/syncs/progress/does-not-exist", "admin", "admin");

        Assert.Equal((HttpStatusCode)502, response.StatusCode);
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
}
