using System.Text.RegularExpressions;

namespace Kosync.Tests.TestSupport;

// Shared cookie-login flow for tests exercising pages past the /login
// wall. The login form is a Static SSR "named form" (Blazor's non-JS
// form-post mechanism), so a real browser round trip is simulated: GET
// /login to scrape the antiforgery token and form handler name, then POST
// them back like the rendered form would submit them.
public static class DashboardLogin
{
    public static async Task<HttpResponseMessage> LogInAsync(HttpClient client, string username, string password)
    {
        var fields = await GetLoginFormFields(client);
        return await client.PostAsync(AppRoutes.Login, BuildLoginForm(fields, username, password));
    }

    public static async Task<(string Handler, string AntiforgeryToken)> GetLoginFormFields(HttpClient client)
    {
        var html = await (await client.GetAsync(AppRoutes.Login)).Content.ReadAsStringAsync();

        var handler = Regex.Match(html, "name=\"_handler\" value=\"([^\"]*)\"").Groups[1].Value;
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]*)\"").Groups[1].Value;

        return (handler, token);
    }

    public static FormUrlEncodedContent BuildLoginForm((string Handler, string AntiforgeryToken) fields, string username, string password)
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
