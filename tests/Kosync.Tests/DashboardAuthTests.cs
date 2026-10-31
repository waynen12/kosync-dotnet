using System.Net;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #4: Blazor Web App hosting + /login. The login form is a
// Static SSR "named form" (Blazor's non-JS form-post mechanism), so a real
// browser round trip is simulated here: GET /login to scrape the antiforgery
// token and form handler name, then POST them back like the rendered form
// would submit them.
public class DashboardAuthTests : IntegrationTestBase
{
    [Fact]
    public async Task Login_Get_ReturnsStaticFormWithoutOpeningCircuit()
    {
        var response = await Client.GetAsync(AppRoutes.Login);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<form method=\"post\" action=\"{AppRoutes.Login}\">", html);
        Assert.DoesNotContain("Blazor:{\"type\":\"server\"", html);
    }

    [Fact]
    public async Task Dashboard_WithoutSession_RedirectsToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_IssuesCookieAndRedirectsToDashboard()
    {
        using var client = Factory.NoRedirectClient();

        var response = await DashboardLogin.LogInAsync(client, "admin", "admin");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.Dashboard, response.Headers.Location!.PathAndQuery);
        DashboardLogin.AssertIssuedASessionCookie(response);

        var dashboardResponse = await client.GetAsync(AppRoutes.Dashboard);
        var dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        // The signed-in Username as the folded-in nav renders it: bare, in
        // `hub-nav-user`, where the pre-hub shell wrote a "Logged in as ..."
        // sentence. Asserted with its element, not as a bare ">admin<" - that
        // was a deliberately loose assertion while the copy was in flux
        // during the UI prototype, and the nav has been settled since #17.
        //
        // A literal class name, like every other `hub-` assertion in the
        // suite: these names are markup structure rather than copy, so the
        // stylesheet is the other half of the pair and HubClasses is what
        // checks the two agree. Constants.cs carries the copy instead.
        Assert.Contains(@"<span class=""hub-nav-user"">admin</span>", dashboardHtml);
    }

    [Fact]
    public async Task Login_WithWrongPassword_DoesNotIssueCookie()
    {
        using var client = Factory.NoRedirectClient();

        var response = await DashboardLogin.LogInAsync(client, "admin", "wrong-password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var html = await response.Content.ReadAsStringAsync();
        // Read from the same constant the login screen renders, so the
        // assertion can't drift from the copy (CLAUDE.md). The string lives
        // in Constants.cs rather than the prototype's own constants, which
        // is what unblocked deleting that folder (issue #13, then #19).
        Assert.Contains(Constants.LoginErrorText, html);
    }

    // Issue #36: a post that carries the handler and the antiforgery token but
    // none of the form's own fields. Blazor binds the form model to null for
    // such a body - which is what BL0008 warns about - and /login is an
    // unauthenticated endpoint, so an unhandled NullReferenceException there
    // is a 500 anyone can ask for.
    [Fact]
    public async Task Login_WithNoFormFields_FailsTheLoginRatherThanThrowing()
    {
        using var client = Factory.NoRedirectClient();

        var response = await PortalForms.SubmitAsync(client, AppRoutes.Login, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(Constants.LoginErrorText, html);
    }

    // A non-Administrator logging in used to be refused here. Issue #26
    // reversed that - every User logs into the portal with their sync
    // credentials (ADR 0004) - and PortalLoginForEveryUserTests owns the
    // behaviour now, including the one precondition that survived it
    // (an inactive account).
}
