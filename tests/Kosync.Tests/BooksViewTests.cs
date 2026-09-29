using System.Net;
using Kosync.Database;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #6: the Books card-grid landing page and its per-Book
// drill-in detail page.
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
    public async Task Dashboard_BooksTab_ShowsBookCardWithProgressAndDocumentHash()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.42m,
            device = "Kobo Nova",
            device_id = "device-1"
        }, "admin", "admin");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.Contains("hash1", html);
        Assert.Contains("42%", html);
        Assert.DoesNotContain("No books have synced yet.", html);
    }

    [Fact]
    public async Task BookCard_LinksToItsRoutedDetailPage()
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

        // Only the regressed push is flagged - the promoted one is not.
        var regressionCount = html.Split(Constants.ProgressRegressionFlag).Length - 1;
        Assert.Equal(1, regressionCount);
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
        Assert.Contains("Book not found.", html);
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
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
        return document.BookId;
    }
}
