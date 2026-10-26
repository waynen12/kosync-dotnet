using System.Text.RegularExpressions;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #18: the dashboard becomes the hub's dense table behind a
// sidebar (the DashboardVariantB + HubSidebar fold-in).
//
// DevicesViewTests and BooksViewTests still own what the two sections show
// about a Device and a Book. What is asserted here is the shape the fold-in
// chose over the card grid, and the two things the issue calls "the point of
// this ticket": section selection is navigation rather than component state,
// and the dashboard no longer lists Documents.
public class DashboardViewTests : IntegrationTestBase
{
    [Fact]
    public async Task Dashboard_RendersTheDenseTableWithEveryColumn()
    {
        await PushProgressAsync("hash1", 0.42m, "Kobo Nova", "device-1");

        var html = await DashboardAsync(AppRoutes.Dashboard);

        // Matched with its delimiter: a bare "hub-table" is a substring of
        // "hub-table-wrap" and so could never fail on its own.
        Assert.Contains(@"<table class=""hub-table"">", html);

        Assert.Contains(Constants.Dashboard.ColumnName, html);
        Assert.Contains(Constants.Dashboard.ColumnDocuments, html);
        Assert.Contains(Constants.Dashboard.ColumnProgress, html);
        Assert.Contains(Constants.Dashboard.ColumnLastSynced, html);
        Assert.Contains(Constants.Dashboard.ColumnFlags, html);
    }

    // The deliberate loss in this ticket (docs/ROADMAP.md, "The dashboard
    // stops listing documents"): CardGrid rendered a hash row per Document,
    // and the table shows a count instead. Two Documents on one Device, so a
    // count of 2 cannot be confused with either a row count or a percentage.
    [Fact]
    public async Task Dashboard_ShowsADocumentCountRatherThanListingTheDocuments()
    {
        await PushProgressAsync("hash1", 0.10m, "Kobo Nova", "device-1");
        await PushProgressAsync("hash2", 0.75m, "Kobo Nova", "device-1");

        var html = await DashboardAsync(AppRoutes.Dashboard);

        Assert.Contains(@"<td class=""hub-table-num"">2</td>", html);

        // Element text rather than a bare substring, because the hashes do
        // still reach the page - as the filter index, asserted on its own
        // below. What must not come back is a Document rendered as content.
        Assert.DoesNotContain(">hash1<", html);
        Assert.DoesNotContain(">hash2<", html);
        Assert.DoesNotContain("ks-doc-row", html);
    }

    // The first of the two things issue #18 calls the point of the ticket.
    // The code this replaced sent a SignalR message so the server could
    // change a local string from "devices" to "books" - the exact
    // anti-pattern ADR 0003's amendment forbids. An anchor carrying the
    // destination is the proof: there is nothing left for a click to ask the
    // server, and the back button sees two URLs rather than one.
    [Fact]
    public async Task SectionNav_IsRealNavigationRatherThanComponentState()
    {
        var html = await DashboardAsync(AppRoutes.Dashboard);

        Assert.Contains($@"class=""hub-nav-item current"" href=""{AppRoutes.Dashboard}""", html);
        Assert.Contains($@"class=""hub-nav-item"" href=""{AppRoutes.DashboardBooksTab}""", html);

        // A <button> in here is how the regression would look: the only way
        // to act on a click in a Blazor component is to hand it to the
        // server. The sidebar is links or it is a round trip.
        Assert.DoesNotContain("<button", Sidebar(html));
    }

    [Fact]
    public async Task SectionNav_MarksTheSectionOnScreenAsCurrent()
    {
        var html = await DashboardAsync(AppRoutes.DashboardBooksTab);

        Assert.Contains($@"class=""hub-nav-item current"" href=""{AppRoutes.DashboardBooksTab}""", html);
        Assert.Contains($@"class=""hub-nav-item"" href=""{AppRoutes.Dashboard}""", html);
    }

