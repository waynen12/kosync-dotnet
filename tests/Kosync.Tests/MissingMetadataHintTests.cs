using System.Net;
using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #31: the inline hint on a Book nothing has ever reported a
// title for. KOReader's "send document metadata" setting is off by default,
// so without the hint the feature looks broken rather than switched off
// (ADR 0007) - and it is surfaced inline on the Book, the same way a Split
// Book is, rather than in an inbox of its own.
public class MissingMetadataHintTests : IntegrationTestBase
{
    [Fact]
    public async Task ABookWithNoMetadataAtAll_CarriesTheHint()
    {
        await SyncAsync("hash1");

        var book = await GetBookAsync(await GetBookIdAsync("hash1"));

        Assert.True(book!.NeedsMetadataHint);
    }

    // The setting is already on for this reader, so telling them to enable it
    // would be wrong even though the Book shows no title: a push that carried
    // a filename and nothing else is still a push that carried metadata.
    [Fact]
    public async Task ABookWhoseDocumentObservedAnything_DoesNotCarryTheHint()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { filename = "dune.epub" });

        var book = await GetBookAsync(await GetBookIdAsync("hash1"));

        Assert.False(book!.NeedsMetadataHint);
    }

    // A reader who typed the title already has the answer the setting would
    // have fetched them, and does not need telling where titles come from.
    [Fact]
    public async Task ABookWithACuratedTitle_DoesNotCarryTheHint()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, "Dune", "Frank Herbert");

        var book = await GetBookAsync(bookId);

        Assert.False(book!.NeedsMetadataHint);
    }

    // A curated author leaves the Book still called `Book #N`, and the hint
    // says "no title yet" - so it is still the truth, and still worth saying.
    [Fact]
    public async Task ABookWithACuratedAuthorButNoTitle_StillCarriesTheHint()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, null, "Frank Herbert");

        var book = await GetBookAsync(bookId);

        Assert.Equal($"Book #{bookId}", book!.Title);
        Assert.True(book.NeedsMetadataHint);
    }

    // Clearing the curated title puts the Book back to knowing nothing about
    // itself, which is exactly when the hint is worth showing again.
    [Fact]
    public async Task ClearingACuratedTitle_BringsTheHintBack()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, "Dune", "Frank Herbert");
        await CurateAsync(bookId, null, null);

        var book = await GetBookAsync(bookId);

        Assert.True(book!.NeedsMetadataHint);
    }

    [Fact]
    public async Task BookDetail_RendersTheHintOnlyWhereItIsNeeded()
    {
        await SyncAsync("hash1");
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune" });

        // The whole sentence, including the part naming where in KOReader to
        // look - it is the only actionable half of the hint.
        Assert.Contains(Constants.MissingMetadataHint, await PortalTextAsync(AppRoutes.BookDetail(await GetBookIdAsync("hash1"))));
        Assert.DoesNotContain(Constants.MissingMetadataHint, await PortalTextAsync(AppRoutes.BookDetail(await GetBookIdAsync("hash2"))));
    }

    // The dashboard's table has a row per Book and no room for prose; the
    // hint belongs where the reader went looking for the missing title.
    [Theory]
    [InlineData(Constants.BooksTab)]
    [InlineData(Constants.DevicesTab)]
    public async Task Dashboard_DoesNotCarryTheHint(string tab)
    {
        await SyncAsync("hash1");

        Assert.DoesNotContain(
            Constants.MissingMetadataHint,
            await PortalTextAsync($"{AppRoutes.Dashboard}?tab={tab}"));
    }

    // Decoded, because Blazor HTML-encodes both the em dash and the
    // apostrophe in "KOReader's" - so the raw response never contains the
    // sentence verbatim even when the page renders it perfectly.
    private async Task<string> PortalTextAsync(string route)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(route)).Content.ReadAsStringAsync();

        return WebUtility.HtmlDecode(html);
    }

    private async Task SyncAsync(string documentHash, DocumentMetadataRequest? metadata = null)
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.25m,
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

    private async Task CurateAsync(int bookId, string? title, string? author)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        await new BookDashboardService(db).CurateBookAsync(bookId, title, author);
    }
}
