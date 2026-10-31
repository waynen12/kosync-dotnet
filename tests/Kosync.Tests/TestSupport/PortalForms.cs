using System.Text.RegularExpressions;

namespace Kosync.Tests.TestSupport;

// Every form in the portal is a Static SSR "named form" (Blazor's non-JS
// form-post mechanism), so a test has to do what a browser does: GET the
// page to scrape its antiforgery token and form handler name, then POST them
// back the way the rendered form would submit them. This is that round trip,
// for any page - DashboardLogin wraps the /login case, which predates the
// other two and reads better named after what it does.
public static class PortalForms
{
    public static async Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client, string? currentPassword, string? newPassword, string? confirmPassword = null)
    {
        return await SubmitAsync(client, AppRoutes.ChangePassword, new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = currentPassword ?? string.Empty,
            ["Input.NewPassword"] = newPassword ?? string.Empty,
            // Defaults to the new password, so a test about some other rule
            // does not have to repeat it to get past the confirmation.
            ["Input.ConfirmPassword"] = confirmPassword ?? newPassword ?? string.Empty,
        });
    }

    // The sign-out screen's only field is the hidden pair every named form
    // carries, which is exactly what makes it a post rather than a link: a
    // GET sign-out is triggered by anything that merely fetches the URL.
    public static async Task<HttpResponseMessage> LogOutAsync(HttpClient client)
    {
        return await SubmitAsync(client, AppRoutes.Logout, new Dictionary<string, string>());
    }

    // Scrapes the page's form fields, then posts the given values back to it.
    public static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, string url, Dictionary<string, string> values)
    {
        var fields = await FieldsOf(client, url);

        values["_handler"] = fields.Handler;
        values["__RequestVerificationToken"] = fields.AntiforgeryToken;

        return await client.PostAsync(url, new FormUrlEncodedContent(values));
    }

    // The same round trip against one named form on a page that has several
    // - the Users page renders one per User (issue #28), so "the form on the
    // page" is no longer a thing a test can mean. Each form carries its own
    // antiforgery token, so the token has to come from inside the form being
    // submitted rather than from the first one in the document.
    public static async Task<HttpResponseMessage> SubmitNamedAsync(
        HttpClient client, string url, string formName, Dictionary<string, string> values)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();

        var form = Regex.Matches(html, "<form.*?</form>", RegexOptions.Singleline)
            .SingleOrDefault(match => match.Value.Contains($"name=\"_handler\" value=\"{formName}\""));

        Assert.NotNull(form);

        values["_handler"] = formName;
        values["__RequestVerificationToken"] =
            Regex.Match(form.Value, "name=\"__RequestVerificationToken\" value=\"([^\"]*)\"").Groups[1].Value;

        return await client.PostAsync(url, new FormUrlEncodedContent(values));
    }

    public static async Task<(string Handler, string AntiforgeryToken)> FieldsOf(HttpClient client, string url)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();

        var handler = Regex.Match(html, "name=\"_handler\" value=\"([^\"]*)\"").Groups[1].Value;
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]*)\"").Groups[1].Value;

        return (handler, token);
    }
}