    // Both sections are counted on both, which is why the page loads both:
    // the count is the sidebar's reason for being more than two tabs.
    [Fact]
    public async Task SectionNav_LabelsEachSectionWithItsSize()
    {
        await PushProgressAsync("hash1", 0.10m, "Kobo Nova", "device-1");
        await PushProgressAsync("hash2", 0.75m, "Pixel Phone", "device-2");

        var sidebar = Sidebar(await DashboardAsync(AppRoutes.DashboardBooksTab));

        // Two Devices and two unmerged Books, so both counts read 2 - and a
        // count rendered against the wrong section still fails the pair of
        // assertions in SectionNav_IsRealNavigationRatherThanComponentState.
        Assert.Equal(2, Regex.Matches(sidebar, @"<span class=""hub-nav-count"">2</span>").Count);
    }

    // The other half of a fold-in, and the half no markup assertion can see:
    // variant B's rules lived in prototype.css, which only PrototypeSwitcher
    // ever linked and issue #19 deletes. A class whose rule did not come
    // across is in the markup either way - it is simply unstyled.
    [Fact]
    public async Task Dashboard_RendersOnlyClassesThatAppCssDefines()
    {
        await SeedALibraryRenderingEveryRowStateAsync();

        var defined = HubClasses.DefinedInAppCss();

        // Both sections, because each has markup the other does not: the
        // Devices section is the one with the device icon, and only the Books
        // section's rows can be a split Book on their own.
        foreach (var route in new[] { AppRoutes.Dashboard, AppRoutes.DashboardBooksTab })
        {
            var rendered = HubClasses.RenderedIn(await DashboardAsync(route));

            Assert.NotEmpty(rendered);
            Assert.Empty(rendered.Except(defined));
        }
    }

    // The dashboard was CardGrid's last caller, so the component and the
    // shape behind it go with it (issue #18). Asserted by reflection because
    // the deletion is the point: nothing can reference a type that is gone,
    // which is a stronger claim than any grep of the markup.
    [Fact]
    public void CardGrid_IsGoneRatherThanMerelyUncalled()
    {
        var application = typeof(Constants).Assembly;

        Assert.Null(application.GetType("Kosync.Components.Shared.CardGrid"));
        Assert.Null(application.GetType("Kosync.Components.Shared.CardGridItem"));
        Assert.Null(application.GetType("Kosync.Components.Shared.CardGridDocRow"));
    }

    [Fact]
    public void AppCss_NoLongerCarriesTheCardGridChrome()
    {
        // Comments stripped first: section 9's prose says the card-grid rules
        // went with this issue, which is the explanation, not a survivor.
        var css = StaticAssets.ReadAppCssWithoutComments();

        // The lookahead is what makes these fail for the right reason:
        // without it `.ks-card-meta`, which BookDetail still renders, reads
        // as a definition of `.ks-card`.
        foreach (var retired in new[] { "ks-grid", "ks-card", "ks-card-head", "ks-card-title", "ks-card-sub", "ks-card-clickable", "ks-doc-row", "ks-pbar-track", "ks-pbar-fill", "ks-tabs", "ks-tab", "ks-header", "ks-title", "ks-subtitle" })
        {
            Assert.DoesNotMatch($@"\.{retired}(?![\w-])", css);
        }

        // ...and the two the detail pages still need are still here, so this
        // cannot pass by deleting section 9 wholesale.
        Assert.Matches(@"\.ks-card-meta(?![\w-])", css);
        Assert.Matches(@"\.ks-doc-card(?![\w-])", css);
    }

