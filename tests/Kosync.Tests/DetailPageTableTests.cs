using Kosync.Database;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #39: the two drill-ins rebuilt as the third prototyping
// session's variant A, "Ledger" (docs/ROADMAP.md, "The detail pages -
// decided"). A Book's Documents and a Device's Documents become the
// dashboard's `hub-table`, and a row expands into the full hash plus
// whatever that page has to show under it.
//
// The subject here is the shape both pages now share - the table, the
// expansion, what rolls up onto a collapsed row and what does not.
// BooksViewTests and DevicesViewTests keep the per-page behaviour that
// predates this design.
public class DetailPageTableTests : IntegrationTestBase
{
    // The deciding argument of the whole session: the drill-in should not be
    // a different design language from the page that links to it (issue #18
    // chose the table for the dashboard, one level up). So these are the
    // dashboard's primitives, not a second set.
    [Fact]
    public async Task BookDetail_ListsItsDocumentsAsTheDashboardsTable()
    {
        await PushAsync("hash1", 0.42m);

        var html = await BookDetailHtmlAsync("hash1");

        Assert.Contains(@"class=""hub-table-wrap""", html);
        Assert.Contains(@"class=""hub-table""", html);

        Assert.Contains($"<th>{Constants.DetailPages.ColumnDocument}</th>", html);
        Assert.Contains($"<th>{Constants.DetailPages.ColumnDevice}</th>", html);
        Assert.Contains($"<th>{Constants.DetailPages.ColumnProgress}</th>", html);
        Assert.Contains(Constants.DetailPages.ColumnLastPush, html);
        Assert.Contains(Constants.DetailPages.ColumnFlags, html);
    }

    // The card shape these rows replaced. Asserted as absent rather than
    // left to the stylesheet scan, because markup carrying a class whose
    // rules have been deleted renders as an unstyled div - which a test
    // reading only app.css cannot see.
    [Fact]
    public async Task BookDetail_NoLongerRendersTheCardShapeTheTableReplaced()
    {
        await PushAsync("hash1", 0.42m);

        var html = await BookDetailHtmlAsync("hash1");

        Assert.DoesNotContain("ks-doc-card", html);
        Assert.DoesNotContain("ks-detail", html);
        Assert.DoesNotContain("ks-history", html);
    }

    // What stopped the table being a table: ~32 monospace characters per row
    // competing with the filename. The hash is reference material you fetch
    // while diagnosing, so it moves inside the drawer - but it is never
    // replaced by the label, which is ADR 0007's rule and the reason it is
    // on the page at all.
    [Fact]
    public async Task BookDetail_KeepsTheFullHashInTheDrawerAndTheLabelOnTheRow()
    {
        await PushAsync(LongHash, 0.42m);

        var html = await BookDetailHtmlAsync(LongHash);

        // The label the collapsed row carries - DocumentLabelRule's
        // truncated hash, nothing having observed a filename here.
        Assert.Contains("01234567...", html);

        // The full hash is present, and behind the disclosure: the drawer row
        // is the `hidden` one, so finding the hash inside it is finding it
        // off-screen until somebody opens the row.
        Assert.Contains(LongHash, html);
        Assert.Contains(Constants.DetailPages.HashLabel, html);
        Assert.Matches(RowDrawerPattern(LongHash), html);
    }

    // Progressive disclosure must not hide the thing the page exists to
    // surface: a collapsed table still has to show which Document has
    // something wrong in its history, or diagnosing a Book means opening
    // every row to find out there was nothing to find.
    [Fact]
    public async Task BookDetail_RollsTheRegressionFlagUpOntoTheCollapsedRow()
    {
        // Five pushes of which exactly one was blocked - the density issue
        // #38 was found at, and the only one that can tell a rolled-up
        // "something was blocked here" from "this push is not the current
        // one".
        foreach (var percentage in new[] { 0.47m, 0.52m, 0.12m, 0.58m, 0.61m })
        {
            await PushAsync("hash1", percentage);
        }

        var html = await BookDetailHtmlAsync("hash1");

        Assert.Contains(Constants.ProgressRegressionFlag, RenderedHtml.CollapsedRows(html));
    }

