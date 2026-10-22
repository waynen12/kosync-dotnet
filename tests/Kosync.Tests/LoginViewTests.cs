using System.Text.RegularExpressions;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #16: /login takes the hub design (the LoginVariantA fold-in).
//
// The credential check and sign-in flow are DashboardAuthTests' subject and
// are unchanged by the fold-in; what is asserted here is the rendering - that
// the hub login card is what /login actually serves, and that it carries no
// throwaway-prototype scaffolding, which issue #19 deletes out from under it.
public class LoginViewTests : IntegrationTestBase
{
    [Fact]
    public async Task Login_RendersTheHubLoginCard()
    {
        var html = await (await Client.GetAsync(AppRoutes.Login)).Content.ReadAsStringAsync();

        // The card and its gradient backdrop are the design decision
        // docs/ROADMAP.md records as "hub parity" for this screen.
        Assert.Contains("hub-login-card", html);
        Assert.Contains(Constants.BrandName, html);
        Assert.Contains(Constants.LoginHeading, html);

        // Label+Input pairs and a full-width brand submit, per the same entry.
        Assert.Contains(Constants.LoginUsernameLabel, html);
        Assert.Contains(Constants.LoginPasswordLabel, html);
        Assert.Contains(Constants.LoginSubmitText, html);
        Assert.Contains("hub-btn-brand", html);
    }

    [Fact]
    public async Task Login_CarriesNoPrototypeScaffolding()
    {
        var html = await (await Client.GetAsync(AppRoutes.Login)).Content.ReadAsStringAsync();

        // The variant switcher, its stylesheet and its script. Development is
        // the only environment that rendered them, which is also the
        // environment the test factory runs in - so this would catch a
        // leftover.
        Assert.DoesNotContain("prototype", html, StringComparison.OrdinalIgnoreCase);

        // The full-bleed gradient reaches the viewport edges, so the screen
        // takes a chromeless layout rather than MainLayout's app shell.
        Assert.DoesNotContain("ks-shell-header", html);
    }

    [Fact]
    public async Task Login_WithWrongPassword_RendersTheErrorInsideTheCard()
    {
        using var client = Factory.NoRedirectClient();

        var response = await DashboardLogin.LogInAsync(client, "admin", "wrong-password");
        var html = await response.Content.ReadAsStringAsync();

        // ai-hub raises this as a sonner toast, which needs a live client;
        // Static SSR has none, so the card renders it where the toast was.
        Assert.Contains("hub-login-error", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(Constants.LoginErrorText, html);
    }

    [Fact]
    public async Task Login_RendersOnlyClassesThatAppCssDefines()
    {
        using var client = Factory.NoRedirectClient();

        // Both states: the error block and its icon only exist on a failed
        // post, so a GET alone leaves those rules unchecked.
        var html = await (await client.GetAsync(AppRoutes.Login)).Content.ReadAsStringAsync()
            + await (await DashboardLogin.LogInAsync(client, "admin", "wrong-password")).Content.ReadAsStringAsync();

        // The hub login rules lived in prototype.css, which only
        // PrototypeSwitcher ever linked - and /login no longer renders it.
        // A class the page asks for but app.css does not define is therefore
        // an unstyled login screen, which no markup assertion can see.
        var rendered = Regex.Matches(html, @"class=""([^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(name => name.StartsWith("hub-"))
            .ToHashSet();

        var css = StaticAssets.ReadAppCssWithoutComments();

        // The trailing lookahead matters: without it `.hub-login-card` also
        // reads as a definition of `.hub-login`, and a missing rule passes.
        var defined = Regex.Matches(css, @"\.(hub-[a-z0-9-]+)(?![\w-])")
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

        Assert.NotEmpty(rendered);
        Assert.Empty(rendered.Except(defined));
    }
}
