using System.Net;
using Kosync.Database.Entities;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #24: a User holding a Provisional Password reaches nothing in
// the portal but the change-password page and logout (ADR 0006).
//
// The gate is middleware rather than a check in MainLayout, so these tests
// ask for HTTP status codes rather than rendered markup: a layout check runs
// per component render and a future page can sidestep it, while middleware
// sees every request. A test that asserted on the page's markup would pass
// just as well against the layout version this deliberately isn't.
//
// The factory seeds the admin the app really boots with - the one whose
// password is still Provisional - because that is the account this gate
// exists for.
public class ProvisionalPasswordPortalGateTests : IntegrationTestBase
{
    protected override KosyncWebApplicationFactory CreateFactory()
    {
        return new KosyncWebApplicationFactory
        {
            AdminPasswordState = PasswordState.Provisional,
        };
    }

    [Fact]
    public async Task Dashboard_WithAProvisionalPassword_RedirectsToChangePassword()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // A relative Location, so read as the original string rather than
        // through PathAndQuery, which only an absolute URI has.
        Assert.Equal(AppRoutes.ChangePassword, response.Headers.Location!.OriginalString);
    }

    // The criterion is "any authenticated route", not "the dashboard", so
    // every route AppRoutes publishes behind the login wall is listed. A
    // route added later is caught by the gate itself - it redirects anything
    // that is not an escape route - but a new line here is what says somebody
    // thought about it.
    [Theory]
    [InlineData(AppRoutes.Dashboard)]
    [InlineData("/dashboard?tab=books")]
    [InlineData("/dashboard/devices/1")]
    [InlineData("/dashboard/books/1")]
    public async Task AuthenticatedRoutes_WithAProvisionalPassword_RedirectToChangePassword(string route)
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.ChangePassword, response.Headers.Location!.OriginalString);
    }

    // Blazor's own endpoints are not routes a User navigates to: answering
    // one with an HTML redirect breaks the very page the gate is sending them
    // to. The script is the one every page in App.razor asks for.
    [Fact]
    public async Task FrameworkEndpoints_WithAProvisionalPassword_AreNotRedirected()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync("/_framework/blazor.web.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The gate is about the portal's cookie session. The KOReader protocol
    // and /manage/* authenticate from headers per request and refuse a
    // Provisional Password themselves, with a 401 that names the portal
    // (ProvisionalPasswordSyncRefusalTests) - so a redirect here would
    // replace a protocol-shaped refusal with an HTML page, and would take
    // PUT /manage/users/password with it.
    [Fact]
    public async Task ManagementApi_WithAProvisionalPassword_IsNotRedirected()
    {
        var response = await GetAsync(
            "/manage/users", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The same request from a browser that also happens to be signed into
    // the portal. A gate that keyed only on "is there a cookie" would answer
    // this with an HTML redirect - and the route it would break that way is
    // PUT /manage/users/password, which ADR 0006 leans on as the way a User
    // with no working portal replaces their password. What makes it exempt is
    // the endpoint being a controller action rather than a portal page, not
    // the absence of a cookie.
    [Fact]
    public async Task ManagementApi_FromABrowserHoldingAPortalCookie_IsNotRedirected()
    {
        using var client = await Factory.SignedInClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/manage/users");
        request.SetCredentials(Constants.Admin.Username, Constants.Admin.DefaultPassword);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // /login is the third escape route, and for a reason of its own: a
    // signed-in User who navigates to it is trying to sign in as somebody
    // else, and bouncing them to a page about the first account's password
    // would leave them no way to do it.
    [Fact]
    public async Task Login_WithAProvisionalPassword_IsStillReachable()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Login);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The escape that actually escapes: reaching /logout is no use if the
    // post that ends the session is the thing the gate turns away.
    [Fact]
    public async Task SigningOut_WithAProvisionalPassword_EndsTheSession()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.LogOutAsync(client);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.Login, response.Headers.Location!.PathAndQuery);

        // Signed out rather than merely redirected: the dashboard now
        // challenges, where a still-live session would have been gated.
        var dashboard = await client.GetAsync(AppRoutes.Dashboard);

        Assert.StartsWith(AppRoutes.Login, dashboard.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task ChangePassword_WithAProvisionalPassword_IsReachable()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.ChangePassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithAProvisionalPassword_IsReachable()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Logout);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // What the gated User actually lands on. Signing in points at the
    // dashboard; the gate is what turns that into the change-password page,
    // so following the redirects is the only way to assert the journey a
    // person takes rather than one hop of it.
    [Fact]
    public async Task SigningIn_WithAProvisionalPassword_LandsOnTheChangePasswordPage()
    {
        var response = await DashboardLogin.LogInAsync(
            Client, Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppRoutes.ChangePassword, response.RequestMessage!.RequestUri!.AbsolutePath);

        var html = await response.Content.ReadAsStringAsync();

        // The gate leaves no trace on the request it lands on, so the page
        // works out for itself that this User has no choice about being here.
        Assert.Contains(Constants.ChangePassword.RequiredNotice, html);
    }

    // The acceptance criterion in one test: the forced change clears the
    // state and hands back both halves of what the Provisional Password was
    // withholding - the portal, and sync.
    [Fact]
    public async Task CompletingTheChange_GrantsThePortalAndSync()
    {
        using var client = await Factory.SignedInClientAsync();

        var changed = await PortalForms.ChangePasswordAsync(
            client, Constants.Admin.DefaultPassword, TestPasswords.Chosen);

        Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
        // Absolute, unlike the gate's own relative redirect: this one comes
        // from NavigationManager rather than from Response.Redirect.
        Assert.Equal(AppRoutes.Dashboard, changed.Headers.Location!.PathAndQuery);

        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf(Constants.Admin.Username));

        // The portal half: the same session now reaches the dashboard it was
        // being bounced away from.
        var dashboard = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        // The sync half: a KOReader device authenticating with the new
        // password is no longer refused (ADR 0006).
        var auth = await GetAsync("/users/auth", Constants.Admin.Username, TestPasswords.Chosen);

        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
    }
}
