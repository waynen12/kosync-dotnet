using System.Net;
using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #6: the Books section of the dashboard and its per-Book
// drill-in detail page.
//
// The landing page was a card grid until issue #18 replaced it with the hub's
// dense table, so the assertions here are about what a Book shows in that
// table. DashboardViewTests owns the table's shape and the sidebar.
public class BooksViewTests : IntegrationTestBase
{
    [Fact]
    public async Task Dashboard_WithoutBooks_ShowsEmptyState()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.Contains("No books have synced yet.", html);
    }

    [Fact]
    public async Task Dashboard_BooksTab_ShowsBookRowWithProgressAndDocumentCount()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        // A Book has no title until Phase 3 observes or curates one
        // (ADR 0007), so the table names it by its id.
        Assert.Contains($"Book #{bookId}", html);
        Assert.Contains(@"<td class=""hub-table-num"">1</td>", html);
        Assert.Contains("42%", html);
        // Its one Document's hash went with the card - see DashboardViewTests,
        // which owns that change.
        Assert.DoesNotContain(">hash1<", html);
        Assert.DoesNotContain("No books have synced yet.", html);
    }

    [Fact]
    public async Task BookRow_LinksToItsRoutedDetailPage()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"{AppRoutes.BookDetail(bookId)}\"", html);
    }

    [Fact]
    public async Task BookDetail_ShowsFurthestAlongProgressAcrossMergedDocuments()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.10m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        // hash2 pulls ahead after the merge - the Book's overall progress
        // must reflect the furthest-along Document, not just hash1.
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p2", percentage = 0.90m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains("90% progress", html);
    }

    [Fact]
    public async Task BookDetail_ListsEveryDocumentWithItsOriginatingDeviceAndProgress()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.75m, device = "Pixel Phone", device_id = "device-2"
        }, "admin", "admin");

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains("hash1", html);
        Assert.Contains("Kobo Nova", html);
        Assert.Contains("10%", html);
        Assert.Contains("hash2", html);
        Assert.Contains("Pixel Phone", html);
        Assert.Contains("75%", html);
    }

    [Fact]
    public async Task BookDetail_ShowsFullSyncEventHistoryWithRegressionFlaggedInline()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.50m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        // Regresses below the existing current push - still recorded, but
        // must be flagged inline rather than silently promoted.
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.30m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains("50%", html);
        Assert.Contains("30%", html);

        // Only the regressed push is flagged - the promoted one is not. Two
        // pushes cannot tell that apart from "every push but the current
        // one"; ProgressRegressionTests is where that distinction is made.
        //
        // In the drawer since issue #39, which is where the history now
        // lives. The row above it carries its own rolled-up copy.
        Assert.Equal(1, RenderedHtml.Occurrences(RenderedHtml.RowDrawers(html), Constants.ProgressRegressionFlag));
    }

    [Fact]
    public async Task Dashboard_BooksTab_FlagsASplitBookInline()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        await MergeIntoOneBookAsync("hash1", "hash2");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.Contains(Constants.SplitBookFlag, html);
    }

    [Fact]
    public async Task Dashboard_BooksTab_DoesNotFlagASingleDocumentBookAsSplit()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.DoesNotContain(Constants.SplitBookFlag, html);
    }

    [Fact]
    public async Task BookDetail_FlagsASplitBookInline()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains(Constants.SplitBookFlag, html);
    }

    [Fact]
    public async Task BookDetail_ShowsAMergeForm()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.42m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains("Merge another Book into this one", html);

        // The id box it is still built around. Since issue #39 the form is
        // inside a closed disclosure rather than open on the page - which
        // DetailPageTableTests owns - but the Book id typed into it is the
        // only way to merge two Books nothing suggested, so it has to be
        // reachable from here either way.
        Assert.Contains($@"placeholder=""{Constants.MergeFormPlaceholder}""", html);
    }

    [Fact]
    public async Task BookDetail_ShowsAResetProgressButtonPerDocument()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.42m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains(Constants.ResetProgressButton, html);
    }

    [Fact]
    public async Task BookDetail_AfterReset_ShowsTheResetFlaggedInHistory()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        await ResetProgressAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains(Constants.ProgressResetFlag, html);
    }

    // Both resets are rendered, not just the newest (issue #40): the
    // timeline is the reader's account of what happened to this Document,
    // and a reset that has gone missing from it leaves the push that
    // followed looking like an unexplained step backwards.
    [Fact]
    public async Task BookDetail_AfterTwoResets_ShowsBothOfThemInHistory()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        await ResetProgressAsync("hash1");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.05m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        await ResetProgressAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Equal(2, RenderedHtml.Occurrences(html, Constants.ProgressResetFlag));
    }

    [Fact]
    public async Task BookDetail_BackLinkReturnsToTheBooksTabNotDevices()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.42m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();

        Assert.Contains($"href=\"{AppRoutes.DashboardBooksTab}\"", html);
    }

    [Fact]
    public async Task BookDetail_UnknownBook_ShowsNotFoundMessage()
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var response = await client.GetAsync(AppRoutes.BookDetail(999));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Constants.BookNotFoundText, html);
    }

    [Fact]
    public async Task BookDetail_WithoutSession_RedirectsToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.BookDetail(1));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
        return document.BookId;
    }

}
