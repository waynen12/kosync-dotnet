using System.Net;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #43: signing out is one click in the nav, and still a POST.
//
// The confirm screen that used to sit at /logout was never a "are you sure"
// - it was the mechanism that made signing out a form post, because a GET
// sign-out is triggered by anything that merely fetches the URL. These tests
// are therefore split in two: that one click does it, and that the thing the
// click does is still a token-carrying POST.
public class SignOutTests : IntegrationTestBase
{
    // DeviceDetail is the probe page, as in AppShellViewTests: it renders the
    // app shell with no fixture data at all, so what is under test is the nav
    // rather than anything a page put there.
    [Fact]
    public async Task TheNavControl_IsAFormPostCarryingAnAntiforgeryToken()
    {
        using var client = await Factory.SignedInClientAsync();

        var html = await (await client.GetAsync(AppRoutes.DeviceDetail(1))).Content.ReadAsStringAsync();

        var form = PortalForms.SignOutFormIn(html);

        // A real submit button rather than a link: the whole point is that the
        // browser has to be told to post, not merely to fetch.
        Assert.Contains("type=\"submit\"", form);
        Assert.Contains("name=\"__RequestVerificationToken\"", form);

        // The interstitial, gone. An <a href="/logout"> anywhere in the shell
        // would be a one-click sign-out that a link preview can also perform.
        Assert.DoesNotContain($"href=\"{AppRoutes.Logout}\"", html);
    }

    [Fact]
    public async Task ThePost_EndsTheSessionAndLandsOnLogin()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.LogOutAsync(client);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.Login, response.Headers.Location!.OriginalString);

        // Signed out rather than merely redirected: the dashboard was
        // reachable a moment ago and now challenges.
        var dashboard = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.Found, dashboard.StatusCode);
        Assert.StartsWith(AppRoutes.Login, dashboard.Headers.Location!.PathAndQuery);
    }

    // The reason the deleted confirm screen ever existed, asserted directly
    // now that no screen is enforcing it. Anything that merely *fetches* a
    // URL - a link preview, a prefetching browser, an <img> on a page the
    // User is reading - must not be able to end a session.
    [Fact]
    public async Task AGetOfTheUrl_DoesNotEndTheSession()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Logout);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        await AssertStillSignedIn(client);
    }

    // The other half, and the one no HTTP method can enforce: a post from a
    // page the User never meant to submit. Without the token this is
    // logout-by-CSRF, which is the attack a form post on its own does not
    // answer.
    [Fact]
    public async Task APostWithNoAntiforgeryToken_DoesNotEndTheSession()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.PostAsync(
            AppRoutes.Logout, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertStillSignedIn(client);
    }

    // Who signed out, which an operator reading a log is entitled to. Also
    // the one assertion that pins the order inside PortalSignOut: signing out
    // is what empties HttpContext.User, so a username read afterwards is
    // blank and the line names nobody.
    [Fact]
    public async Task ThePost_NamesTheUserInTheLog()
    {
        using var client = await Factory.SignedInClientAsync();

        await PortalForms.LogOutAsync(client);

        Assert.Contains(
            Factory.Logs.Entries,
            entry => entry.Category == Constants.Logout.LogCategory
                && entry.Message.Contains($"[{Constants.Admin.Username}]")
                && entry.Message.Contains("signed out"));
    }

    // Nothing to end, and nothing that should look like an error either: the
    // endpoint is behind the same [Authorize] the deleted page carried, so an
    // anonymous post is challenged rather than run.
    [Fact]
    public async Task APostWithNoSession_IsSentToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.PostAsync(
            AppRoutes.Logout, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        // Absolute, because this redirect is the cookie handler's challenge
        // rather than anything in this app's own code.
        Assert.Equal(AppRoutes.Login, response.Headers.Location!.AbsolutePath);
    }

    private static async Task AssertStillSignedIn(HttpClient client)
    {
        var dashboard = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }
}
