using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #31: what a Document is *called* on the two detail pages. A
// Document is a file, so the filename a Device observed is the most honest
// name for it, and a truncated hash stands in where none was observed
// (ADR 0007). The full hash stays on the page either way - it is what you
// need to diagnose a Split Book.
public class DocumentLabelTests : IntegrationTestBase
{
    // A real md5, because the truncation only has something to do on one:
    // "hash1" is already shorter than the label.
    private const string LongHash = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task ADocumentWithAnObservedFilename_IsLabelledWithIt()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "Dune", authors = "Frank Herbert"
        });

        var book = await GetBookAsync(await GetBookIdAsync(LongHash));

        Assert.Equal("dune.epub", Assert.Single(book!.Documents).Label);
    }

    // The permanent case, not a transitional one: KOReader's "send document
    // metadata" setting is opt-in and only exists from 2026.07 (ADR 0007).
    [Fact]
    public async Task ADocumentWithNoObservedFilename_FallsBackToATruncatedHash()
    {
        await SyncAsync(LongHash);

        var book = await GetBookAsync(await GetBookIdAsync(LongHash));

        // Literal rather than derived from the constant: the point is that
        // the label is a recognisable prefix of the hash and shorter than it.
        Assert.Equal("01234567...", Assert.Single(book!.Documents).Label);
    }

    // A push that carried a title but no filename still observed nothing to
    // name the *file* with, and a Book's title is not a Document's label -
    // two Documents on one Book can report different titles (ADR 0007).
    [Fact]
    public async Task ADocumentObservedWithoutAFilename_StillFallsBackToTheHash()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest
        {
            title = "Dune", authors = "Frank Herbert"
        });

        var book = await GetBookAsync(await GetBookIdAsync(LongHash));

        Assert.Equal("01234567...", Assert.Single(book!.Documents).Label);
    }

    // The label is a label. The hash is what you take to KOReader to find out
    // which file a Split Book's two Documents actually are, so it survives
    // whether or not there is a filename to show above it.
    [Fact]
    public async Task TheFullHashIsKeptBesideTheLabel()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest { filename = "dune.epub" });

        var book = await GetBookAsync(await GetBookIdAsync(LongHash));

        Assert.Equal(LongHash, Assert.Single(book!.Documents).DocumentHash);
    }

    // Both detail pages label Documents, and they must agree: the same file
    // reached from a Device and from its Book is the same file.
    [Fact]
    public async Task ADevicesDocuments_AreLabelledTheSameWay()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest { filename = "dune.epub" });
        await SyncAsync("fedcba9876543210fedcba9876543210");

        var device = await GetDeviceAsync();

        Assert.Equal("dune.epub", device!.Documents.Single(d => d.DocumentHash == LongHash).Label);
        Assert.Equal("fedcba98...", device.Documents.Single(d => d.DocumentHash != LongHash).Label);
    }

    [Fact]
    public async Task BookDetail_ShowsTheFilenameAndKeepsTheFullHash()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest { filename = "dune.epub" });

        var html = await DetailHtmlAsync(AppRoutes.BookDetail(await GetBookIdAsync(LongHash)));

        Assert.Contains("dune.epub", html);
        Assert.Contains(LongHash, html);
    }

    [Fact]
    public async Task BookDetail_WithNoFilename_ShowsTheTruncatedHashAndTheFullOne()
    {
        await SyncAsync(LongHash);

        var html = await DetailHtmlAsync(AppRoutes.BookDetail(await GetBookIdAsync(LongHash)));

        Assert.Contains("01234567...", html);
        Assert.Contains(LongHash, html);
    }

    [Fact]
    public async Task DeviceDetail_ShowsTheFilenameAndKeepsTheFullHash()
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest { filename = "dune.epub" });

        var html = await DetailHtmlAsync(AppRoutes.DeviceDetail((await GetDeviceAsync())!.Id));

        Assert.Contains("dune.epub", html);
        Assert.Contains(LongHash, html);
    }

    [Fact]
    public async Task DeviceDetail_WithNoFilename_ShowsTheTruncatedHashAndTheFullOne()
    {
        await SyncAsync(LongHash);

        var html = await DetailHtmlAsync(AppRoutes.DeviceDetail((await GetDeviceAsync())!.Id));

        Assert.Contains("01234567...", html);
        Assert.Contains(LongHash, html);
    }

    // The dashboard shows a Document count and labels no individual Document,
    // so a filename has nothing to replace there (docs/ROADMAP.md, "The
    // dashboard stops listing documents"). Both tabs: a Device row's search
    // text is built from its Documents too, and DeviceDocumentSummary is one
    // of the two records that just gained a Label.
    //
    // The full hash stays in the row's data-ks-text - asserted, not merely
    // allowed, because that is the one thing the dashboard still owes
    // Documents: pasting the hash KOReader showed you has to find the row.
    // Swapping it for the label would pass a "no filename anywhere" check on
    // its own.
    [Theory]
    [InlineData(Constants.BooksTab)]
    [InlineData(Constants.DevicesTab)]
    public async Task Dashboard_DoesNotLabelDocumentsButStillFindsThemByHash(string tab)
    {
        await SyncAsync(LongHash, new DocumentMetadataRequest { filename = "dune.epub" });

        var html = await DetailHtmlAsync($"{AppRoutes.Dashboard}?tab={tab}");

        Assert.Matches($@"data-ks-text=""[^""]*{LongHash}[^""]*""", html);
        Assert.DoesNotContain("dune.epub", html);
        Assert.DoesNotContain("01234567...", html);
    }

    private async Task<string> DetailHtmlAsync(string route)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync(route)).Content.ReadAsStringAsync();
    }

    private async Task SyncAsync(string documentHash, DocumentMetadataRequest? metadata = null, decimal percentage = 0.25m)
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = percentage,
            device = "Kobo",
            device_id = "device-1",
            metadata = metadata
        }, "admin", "admin");

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return (await db.Documents.SingleAsync(d => d.DocumentHash == documentHash)).BookId;
    }

    private async Task<BookSummary?> GetBookAsync(int bookId)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).GetBookAsync(bookId);
    }

    private async Task<DeviceSummary?> GetDeviceAsync()
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var device = await db.Devices.SingleAsync(d => d.DeviceId == "device-1");

        return await new DeviceDashboardService(db).GetDeviceAsync(device.Id);
    }
}