    // And deliberately no Progress Reset roll-up. A reset is a restart
    // somebody chose, flagged neutrally precisely because nothing went wrong
    // (issue #9) - so it has nothing to summon anybody into a row for. It
    // still appears in the history once the row is open.
    [Fact]
    public async Task BookDetail_DoesNotRollAProgressResetUpOntoTheCollapsedRow()
    {
        await PushAsync("hash1", 0.80m);
        await ResetProgressAsync("hash1");

        var html = await BookDetailHtmlAsync("hash1");

        Assert.Contains(Constants.ProgressResetFlag, html);
        Assert.DoesNotContain(Constants.ProgressResetFlag, RenderedHtml.CollapsedRows(html));
    }

    // The reset was one press from an irreversible action, in a column you
    // scan past. It is now inside the drawer and behind the same two presses
    // the merge already asked for (issue #32) - a <summary> cannot act, so
    // the first press only opens the confirmation.
    [Fact]
    public async Task BookDetail_AsksTwiceBeforeResettingADocumentsProgress()
    {
        await PushAsync("hash1", 0.42m);

        var html = await BookDetailHtmlAsync("hash1");

        Assert.Contains(Constants.ResetProgressConfirmWarning, System.Net.WebUtility.HtmlDecode(html));
        AssertIsDisclosureSummary(Constants.ResetProgressButton, html);
    }

    // And it is inside the drawer rather than on the row, which is the half
    // the assertion above cannot see: a confirmation that asks twice on a
    // row you scan past is still a control in the way of the evidence.
    [Fact]
    public async Task BookDetail_KeepsTheResetActionInsideTheDrawer()
    {
        await PushAsync("hash1", 0.42m);

        var html = await BookDetailHtmlAsync("hash1");

        Assert.DoesNotContain(Constants.ResetProgressButton, RenderedHtml.CollapsedRows(html));
    }

    // The page lands on evidence. Both forms used to be permanently open
    // above the Documents, which put an irreversible merge control between
    // the reader and the thing they came to look at.
    [Theory]
    [InlineData(Constants.CuratedMetadata.SectionTitle)]
    [InlineData(Constants.MergeFormSubmitText)]
    public async Task BookDetail_LandsWithItsFormsClosed(string summaryLabel)
    {
        await PushAsync("hash1", 0.42m);

        var html = await BookDetailHtmlAsync("hash1");

        // `<details>` and not `<details open>`: the attribute is what would
        // make this a form on screen again, and it is the only difference
        // between the two states in the markup.
        Assert.Matches(
            $@"<details>\s*<summary[^>]*>\s*{System.Text.RegularExpressions.Regex.Escape(summaryLabel)}",
            html);
    }

    // Today the suggestion list renders unconditionally above the Documents.
    // It moves inside the merge disclosure, with the count on the summary -
    // because closed, the summary is the only thing that can say the rule
    // found anything. The *flag* is unaffected, which the test below covers.
    [Fact]
    public async Task BookDetail_PutsTheSuggestionCountOnTheMergeSummary()
    {
        await SyncWithTitleAsync("hash1", "Dune", "Frank Herbert");
        await SyncWithTitleAsync("hash2", "Dune", "Frank Herbert");

        var html = await BookDetailHtmlAsync("hash1");

        var expected = string.Format(Constants.MergeSuggestions.MergeDisclosureFormat, 1);

        AssertIsDisclosureSummary(expected, html);

        // The one decision in tension with "inline flags on the thing they
        // belong to", so it is asserted rather than left implied: only the
        // action moved behind the disclosure. The Book still wears the flag.
        Assert.Contains(Constants.MergeSuggestions.Flag, RenderedHtml.CollapsedRows(html));
    }

    // The same table one page over, which is the point: a layout that only
    // worked on one of the two pages would reintroduce the split this
    // session existed to close (docs/ROADMAP.md, on why variant C lost).
    [Fact]
    public async Task DeviceDetail_ListsItsDocumentsAsTheSameTable()
    {
        await PushAsync(LongHash, 0.42m);

        var html = await DeviceDetailHtmlAsync();

        Assert.Contains(@"class=""hub-table-wrap""", html);
        Assert.Contains($"<th>{Constants.DetailPages.ColumnDocument}</th>", html);
        Assert.Contains(Constants.DetailPages.ColumnLastPush, html);

        // Not the plain word "Progress": a Device's row shows that Device's
        // own most recent push, and another Device may have pushed further
        // since (DeviceDashboardService). "Progress" would claim the
        // Document's current pointer. Decoded, because the apostrophe in it
        // reaches the page as an entity.
        Assert.Contains(Constants.DetailPages.ColumnDeviceProgress, System.Net.WebUtility.HtmlDecode(html));

        // The hash is behind the same disclosure, for the same reason.
        Assert.Contains(Constants.DetailPages.HashLabel, html);
        Assert.Matches(RowDrawerPattern(LongHash), html);
    }

