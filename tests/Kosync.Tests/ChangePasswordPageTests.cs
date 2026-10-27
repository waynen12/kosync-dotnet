using System.Net;
using Kosync.Database.Entities;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #24: the change-password page itself - what it says, what it
// refuses, and the sign-out escape beside it.
//
// The default fixture, so the admin here has already replaced their seeded
// password: the page is not only the forced first change, it is also how a
// User changes a password they chose. What the *gate* does to a User who
// hasn't is ProvisionalPasswordPortalGateTests' subject.
public class ChangePasswordPageTests : IntegrationTestBase
{
    [Fact]
    public async Task Page_WarnsThatTheChangeInvalidatesEveryDevice()
    {
        using var client = await Factory.SignedInClientAsync();

        var html = await (await client.GetAsync(AppRoutes.ChangePassword)).Content.ReadAsStringAsync();

        // ADR 0006 accepted that one credential does both jobs *in exchange
        // for* this warning, which is why it is asserted rather than left to
        // the design. Read from the constant the page renders so the copy can
        // be reworded without the test going quiet about its absence.
        Assert.Contains(Constants.ChangePassword.DeviceInvalidationWarning, html);
        Assert.Contains("role=\"alert\"", html);

        // The forced-change notice is the gate's explanation, and this User
        // was not gated.
        Assert.DoesNotContain(Constants.ChangePassword.RequiredNotice, html);
    }

    [Fact]
    public async Task Page_RendersTheHubCardAndNoPrototypeScaffolding()
    {
        using var client = await Factory.SignedInClientAsync();

        var html = await (await client.GetAsync(AppRoutes.ChangePassword)).Content.ReadAsStringAsync();

        // The same centred card /login wears - issue #24 asks for this screen
        // in the folded-in design language, matching it.
        Assert.Contains("hub-login-card", html);
        Assert.Contains(Constants.ChangePassword.CurrentPasswordLabel, html);
        Assert.Contains(Constants.ChangePassword.NewPasswordLabel, html);
        Assert.Contains(Constants.ChangePassword.ConfirmPasswordLabel, html);
        Assert.Contains(Constants.ChangePassword.SubmitText, html);

        Assert.DoesNotContain("prototype", html, StringComparison.OrdinalIgnoreCase);
    }

    // The fold-in hazard DesignSystemTests and LoginViewTests both guard: a
    // `hub-` class app.css does not define renders as unstyled markup, and no
    // markup assertion can see it. Both states, because the error block only
    // exists on a refused post.
    [Fact]
    public async Task Page_RendersOnlyClassesThatAppCssDefines()
    {
        using var client = await Factory.SignedInClientAsync();

        var html = await (await client.GetAsync(AppRoutes.ChangePassword)).Content.ReadAsStringAsync()
            + await (await PortalForms.ChangePasswordAsync(client, "wrong", TestPasswords.Chosen))
                .Content.ReadAsStringAsync()
            + await (await client.GetAsync(AppRoutes.Logout)).Content.ReadAsStringAsync();

        var rendered = HubClasses.RenderedIn(html);

        Assert.NotEmpty(rendered);
        Assert.Empty(rendered.Except(HubClasses.DefinedInAppCss()));
    }

    [Fact]
    public async Task WithoutASession_RedirectsToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.ChangePassword);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    // Holding a session is not proof that the person at the keyboard knows
    // the password, and this is the request that replaces it.
    [Fact]
    public async Task WithTheWrongCurrentPassword_RefusesAndLeavesTheCredentialAlone()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.ChangePasswordAsync(client, "not-the-password", TestPasswords.Chosen);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Constants.ChangePassword.CurrentPasswordWrongError, html);

        // The old credential still works where it counts - on a device.
        var auth = await GetAsync("/users/auth", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
    }

    [Fact]
    public async Task WithAMismatchedConfirmation_Refuses()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.ChangePasswordAsync(
            client, Constants.Admin.DefaultPassword, TestPasswords.Chosen, "a-different-password");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(Constants.ChangePassword.ConfirmationMismatchError, html);

        var auth = await GetAsync("/users/auth", Constants.Admin.Username, Constants.Admin.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
    }

    // The rule that matters for a Provisional Password: re-typing it would
    // otherwise record it as the User's own choice while leaving the
    // credential somebody else picked in place (ADR 0006).
    [Fact]
    public async Task WithThePasswordUnchanged_Refuses()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.ChangePasswordAsync(
            client, Constants.Admin.DefaultPassword, Constants.Admin.DefaultPassword);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(Constants.ChangePassword.UnchangedPasswordError, html);
        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf(Constants.Admin.Username));
    }

    [Fact]
    public async Task WithAnEmptyNewPassword_Refuses()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.ChangePasswordAsync(client, Constants.Admin.DefaultPassword, "   ");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(Constants.ChangePassword.EmptyPasswordError, html);
    }

    [Fact]
    public async Task WithTooShortANewPassword_RefusesAndNamesTheMinimum()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.ChangePasswordAsync(client, Constants.Admin.DefaultPassword, "short");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(
            string.Format(
                Constants.ChangePassword.TooShortErrorFormat,
                Constants.Passwords.DefaultMinimumPasswordLength),
            html);
    }

    // What the sign-out post itself does is asserted in
    // ProvisionalPasswordPortalGateTests, where it is also the escape route
    // the gate has to let through.

    // A User whose password is their own is not gated - the dashboard is
    // reachable, and the change-password page is somewhere they chose to go.
    [Fact]
    public async Task WithAUserChosenPassword_TheDashboardIsNotGated()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A page nothing links to is a page only the gate can reach, which would
    // leave a User who chose their own password no way to change it. The nav
    // held placeholders for both of these until the destinations existed
    // (MainLayout.razor).
    [Fact]
    public async Task TheAppShell_LinksToBothPages()
    {
        using var client = await Factory.SignedInClientAsync();

        var html = await (await client.GetAsync(AppRoutes.DeviceDetail(1))).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"{AppRoutes.ChangePassword}\"", html);
        Assert.Contains($"href=\"{AppRoutes.Logout}\"", html);
    }
}
