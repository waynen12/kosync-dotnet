using System.Net;
using System.Text.Json;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// A User still holding a Provisional Password cannot sync (ADR 0006). The
// refusal covers the whole KOReader protocol surface, not just the portal:
// a fresh install syncs nothing until somebody has replaced admin/admin, and
// a password an Administrator typed never becomes a device's long-lived
// credential.
//
// These tests ask for the admin the app really seeds - the one whose password
// is still Provisional - because that is what a real install boots with.
public class ProvisionalPasswordSyncRefusalTests : IntegrationTestBase
{
    protected override KosyncWebApplicationFactory CreateFactory()
    {
        return new KosyncWebApplicationFactory
        {
            AdminPasswordState = PasswordState.Provisional,
        };
    }

    [Fact]
    public async Task Auth_WithAProvisionalPassword_Returns401()
    {
        var response = await GetAsync("/users/auth", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncProgress_WithAProvisionalPassword_Returns401()
    {
        var response = await PutAsync(
            "/syncs/progress", APush(), Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Refused before the lookup, so the answer is the same 401 whether or not
    // the Document exists - a Provisional Password cannot be used to probe
    // which hashes are on the server either.
    [Fact]
    public async Task GetProgress_WithAProvisionalPassword_Returns401()
    {
        var response = await GetAsync(
            "/syncs/progress/hash1", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The URL is spelled out here rather than built from PortalOptions, so
    // that quietly changing the shipped default fails a test instead of
    // passing one that recomputes whatever the code now says.
    [Theory]
    [InlineData("/users/auth")]
    [InlineData("/syncs/progress/hash1")]
    public async Task TheRefusal_NamesTheDefaultPortalLoginUrl(string url)
    {
        var response = await GetAsync(url, Constants.Admin.Username, Constants.Admin.DefaultPassword);
        var body = await BodyAsync(response);

        Assert.Contains(DefaultPortalLoginUrl, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TheRefusal_OnAProgressPush_NamesThePortalLoginUrl()
    {
        var response = await PutAsync(
            "/syncs/progress", APush(), Constants.Admin.Username, Constants.Admin.DefaultPassword);
        var body = await BodyAsync(response);

        Assert.Contains(DefaultPortalLoginUrl, body.GetProperty("message").GetString());
    }

    // The other half of the decision, and the one worth protecting: the
    // refusal has to stop once the password is the User's own, or the whole
    // server is bricked. Same credential throughout - replacing a password is
    // what lifts the block, not presenting a different one.
    [Fact]
    public async Task OnceThePasswordIsTheUsersOwn_AllThreeEndpointsWorkAgain()
    {
        Factory.SetPasswordState(Constants.Admin.Username, PasswordState.UserChosen);

        var auth = await GetAsync("/users/auth", Constants.Admin.Username, Constants.Admin.DefaultPassword);
        var push = await PutAsync(
            "/syncs/progress", APush(), Constants.Admin.Username, Constants.Admin.DefaultPassword);
        var read = await GetAsync(
            "/syncs/progress/hash1", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    // Spelled out rather than composed from Constants.Portal, so that changing
    // the shipped default is a test failure to think about rather than a
    // silent recomputation.
    private const string DefaultPortalLoginUrl = "http://localhost:8080/login";

    private static DocumentRequest APush(string documentHash = "hash1") => new()
    {
        document = documentHash,
        progress = "p1",
        percentage = 0.5m,
        device = "Kobo",
        device_id = "device-1",
    };
}

// The same refusal on a server whose operator has said where the portal
// actually is. A separate class because the configured setting *is* the
// fixture here, and IntegrationTestBase.CreateFactory is how a test class
// says so - rather than standing up a second host beside an inherited one.
public class ConfiguredPortalUrlRefusalTests : IntegrationTestBase
{
    // The trailing slash is deliberate: an operator will write one sooner or
    // later, and it must not come back as "example.org//login".
    private const string ConfiguredBaseUrl = "https://kosync.example.org/";

    protected override KosyncWebApplicationFactory CreateFactory()
    {
        return new KosyncWebApplicationFactory
        {
            AdminPasswordState = PasswordState.Provisional,
            PortalBaseUrl = ConfiguredBaseUrl,
        };
    }

    // The shipped default is only ever right for somebody running the server
    // on the machine they are reading the message from; a real install is
    // behind a reverse proxy on a domain the server cannot discover.
    //
    // All three endpoints, not just one: the configured value reaching the
    // message is the whole feature, and a reader only ever sees the endpoint
    // their own device happened to call.
    [Theory]
    [InlineData("/users/auth")]
    [InlineData("/syncs/progress/hash1")]
    public async Task TheRefusal_NamesTheConfiguredPortalUrl(string url)
    {
        var response = await GetAsync(url, Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNamesTheConfiguredPortal(await BodyAsync(response));
    }

    [Fact]
    public async Task TheRefusal_OnAProgressPush_NamesTheConfiguredPortalUrl()
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.5m,
            device = "Kobo",
            device_id = "device-1",
        }, Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNamesTheConfiguredPortal(await BodyAsync(response));
    }

    private static void AssertNamesTheConfiguredPortal(JsonElement body)
    {
        var message = body.GetProperty("message").GetString();

        Assert.Contains("https://kosync.example.org/login", message);

        // The configured value replaced the default rather than sitting
        // alongside it, and the trailing slash did not survive as a double.
        Assert.DoesNotContain("localhost", message);
        Assert.DoesNotContain("org//login", message);
    }
}
