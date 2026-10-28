namespace Kosync.Tests.TestSupport;

// Shared cookie-login flow for tests exercising pages past the /login wall.
// The scraping and posting of a Static SSR named form is PortalForms'
// business - every portal form works that way; what lives here is the one
// thing specific to /login, which is the pair of fields it carries.
public static class DashboardLogin
{
    public static async Task<HttpResponseMessage> LogInAsync(HttpClient client, string username, string password)
    {
        return await PortalForms.SubmitAsync(client, AppRoutes.Login, new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = password,
        });
    }

    // That a sign-in actually issued a session, which is the half of "logged
    // in" a redirect to the dashboard does not prove. Lives here rather than
    // in any one test class because the cookie's name is the authentication
    // scheme's business (ADR 0002), not any single test's.
    public static void AssertIssuedASessionCookie(HttpResponseMessage response)
    {
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith(SessionCookiePrefix));
    }

    private const string SessionCookiePrefix = ".AspNetCore.Cookies=";

    // A client that has signed in and does not follow redirects, which is
    // what a test asserting on a redirect needs - and every test past the
    // login wall needs the first half.
    public static async Task<HttpClient> SignedInClientAsync(
        this KosyncWebApplicationFactory factory, string? username = null, string? password = null)
    {
        var client = factory.NoRedirectClient();

        await LogInAsync(
            client,
            username ?? Constants.Admin.Username,
            password ?? Constants.Admin.DefaultPassword);

        return client;
    }
}
