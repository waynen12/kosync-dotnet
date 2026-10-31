using System.Net;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// An Administrator provisioning accounts is the way Users come into
// existence (ADR 0006), so KOReader's own `POST /users/create` is off unless
// the installer deliberately turns it on - its passwords were never
// Provisional, and leaving it open would be a second account-creation path
// that bypasses the forced change entirely (issue #23).
public class SelfRegistrationTests : IntegrationTestBase
{
    // Explicitly unset rather than merely absent: the variable is read from
    // the process environment, which another test or a developer's shell can
    // have left set.
    private static EnvironmentVariableScope NotConfigured() =>
        new(Constants.EnvironmentVariables.SelfRegistrationEnabled, null);

    private static EnvironmentVariableScope EnabledBy(string value) =>
        new(Constants.EnvironmentVariables.SelfRegistrationEnabled, value);

    private static EnvironmentVariableScope Enabled() =>
        EnabledBy(Constants.EnvironmentVariables.Enabled);

    [Fact]
    public async Task CreateUser_WithNoConfiguration_IsRefused()
    {
        using var scope = NotConfigured();

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithNoConfiguration_SaysWhereAnAccountComesFromInstead()
    {
        using var scope = NotConfigured();

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });
        var body = await BodyAsync(response);

        Assert.Equal(Constants.SelfRegistration.Refused, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CreateUser_WithNoConfiguration_CreatesNobody()
    {
        using var scope = NotConfigured();

        await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });

        Assert.False(Factory.StoredUserExists("newuser"));
    }

    [Fact]
    public async Task CreateUser_WhenEnabled_Returns201_AndCanAuthenticateWithRawKey()
    {
        using var scope = Enabled();

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "some-hashed-key" });
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("newuser", body.GetProperty("username").GetString());

        var authRequest = new HttpRequestMessage(HttpMethod.Get, "/users/auth");
        authRequest.SetCredentialsWithRawKey("newuser", "some-hashed-key");
        var authResponse = await Client.SendAsync(authRequest);

        Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
    }

    // Nobody else picked this password - the person registering typed it into
    // their own KOReader - so it is theirs from the start and there is
    // nothing to force them to replace (ADR 0006).
    [Fact]
    public async Task CreateUser_WhenEnabled_LeavesThePasswordTheUsersOwn()
    {
        using var scope = Enabled();

        await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "some-hashed-key" });

        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf("newuser"));
    }

    [Fact]
    public async Task CreateUser_WhenEnabled_WithExistingUsername_Returns402()
    {
        using var scope = Enabled();

        await PostAsync("/users/create", new UserCreateRequest { username = "dupe", password = "hash" });

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "dupe", password = "hash2" });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
    }

    // The spelling an installer is likeliest to reach for, and the one the
    // replaced variable's documented example used.
    [Fact]
    public async Task CreateUser_WhenExplicitlyFalse_IsRefused()
    {
        using var scope = EnabledBy("false");

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });

        Assert.Equal((HttpStatusCode)402, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WhenEnabledInAnyCasing_IsAllowed()
    {
        using var scope = EnabledBy("True");

        var response = await PostAsync("/users/create", new UserCreateRequest { username = "newuser", password = "hash" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // An Administrator creating a User is the provisioning route ADR 0006
    // chose, so it answers to nothing this flag says.
    [Fact]
    public async Task ManageUsers_IsUnaffectedBySelfRegistrationBeingOff()
    {
        using var scope = NotConfigured();

        var response = await PostAsync(
            "/manage/users",
            new UserCreateRequest { username = "provisioned", password = "pw" },
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Factory.StoredUserExists("provisioned"));
    }
}
