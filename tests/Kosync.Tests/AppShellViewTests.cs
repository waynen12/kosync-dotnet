using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #17: MainLayout's app shell takes the hub design (the
// HubNav.razor fold-in).
//
// DeviceDetail is the probe page throughout. Every authenticated page that
// takes the default layout gets the same shell, and this one renders it with
// no fixture data at all - a missing Device still renders the layout around
// its "not found", so the shell is under test rather than the page.
public class AppShellViewTests : IntegrationTestBase
{
    [Fact]
    public async Task AppShell_RendersTheHubNavOnAnAuthenticatedPage()
    {
        var html = await AuthenticatedPageAsync();

        // The gradient band and the translucent strip over it - the two
        // elements that carry the look, per docs/ROADMAP.md's fold-in table.
        // Matched with their delimiters: a bare "hub-nav" is a substring of
        // "hub-nav-brand" and so could never fail on its own.
        Assert.Contains(@"<header class=""hub-topband"">", html);
        Assert.Contains(@"<nav class=""hub-nav"">", html);
        Assert.Contains(@"class=""hub-nav-brand""", html);
        Assert.Contains(Constants.BrandName, html);

        // ai-hub's nav shows the bare Username, where the pre-hub shell wrote
        // a "Logged in as ..." sentence.
        Assert.Contains("hub-nav-user", html);
        Assert.Contains(">admin<", html);

        Assert.DoesNotContain("ks-shell-", html);
    }

    [Fact]
    public async Task AppShell_RendersOnlyClassesThatAppCssDefines()
    {
        var rendered = HubClasses.RenderedIn(await AuthenticatedPageAsync());

        // The nav's rules lived in prototype.css, which issue #19 deleted
        // along with the only page that linked it. They had to be in app.css
        // for the shell to survive that.
        Assert.NotEmpty(rendered);
        Assert.Empty(rendered.Except(HubClasses.DefinedInAppCss()));
    }

    [Fact]
    public async Task ThemeToggle_StaysPureJsRatherThanRoundTripping()
    {
        var html = await AuthenticatedPageAsync();

        // ADR 0003's amendment: local UI state must not round-trip. Flipping
        // the theme sets one attribute on <html>, so a Blazor `@onclick`
        // would bounce a click off the server to do it - and this toggle is
        // the existing proof of that rule. Converting it is the regression
        // this guards: the literal attribute below is what disappears.
        Assert.Contains(@"onclick=""window.kosyncTheme.toggle()""", html);

        // theme.js rewrites the textContent of [data-theme-toggle], which
        // would delete the toggle's inline SVG. The hub toggle renders both
        // icons and lets CSS pick one instead, so it must not opt in.
        Assert.DoesNotContain("data-theme-toggle", html);
        Assert.Contains("hub-theme-moon", html);
        Assert.Contains("hub-theme-sun", html);
    }

    [Fact]
    public void AppCss_NoLongerCarriesThePreHubShellChrome()
    {
        // The hub nav replaced `.ks-shell-*` outright rather than being
        // styled on top of it, so unlike the detail pages' `.ks-*` rules
        // (which stay until issue #33) these had no remaining reader. Dead
        // rules in a stylesheet read as a second, competing shell.
        //
        // Comments are stripped first: section 6's prose names what it
        // replaced, and that is the explanation, not a surviving rule.
        Assert.DoesNotContain("ks-shell", StaticAssets.ReadAppCssWithoutComments());
    }

    [Fact]
    public void ThemeJs_NeverRewritesAToggleLabel()
    {
        // The last [data-theme-toggle] in the app went with the pre-hub
        // shell. Leaving the label-rewriting behind would make the attribute
        // a trap for the next toggle someone adds - the icons it swaps are
        // inline SVG children, and textContent assignment removes them.
        //
        // Comments stripped first: the comment that replaced the code says
        // what it replaced, which is the explanation, not the behaviour.
        var js = StaticAssets.ReadThemeJsWithoutComments();

        Assert.DoesNotContain("data-theme-toggle", js);
        Assert.DoesNotContain("textContent", js);
    }

    [Fact]
    public void ThemeJs_ReappliesTheStoredThemeAfterEnhancedNavigation()
    {
        // Stripped, for the same reason: theme.js's own comment explains the
        // hook at length, so a scan of the raw file passes on the prose
        // alone and would survive the registration being deleted.
        var js = StaticAssets.ReadThemeJsWithoutComments();

        // The sticky-per-User contract (issue #5): enhanced navigation
        // patches the DOM in place and resets <html>'s attributes to the
        // server-rendered markup, which never carries data-theme. Without
        // this hook the theme silently resets on every in-app navigation -
        // and nothing in the rendered HTML can show that.
        Assert.Contains("enhancedload", js);
        Assert.Matches(@"Blazor\.addEventListener\(\s*""enhancedload""", js);

        // Blazor's event bus is the only place "enhancedload" fires and
        // blazor.web.js has not loaded when this script runs, so the
        // registration has to be deferred.
        Assert.Contains("DOMContentLoaded", js);
    }

    private async Task<string> AuthenticatedPageAsync()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync(AppRoutes.DeviceDetail(1))).Content.ReadAsStringAsync();
    }
}
