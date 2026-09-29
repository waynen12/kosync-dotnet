using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Unit-level coverage for the aggregation logic behind the Books view
// (issue #6): a Book's displayed progress is the furthest-along current
// progress across its Documents, not whichever synced most recently.
public class BookDashboardServiceTests : IntegrationTestBase
{
    [Fact]
    public async Task GetBooksAsync_SingleDocumentBook_ProgressIsItsCurrentPercentage()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.42m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var books = await GetBooksAsync();

        var book = Assert.Single(books);
        Assert.Equal(0.42m, book.Percentage);
    }

    [Fact]
    public async Task GetBooksAsync_MergedBook_ProgressIsMaxAcrossDocumentsNotMostRecentlySynced()
    {
        var bookId = await CreateMergedBookAsync();

        // hash1 is further along but synced first; hash2 synced more
        // recently but is behind. The furthest-along Document must win.
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        Assert.Equal(0.80m, book!.Percentage);
    }

    [Fact]
    public async Task GetBookAsync_ListsEveryDocumentWithItsOwnDeviceAndProgress()
    {
        var bookId = await CreateMergedBookAsync();

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo Nova", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Pixel Phone", device_id = "device-2"
        }, "admin", "admin");

        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        Assert.Equal(2, book!.Documents.Count);

        var doc1 = Assert.Single(book.Documents, d => d.DocumentHash == "hash1");
        Assert.Equal("Kobo Nova", doc1.DeviceName);
        Assert.Equal(0.80m, doc1.Percentage);

        var doc2 = Assert.Single(book.Documents, d => d.DocumentHash == "hash2");
        Assert.Equal("Pixel Phone", doc2.DeviceName);
        Assert.Equal(0.20m, doc2.Percentage);
    }

    [Fact]
    public async Task GetBookAsync_DocumentHistory_IncludesEveryPushWithCurrentFlag()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.50m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        // A regression - lower than the existing current push, so it's
        // recorded but never promoted.
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.30m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");
        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        var doc = Assert.Single(book!.Documents);
        Assert.Equal(2, doc.History.Count);

        var current = Assert.Single(doc.History, h => h.IsCurrent);
        Assert.Equal(0.50m, current.Percentage);

        var regression = Assert.Single(doc.History, h => !h.IsCurrent);
        Assert.Equal(0.30m, regression.Percentage);
    }

    [Fact]
    public async Task GetBookAsync_UnknownId_ReturnsNull()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new BookDashboardService(db);

        var book = await service.GetBookAsync(999);

        Assert.Null(book);
    }

    [Fact]
    public async Task GetBooksAsync_MergedBookWithDisagreeingProgress_IsFlaggedAsSplitBook()
    {
        var bookId = await CreateMergedBookAsync();

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        Assert.True(book!.IsSplitBook);
    }

    [Fact]
    public async Task GetBooksAsync_MergedBookWithAgreeingProgress_IsNotFlaggedAsSplitBook()
    {
        var bookId = await CreateMergedBookAsync();

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.50m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p1", percentage = 0.50m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        Assert.False(book!.IsSplitBook);
    }

    [Fact]
    public async Task GetBooksAsync_SingleDocumentBook_IsNeverFlaggedAsSplitBook()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.42m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var books = await GetBooksAsync();

        var book = Assert.Single(books);
        Assert.False(book.IsSplitBook);
    }

    [Fact]
    public async Task MergeBooksAsync_UnionsDocumentsOntoTheSurvivingBook()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p0", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var keepBookId = await GetBookIdAsync("hash1");
        var mergeBookId = await GetBookIdAsync("hash2");

        var result = await MergeBooksAsync(keepBookId, mergeBookId);

        Assert.Equal(MergeBooksResult.Success, result);

        var book = await GetBookAsync(keepBookId);
        Assert.NotNull(book);
        Assert.Equal(2, book!.Documents.Count);
        Assert.Contains(book.Documents, d => d.DocumentHash == "hash1");
        Assert.Contains(book.Documents, d => d.DocumentHash == "hash2");
    }

    [Fact]
    public async Task MergeBooksAsync_RemovesTheMergedAwayBook()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p0", percentage = 0.20m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        var keepBookId = await GetBookIdAsync("hash1");
        var mergeBookId = await GetBookIdAsync("hash2");

        await MergeBooksAsync(keepBookId, mergeBookId);

        var mergedAwayBook = await GetBookAsync(mergeBookId);
        Assert.Null(mergedAwayBook);
    }

    [Fact]
    public async Task MergeBooksAsync_SameBookId_ReturnsSameBookWithoutChanges()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        var result = await MergeBooksAsync(bookId, bookId);

        Assert.Equal(MergeBooksResult.SameBook, result);

        var book = await GetBookAsync(bookId);
        Assert.NotNull(book);
        Assert.Single(book!.Documents);
    }

    [Fact]
    public async Task MergeBooksAsync_UnknownMergeBookId_ReturnsBookNotFound()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.10m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");

        var result = await MergeBooksAsync(bookId, 999);

        Assert.Equal(MergeBooksResult.BookNotFound, result);
    }

    [Fact]
    public async Task ResetProgressAsync_ClearsTheCurrentProgressPointer()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");
        var documentId = await GetDocumentIdAsync("hash1");

        var result = await ResetProgressAsync(documentId);
        Assert.Equal(ResetProgressResult.Success, result);

        var book = await GetBookAsync(bookId);
        Assert.NotNull(book);
        var doc = Assert.Single(book!.Documents);

        Assert.DoesNotContain(doc.History, h => h.IsCurrent);
        Assert.Equal(0m, doc.Percentage);
    }

    [Fact]
    public async Task ResetProgressAsync_LeavesSyncEventHistoryIntact()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");
        var documentId = await GetDocumentIdAsync("hash1");

        await ResetProgressAsync(documentId);

        var book = await GetBookAsync(bookId);
        var doc = Assert.Single(book!.Documents);

        Assert.Contains(doc.History, h => !h.IsReset && h.Percentage == 0.80m);
    }

    [Fact]
    public async Task ResetProgressAsync_AddsADistinctResetEntryToHistory()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");
        var documentId = await GetDocumentIdAsync("hash1");

        await ResetProgressAsync(documentId);

        var book = await GetBookAsync(bookId);
        var doc = Assert.Single(book!.Documents);

        Assert.Single(doc.History, h => h.IsReset);
    }

    [Fact]
    public async Task ResetProgressAsync_NextPushBecomesCurrentRegardlessOfPercentage()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p1", percentage = 0.80m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var bookId = await GetBookIdAsync("hash1");
        var documentId = await GetDocumentIdAsync("hash1");

        await ResetProgressAsync(documentId);

        // Lower than the pre-reset progress - would have been blocked as a
        // regression before the reset, but there's nothing to regress
        // against now.
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.05m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        var book = await GetBookAsync(bookId);
        var doc = Assert.Single(book!.Documents);

        Assert.Equal(0.05m, doc.Percentage);
        var current = Assert.Single(doc.History, h => h.IsCurrent);
        Assert.Equal(0.05m, current.Percentage);
    }

    [Fact]
    public async Task ResetProgressAsync_UnknownDocumentId_ReturnsDocumentNotFound()
    {
        var result = await ResetProgressAsync(999);

        Assert.Equal(ResetProgressResult.DocumentNotFound, result);
    }

    // Gets hash1 and hash2 each their own Document, then merges them into a
    // single Book via the shared IntegrationTestBase helper.
    private async Task<int> CreateMergedBookAsync()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1", progress = "p0", percentage = 0.01m, device = "Kobo", device_id = "device-1"
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash2", progress = "p0", percentage = 0.01m, device = "Phone", device_id = "device-2"
        }, "admin", "admin");

        return await MergeIntoOneBookAsync("hash1", "hash2");
    }

    private async Task<List<BookSummary>> GetBooksAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new BookDashboardService(db);
        return await service.GetBooksAsync();
    }

    private async Task<BookSummary?> GetBookAsync(int id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new BookDashboardService(db);
        return await service.GetBookAsync(id);
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
        return document.BookId;
    }

    private async Task<MergeBooksResult> MergeBooksAsync(int keepBookId, int mergeBookId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new BookDashboardService(db);
        return await service.MergeBooksAsync(keepBookId, mergeBookId);
    }

    private async Task<int> GetDocumentIdAsync(string documentHash)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
        return document.Id;
    }

    private async Task<ResetProgressResult> ResetProgressAsync(int documentId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var service = new BookDashboardService(db);
        return await service.ResetProgressAsync(documentId);
    }
}
