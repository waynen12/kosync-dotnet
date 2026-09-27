using System.Net;
using System.Text.RegularExpressions;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;

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
        using var client = NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_IssuesCookieAndRedirectsToDashboard()
    {
        using var client = NoRedirectClient();
        var fields = await GetLoginFormFields(client);

        var response = await client.PostAsync(AppRoutes.Login, BuildLoginForm(fields, "admin", "admin"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.Dashboard, response.Headers.Location!.PathAndQuery);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith(".AspNetCore.Cookies="));

        var dashboardResponse = await client.GetAsync(AppRoutes.Dashboard);
        var dashboardHtml = await dashboardResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        Assert.Contains("Logged in as admin.", dashboardHtml);
    }

    [Fact]
    public async Task Login_WithWrongPassword_DoesNotIssueCookie()
    {
        using var client = NoRedirectClient();
        var fields = await GetLoginFormFields(client);

        var response = await client.PostAsync(AppRoutes.Login, BuildLoginForm(fields, "admin", "wrong-password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid username or password.", html);
    }

    [Fact]
    public async Task Login_WithNonAdminAccount_DoesNotIssueCookie()
    {
        await PostAsync("/manage/users", new UserCreateRequest { username = "reader", password = "pw" }, "admin", "admin");

        using var client = NoRedirectClient();
        var fields = await GetLoginFormFields(client);

        var response = await client.PostAsync(AppRoutes.Login, BuildLoginForm(fields, "reader", "pw"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private HttpClient NoRedirectClient()
    {
        return Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static async Task<(string Handler, string AntiforgeryToken)> GetLoginFormFields(HttpClient client)
    {
        var html = await (await client.GetAsync(AppRoutes.Login)).Content.ReadAsStringAsync();

        var handler = Regex.Match(html, "name=\"_handler\" value=\"([^\"]*)\"").Groups[1].Value;
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]*)\"").Groups[1].Value;

        return (handler, token);
    }

    private static FormUrlEncodedContent BuildLoginForm((string Handler, string AntiforgeryToken) fields, string username, string password)
    {
        return new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = fields.Handler,
            ["__RequestVerificationToken"] = fields.AntiforgeryToken,
            ["Input.Username"] = username,
            ["Input.Password"] = password,
        });
    }
}
