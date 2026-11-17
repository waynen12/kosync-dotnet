using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// A User can set a Book's title and author by hand. That is Curated Metadata
// (GLOSSARY.md): it lives on the Book, it outranks anything a Device observed
// about one of its files, and a push can never overwrite it (ADR 0007,
// issue #30).
public class CuratedMetadataTests : IntegrationTestBase
{
    [Fact]
    public async Task ACuratedTitle_BecomesTheBooksDisplayedTitle()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        var result = await CurateAsync(bookId, "Dune", "Frank Herbert");

        Assert.Equal(CurateBookResult.Success, result);

        var book = await GetBookAsync(bookId);

        Assert.NotNull(book);
        Assert.Equal("Dune", book!.Title);
        Assert.Equal("Frank Herbert", book.Author);
    }

    [Fact]
    public async Task WithNothingCurated_TheObservedTitleShows()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "Dune", authors = "Frank Herbert"
        });

        var book = await GetBookAsync(await GetBookIdAsync("hash1"));

        Assert.NotNull(book);
        Assert.Equal("Dune", book!.Title);
        Assert.Equal("Frank Herbert", book.Author);

        // Nothing was curated, so an edit form has nothing to prefill: the
        // title on screen is an observation, not this User's answer.
        Assert.Null(book.CuratedTitle);
        Assert.Null(book.CuratedAuthor);
    }

    [Fact]
    public async Task ACuratedTitle_BeatsWhatADeviceObserved()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "dune_final_v2", authors = "unknown"
        });

        var bookId = await GetBookIdAsync("hash1");
        await CurateAsync(bookId, "Dune", "Frank Herbert");

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.Title);
        Assert.Equal("Frank Herbert", book.Author);
    }

    // The rule ADR 0007 exists for: a push reports what one device happened
    // to extract from one file, while a human who typed a title is asserting
    // they know the correct answer. The push is still recorded - against the
    // Document, where observations live - it just changes nothing on screen.
    [Fact]
    public async Task APushAfterCurating_DoesNotOverrideTheCuratedTitle()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, "Dune", "Frank Herbert");

        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune_final_v2.epub", title = "dune_final_v2", authors = "unknown"
        });

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.Title);
        Assert.Equal("Frank Herbert", book.Author);

        var document = await DocumentAsync("hash1");

        Assert.Equal("dune_final_v2", document.ObservedTitle);
    }

    // Two Documents on one Book can honestly report different titles. With
    // nothing curated, the most recent observation wins - deliberately not
    // the furthest-along Document that decides the Book's progress, since no
    // sense exists in which a title from a further-read file is more correct
    // (ADR 0007). hash1 is further along here and still loses.
    [Fact]
    public async Task WithTwoObservations_TheMostRecentWinsNotTheFurthestAlong()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "dune_scan", authors = "unknown"
        }, percentage: 0.90m);

        await SyncAsync("hash2", new DocumentMetadataRequest
        {
            filename = "dune-clean.epub", title = "Dune", authors = "Frank Herbert"
        }, percentage: 0.10m);

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.Title);
        Assert.Equal("Frank Herbert", book.Author);
    }

    [Fact]
    public async Task WithNeitherCuratedNorObserved_TheBookIsNamedByItsId()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        var book = await GetBookAsync(bookId);

        Assert.Equal($"Book #{bookId}", book!.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public async Task ClearingACuratedTitle_FallsBackToTheObservation()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "dune_scan", authors = "unknown"
        });

        var bookId = await GetBookIdAsync("hash1");
        await CurateAsync(bookId, "Dune", "Frank Herbert");

        var result = await CurateAsync(bookId, "", "   ");

        Assert.Equal(CurateBookResult.Success, result);

        var book = await GetBookAsync(bookId);

        Assert.Null(book!.CuratedTitle);
        Assert.Null(book.CuratedAuthor);
        Assert.Equal("dune_scan", book.Title);
        Assert.Equal("unknown", book.Author);
    }

    [Fact]
    public async Task ClearingWithNoObservationEither_ReturnsToTheIdFallback()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, "Dune", "Frank Herbert");
        await CurateAsync(bookId, null, null);

        var book = await GetBookAsync(bookId);

        Assert.Equal($"Book #{bookId}", book!.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public async Task ACuratedTitle_IsStoredTrimmed()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");

        await CurateAsync(bookId, "  Dune  ", "  Frank Herbert  ");

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.CuratedTitle);
        Assert.Equal("Frank Herbert", book.CuratedAuthor);
    }

    // Merging is how a Split Book gets reunited, and the observations that
    // disagreed are exactly the evidence of what was split. They stay on
    // their own Documents, so the merge has nothing to reconcile and nothing
    // to destroy (ADR 0007).
    [Fact]
    public async Task MergingTwoBooks_DestroysNeitherOnesObservations()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "Dune", authors = "Frank Herbert"
        });

        await SyncAsync("hash2", new DocumentMetadataRequest
        {
            filename = "dune-anniversary.epub", title = "Dune: 40th Anniversary Edition", authors = "Frank Herbert"
        });

        await MergeIntoOneBookAsync("hash1", "hash2");

        Assert.Equal("Dune", (await DocumentAsync("hash1")).ObservedTitle);
        Assert.Equal("dune.epub", (await DocumentAsync("hash1")).ObservedFilename);
        Assert.Equal("Dune: 40th Anniversary Edition", (await DocumentAsync("hash2")).ObservedTitle);
        Assert.Equal("dune-anniversary.epub", (await DocumentAsync("hash2")).ObservedFilename);
    }

    // A merge removes one of the two Books, and a curated title is the one
    // thing on it that nothing else can reproduce - no Document observed it
    // and no push will bring it back. Where the surviving Book has no answer
    // of its own, it inherits the one a human already gave (ADR 0007).
    [Fact]
    public async Task MergingABookThatWasCurated_KeepsTheTitleOnTheSurvivingBook()
    {
        await SyncAsync("hash1");
        await SyncAsync("hash2");

        var mergeBookId = await GetBookIdAsync("hash2");
        await CurateAsync(mergeBookId, "Dune", "Frank Herbert");

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.CuratedTitle);
        Assert.Equal("Frank Herbert", book.CuratedAuthor);
    }

    // Two curated titles are two humans' answers about what is now one Book,
    // and the surviving Book is the one its owner chose to keep. Nothing here
    // can tell which answer is better, so the merge does not guess.
    [Fact]
    public async Task MergingTwoCuratedBooks_KeepsTheSurvivingBooksOwnTitle()
    {
        await SyncAsync("hash1");
        await SyncAsync("hash2");

        var keepBookId = await GetBookIdAsync("hash1");
        await CurateAsync(keepBookId, "Dune", "Frank Herbert");
        await CurateAsync(await GetBookIdAsync("hash2"), "Dune Messiah", "F. Herbert");

        var bookId = await MergeIntoOneBookAsync("hash1", "hash2");

        var book = await GetBookAsync(bookId);

        Assert.Equal("Dune", book!.CuratedTitle);
        Assert.Equal("Frank Herbert", book.CuratedAuthor);
    }

    // Curating is scoped to the Book's owning User the same way reading is
    // (ADR 0004): another User's Book id is filtered out before the action
    // sees it, so "not found" is both the answer and the whole
    // authorisation check - and the Book it names keeps its own title.
    [Fact]
    public async Task CuratingAnotherUsersBook_IsNotFoundAndChangesNothing()
    {
        await CreateSyncingUserAsync("alice", TestPasswords.Chosen);

        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "alice-hash",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "alice-device",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub", title = "Dune", authors = "Frank Herbert"
            }
        }, "alice", TestPasswords.Chosen);

        response.EnsureSuccessStatusCode();

        int aliceBookId;
        using (var aliceScope = Factory.CreateScopeForUser("alice"))
        {
            var aliceDb = aliceScope.ServiceProvider.GetRequiredService<KosyncDbContext>();
            aliceBookId = (await aliceDb.Documents.SingleAsync(d => d.DocumentHash == "alice-hash")).BookId;
        }

        var result = await CurateAsync(aliceBookId, "Vandalised", "Nobody");

        Assert.Equal(CurateBookResult.BookNotFound, result);

        using var scope = Factory.CreateScopeForUser("alice");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var book = await new BookDashboardService(db).GetBookAsync(aliceBookId);

        Assert.Equal("Dune", book!.Title);
        Assert.Null(book.CuratedTitle);
    }

    // The rest of what a Book shows - progress, flags, its Documents - is
    // BooksViewTests' subject. These are about the title on the page and the
    // form that sets it.
    [Fact]
    public async Task BookDetail_ShowsTheResolvedTitleAndKeepsTheIdVisible()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");
        await CurateAsync(bookId, "Dune", "Frank Herbert");

        var html = await BookDetailHtmlAsync(bookId);

        Assert.Contains("Dune", html);
        Assert.Contains("Frank Herbert", html);

        // The id is what you merge another Book into, so a titled Book must
        // not stop telling you which Book it is.
        Assert.Contains($"Book #{bookId}", html);
    }

    [Fact]
    public async Task BookDetail_PrefillsTheFormWithWhatWasCuratedOnly()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest
        {
            filename = "dune.epub", title = "dune_scan", authors = "unknown"
        });

        var bookId = await GetBookIdAsync("hash1");

        var observedOnly = await BookDetailHtmlAsync(bookId);

        // The displayed title is an observation, so the form is empty: a
        // prefilled one would curate it the moment anybody pressed save,
        // and a push would then stop being able to correct it.
        Assert.Contains(Constants.CuratedMetadata.TitleLabel, observedOnly);
        Assert.DoesNotContain(@"value=""dune_scan""", observedOnly);
        Assert.DoesNotContain(Constants.CuratedMetadata.ClearButton, observedOnly);

        await CurateAsync(bookId, "Dune", "Frank Herbert");

        var curated = await BookDetailHtmlAsync(bookId);

        Assert.Contains(@"value=""Dune""", curated);
        Assert.Contains(@"value=""Frank Herbert""", curated);
        Assert.Contains(Constants.CuratedMetadata.ClearButton, curated);
    }

    [Fact]
    public async Task Dashboard_NamesABookByItsTitleRatherThanItsId()
    {
        await SyncAsync("hash1");
        var bookId = await GetBookIdAsync("hash1");
        await CurateAsync(bookId, "Dune", "Frank Herbert");

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var html = await (await client.GetAsync(AppRoutes.DashboardBooksTab)).Content.ReadAsStringAsync();

        Assert.Contains("Dune", html);
        Assert.DoesNotContain($"Book #{bookId}", html);
    }

    private async Task<string> BookDetailHtmlAsync(int bookId)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();
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

    private async Task<Document> DocumentAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
    }

    private async Task<CurateBookResult> CurateAsync(int bookId, string? title, string? author)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).CurateBookAsync(bookId, title, author);
    }

    private async Task<BookSummary?> GetBookAsync(int bookId)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).GetBookAsync(bookId);
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return (await db.Documents.SingleAsync(d => d.DocumentHash == documentHash)).BookId;
    }
}
