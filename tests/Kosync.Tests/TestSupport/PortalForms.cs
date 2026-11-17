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

    // Sign-out is the one portal form that is not a Static SSR named form and
    // has no page of its own (issue #43): it is a plain <form method="post">
    // in the chrome, posting to an endpoint. So the round trip is a page apart
    // - the token is scraped from whatever page the control is rendered on,
    // and posted to /logout.
    //
    // The token comes off /change-password, because it is the one page
    // carrying the control that *every* signed-in User can reach: a User
    // holding a Provisional Password is gated away from the app shell the nav
    // lives in.
    public static async Task<HttpResponseMessage> LogOutAsync(HttpClient client)
    {
        var html = await (await client.GetAsync(AppRoutes.ChangePassword)).Content.ReadAsStringAsync();

        return await client.PostAsync(AppRoutes.Logout, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = AntiforgeryTokenIn(SignOutFormIn(html)),
            }));
    }

    // The sign-out form in a rendered page, found by where it posts rather
    // than by a form name: it is not a named form, and every page that
    // carries it also carries a form of its own.
    public static string SignOutFormIn(string html) =>
        FormIn(html, $"action=\"{AppRoutes.Logout}\"");

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

        values["_handler"] = formName;
        values["__RequestVerificationToken"] = AntiforgeryTokenIn(FormIn(html, HandlerField(formName)));

        return await client.PostAsync(url, new FormUrlEncodedContent(values));
    }

    public static async Task<(string Handler, string AntiforgeryToken)> FieldsOf(HttpClient client, string url)
    {
        var html = await (await client.GetAsync(url)).Content.ReadAsStringAsync();

        var handler = Regex.Match(html, "name=\"_handler\" value=\"([^\"]*)\"").Groups[1].Value;

        return (handler, AntiforgeryTokenIn(html));
    }

    // The one form on a page that contains the given marker - the hidden
    // handler field for a named form, the action for the sign-out control.
    // Single rather than first, so a marker that matches two forms is a
    // failed assertion rather than a test quietly submitting the wrong one.
    private static string FormIn(string html, string marker)
    {
        var form = Regex.Matches(html, "<form.*?</form>", RegexOptions.Singleline)
            .SingleOrDefault(match => match.Value.Contains(marker));

        Assert.NotNull(form);

        return form.Value;
    }

    private static string HandlerField(string formName) => $"name=\"_handler\" value=\"{formName}\"";

    // Takes a form rather than a page wherever there is more than one, since
    // every form carries a token of its own and the first in the document is
    // not necessarily the one being submitted.
    private static string AntiforgeryTokenIn(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]*)\"").Groups[1].Value;
}