    // A reset belongs to the Document on its Book, where the other Devices'
    // pushes are visible too - so this page carries no actions at all.
    [Fact]
    public async Task DeviceDetail_CarriesNoActions()
    {
        await PushAsync("hash1", 0.42m);

        var html = await DeviceDetailHtmlAsync();

        Assert.DoesNotContain(Constants.ResetProgressButton, html);
        Assert.DoesNotContain(Constants.MergeFormSectionTitle, html);
    }

    // Issue #13's wording survives this redesign, which
    // DevicesViewTests.DeviceDetail_WithoutDocuments_SaysNoDocumentsRather-
    // ThanNoBooks already guards - no second copy of it here.

    // ADR 0003's amendment: local UI state must not round-trip. Expanding a
    // row shows markup already in the page, so it is the browser's business -
    // and an @onclick here would have sent a circuit message and shipped
    // back a DOM diff to reveal it. Same claim DashboardFilterJs makes.
    [Fact]
    public void RowExpandJs_NeverReachesTheServer()
    {
        var js = StaticAssets.ReadJsWithoutComments(Constants.DetailPages.RowExpandScript);

        Assert.DoesNotContain("fetch(", js);
        Assert.DoesNotContain("XMLHttpRequest", js);
        Assert.DoesNotContain("invokeMethod", js);
    }

    // Delegated from the document, which is what makes it survive these
    // pages being InteractiveServer: Blazor owns the rows and re-renders
    // them on every merge, reset and curate, so a listener held on a button
    // is a listener on whatever that button has become. Nothing to re-wire
    // is why this needs no `enhancedload` hook, unlike the dashboard filter.
    [Fact]
    public void RowExpandJs_DelegatesRatherThanWiringEachRow()
    {
        var js = StaticAssets.ReadJsWithoutComments(Constants.DetailPages.RowExpandScript);

        Assert.Matches(@"document\.addEventListener\(\s*""click""", js);
        Assert.DoesNotContain("querySelectorAll", js);
    }

    // The contract the two pages render against. Asserted because it is
    // split across three files - the script, and a <tr> pair in each page -
    // and an attribute renamed in one of them fails silently: the row simply
    // stops opening, with no error anywhere.
    [Theory]
    [InlineData("data-row-expand")]
    [InlineData("data-row-drawer")]
    public async Task BothDetailPages_RenderTheAttributesTheExpanderLooksFor(string attribute)
    {
        await PushAsync("hash1", 0.42m);

        Assert.Contains(attribute, StaticAssets.ReadJsWithoutComments(Constants.DetailPages.RowExpandScript));
        Assert.Contains(attribute, await BookDetailHtmlAsync("hash1"));
        Assert.Contains(attribute, await DeviceDetailHtmlAsync());
    }

    private static void AssertIsDisclosureSummary(string label, string html) =>
        Assert.Matches($@"<summary[^>]*>\s*{System.Text.RegularExpressions.Regex.Escape(label)}", html);

    private async Task<string> DeviceDetailHtmlAsync()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        int deviceId;
        using (var scope = Factory.CreateScopeForUser("admin"))
        {
            var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
            deviceId = (await db.Devices.FirstAsync()).Id;
        }

        return await (await client.GetAsync(AppRoutes.DeviceDetail(deviceId))).Content.ReadAsStringAsync();
    }

    // A push carrying observed metadata, which is what makes two Books
    // suggest each other (BookMergeSuggestionRule needs a title and an
    // author on both).
    private async Task SyncWithTitleAsync(string documentHash, string title, string author) =>
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest { title = title, authors = author }
        }, "admin", "admin");

    private const string LongHash = "0123456789abcdef0123456789abcdef";

    // The drawer <tr> and everything up to the hash inside it, on one line
    // or many. Structural because the claim is about *where* the hash is:
    // a Contains would pass just as well with it back on the row.
    private static string RowDrawerPattern(string hash) =>
        $@"<tr[^>]*\bhidden\b[^>]*>(?s).*?{System.Text.RegularExpressions.Regex.Escape(hash)}";

    private async Task PushAsync(string documentHash, decimal percentage, string device = "Kobo Nova", string deviceId = "device-1") =>
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = percentage,
            device = device,
            device_id = deviceId
        }, "admin", "admin");

    private async Task<string> BookDetailHtmlAsync(string documentHash)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var bookId = await GetBookIdAsync(documentHash);

        return await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
        return document.BookId;
    }
}