    // ADR 0003's amendment lists search-as-you-type among the things that are
    // UI state and must not round-trip, so the toolbar's filter is a plain
    // <input> over a per-row index and a few lines of JS - no @@oninput, and
    // nothing for the server to re-render.
    [Fact]
    public async Task Toolbar_FiltersRowsInTheBrowserRatherThanOnTheServer()
    {
        await PushProgressAsync("hash1", 0.42m, "Kobo Nova", "device-1");

        var html = await DashboardAsync(AppRoutes.Dashboard);

        Assert.Contains(Constants.Dashboard.SearchPlaceholder, html);
        Assert.Contains($@"data-ks-filter=""#{Constants.Dashboard.TableId}""", html);
        Assert.Contains($@"id=""{Constants.Dashboard.TableId}""", html);

        // The row carries what it matches against. A row filtered out is
        // hidden by the stylesheet, so no server-side list state exists for
        // the filter to disagree with.
        Assert.Contains("data-ks-text=", html);
        Assert.Contains(Constants.Dashboard.NoMatchesText, html);
    }

    // "Search by device, book or hash" is a promise about Documents the
    // dashboard no longer shows: you notice two hashes that should be one
    // Book, and this is where you paste one. So the hashes are in the index
    // and in no column - the one place the fold-in still owes them.
    [Fact]
    public async Task Toolbar_IndexesDocumentHashesWithoutGivingThemAColumn()
    {
        await PushProgressAsync("hash1", 0.42m, "Kobo Nova", "device-1");

        var html = await DashboardAsync(AppRoutes.Dashboard);

        Assert.Matches(@"data-ks-text=""[^""]*hash1[^""]*""", html);
        Assert.DoesNotContain(">hash1<", html);
    }

    [Fact]
    public void DashboardFilterJs_NeverReachesTheServer()
    {
        var js = StaticAssets.ReadJsWithoutComments(Constants.Dashboard.FilterScript);

        // The whole claim: a keystroke sets attributes on rows already in the
        // DOM. A request of any kind here would mean the filter had become
        // the round trip this file exists to avoid.
        Assert.DoesNotContain("fetch(", js);
        Assert.DoesNotContain("XMLHttpRequest", js);
        Assert.DoesNotContain("invokeMethod", js);
    }

    [Fact]
    public void DashboardFilterJs_RewiresAfterEnhancedNavigation()
    {
        var js = StaticAssets.ReadJsWithoutComments(Constants.Dashboard.FilterScript);

        // Switching section is now a navigation, and Blazor's enhanced
        // navigation patches the DOM rather than reloading the page - so a
        // filter wired on DOMContentLoaded alone is wired to the Devices
        // table and dead the moment you click Books. Same hook theme.js
        // needs, for the same reason.
        Assert.Matches(@"Blazor\.addEventListener\(\s*""enhancedload""", js);
        Assert.Contains("DOMContentLoaded", js);
    }

    // Three Devices and three Books, two of the Books merged into one - so
    // every row state the table can render is on screen at once: flagged and
    // unflagged, a multi-Document row and single-Document rows.
    private async Task SeedALibraryRenderingEveryRowStateAsync()
    {
        await PushProgressAsync("hash1", 0.10m, "Kobo Nova", "device-1");
        await PushProgressAsync("hash2", 0.75m, "Pixel Phone", "device-2");
        await PushProgressAsync("hash3", 0.00m, "Kobo Clara", "device-3");

        await MergeIntoOneBookAsync("hash1", "hash2");
    }

    // The sidebar only - so "no <button>" is a claim about the nav rather
    // than about the whole page, which has a search input and row actions.
    private static string Sidebar(string html)
    {
        var sidebar = Regex.Match(html, @"<nav class=""hub-sidebar"">.*?</nav>", RegexOptions.Singleline);

        Assert.True(sidebar.Success, "The dashboard rendered no <nav class=\"hub-sidebar\">.");

        return sidebar.Value;
    }

    private async Task PushProgressAsync(string document, decimal percentage, string device, string deviceId) =>
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = document,
            progress = "p1",
            percentage = percentage,
            device = device,
            device_id = deviceId
        }, "admin", "admin");

    private async Task<string> DashboardAsync(string route)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync(route)).Content.ReadAsStringAsync();
    }
}
