using System.Net;
using System.Text.RegularExpressions;
using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #32: now that Books carry real title and author signal,
// matching may *suggest* a merge - exact match, case-insensitive and
// trimmed, on both title and author, among one User's Books that have
// metadata at all (ADR 0007). The narrowness is the feature: the roadmap
// deferred automatic matching because false positives were too expensive,
// and a merge is transactional but not undoable.
public class MergeSuggestionTests : IntegrationTestBase
{
    [Fact]
    public async Task TwoBooksWithTheSameTitleAndAuthor_SuggestEachOther()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "  dune ", authors = "FRANK HERBERT" });

        var first = await GetBookIdAsync("hash1");
        var second = await GetBookIdAsync("hash2");

        Assert.Equal([second], (await GetBookAsync(first))!.MergeSuggestionBookIds);
        Assert.Equal([first], (await GetBookAsync(second))!.MergeSuggestionBookIds);
    }

    // Trimmed means trimmed, not normalised: a title whose inner spacing
    // differs is not an exact match, and collapsing it would be the first
    // step of the fuzzy matching ADR 0007 keeps as its own project - to be
    // decided on real data gathered from this exact rule.
    [Fact]
    public async Task TwoBooksDifferingInInnerWhitespace_AreNotSuggested()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank  Herbert" });

        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash1")))!.MergeSuggestionBookIds);
    }

    // Two editions of the same title by different people are two books. The
    // author is half the rule for exactly this reason.
    [Fact]
    public async Task TwoBooksWithTheSameTitleButDifferentAuthors_AreNotSuggested()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Hamlet", authors = "William Shakespeare" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Hamlet", authors = "John Updike" });

        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash1")))!.MergeSuggestionBookIds);
    }

    // A title-only match, which is the case the rule exists to refuse: this
    // is how books in a series and everything called "Collected Stories" get
    // paired up (ADR 0007).
    [Fact]
    public async Task TwoBooksWithAMatchingTitleAndNoAuthor_AreNotSuggested()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Collected Stories" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Collected Stories" });

        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash1")))!.MergeSuggestionBookIds);
    }

    // Both Books are called `Book #N`, which is a label for a Book that knows
    // nothing about itself rather than a title it has - so there is nothing
    // here to match on. The common case, too: KOReader's metadata setting is
    // off by default.
    [Fact]
    public async Task BooksWithNoMetadataAtAll_AreNeverSuggested()
    {
        await SyncAsync("hash1");
        await SyncAsync("hash2");

        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash1")))!.MergeSuggestionBookIds);
    }

    // A Book with half its metadata is still a title-only match in waiting,
    // whichever half is missing.
    [Fact]
    public async Task ABookWithATitleAndNoAuthor_IsNotSuggestedAgainstAFullyDescribedOne()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune" });

        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash1")))!.MergeSuggestionBookIds);
        Assert.Empty((await GetBookAsync(await GetBookIdAsync("hash2")))!.MergeSuggestionBookIds);
    }

    // Matching runs on what the Book is *called*, which is the resolution in
    // BookMetadataRule: a reader who typed the title has told us the two are
    // the same book, and a suggestion that ignored them would be matching on
    // a device's guess instead.
    [Fact]
    public async Task ACuratedTitleAndAuthor_Match_WhatAnotherBookObserved()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "dune_final_v2", authors = "unknown" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        var curated = await GetBookIdAsync("hash1");
        var observed = await GetBookIdAsync("hash2");

        Assert.Empty((await GetBookAsync(curated))!.MergeSuggestionBookIds);

        await CurateAsync(curated, "Dune", "Frank Herbert");

        Assert.Equal([observed], (await GetBookAsync(curated))!.MergeSuggestionBookIds);
        Assert.Equal([curated], (await GetBookAsync(observed))!.MergeSuggestionBookIds);
    }

    // Two readers owning the same book is the ordinary case, and a merge
    // moves Documents - so a cross-User suggestion would propose moving
    // somebody else's reading into this library (ADR 0004).
    [Fact]
    public async Task TwoUsersWhoOwnTheSameBook_AreNotSuggestedToEachOther()
    {
        var metadata = new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" };

        await CreateSyncingUserAsync("reader", TestPasswords.Chosen);

        await SyncAsync("hash1", metadata);
        await SyncAsync("hash2", metadata, username: "reader", password: TestPasswords.Chosen);

        var ownBook = await GetBookIdAsync("hash1");
        var otherUsersBook = await GetBookIdAsync("hash2", username: "reader");

        Assert.Empty((await GetBookAsync(ownBook))!.MergeSuggestionBookIds);
        Assert.Empty((await GetBookAsync(otherUsersBook, username: "reader"))!.MergeSuggestionBookIds);
    }

    // Inline on the Book it concerns, the way a Split Book is flagged there,
    // rather than in an inbox of its own (docs/ROADMAP.md, "Issue surfacing
    // UX"). The suggested Book is named by its id because both Books have
    // the same title.
    [Fact]
    public async Task BookDetail_SurfacesTheSuggestionInlineOnBothBooks()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        var first = await GetBookIdAsync("hash1");
        var second = await GetBookIdAsync("hash2");

        var html = await BookDetailHtmlAsync(first);

        Assert.Contains(Constants.MergeSuggestions.SectionTitle, html);
        Assert.Contains(string.Format(Constants.BookFallbackNameFormat, second), html);
        Assert.Contains(Constants.MergeSuggestions.MergeAction, html);

        Assert.Contains(
            string.Format(Constants.BookFallbackNameFormat, first),
            await BookDetailHtmlAsync(second));
    }

    [Fact]
    public async Task BookDetail_SaysNothingAboutSuggestionsWhereThereAreNone()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Hamlet", authors = "William Shakespeare" });

        var html = await BookDetailHtmlAsync(await GetBookIdAsync("hash1"));

        Assert.DoesNotContain(Constants.MergeSuggestions.SectionTitle, html);
        Assert.DoesNotContain(Constants.MergeSuggestions.MergeAction, html);
    }

    // A merge cannot be undone, and a suggested pair was put in front of the
    // reader by a rule rather than looked up by them - so the control that
    // takes one asks twice. The confirmation is a <details> disclosure, the
    // shape the Users page already uses for deletion: it is local UI state,
    // which must not round-trip to the server (ADR 0003).
    [Fact]
    public async Task BookDetail_AsksTwiceBeforeMergingASuggestedBook()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        var html = await BookDetailHtmlAsync(await GetBookIdAsync("hash1"));

        Assert.Contains(Constants.MergeConfirmWarning, WebUtility.HtmlDecode(html));
        Assert.Contains(Constants.MergeConfirmSubmitText, html);

        // The merge label is the <summary>, which is the whole of the first
        // press: a disclosure cannot submit, so no single click reaches the
        // merge. Structural because a GET is all this seam can see - the
        // second press runs over a circuit.
        AssertIsDisclosureSummary(Constants.MergeSuggestions.MergeAction, html);
    }

    // The typed form merges the id somebody entered, which a typo makes the
    // wrong Book - as irreversible as accepting a false suggestion, so it
    // asks twice through the same two strings.
    [Fact]
    public async Task BookDetail_AsksTwiceBeforeMergingATypedBookId()
    {
        await SyncAsync("hash1");

        var html = await BookDetailHtmlAsync(await GetBookIdAsync("hash1"));

        Assert.Contains(Constants.MergeConfirmWarning, WebUtility.HtmlDecode(html));
        Assert.Contains(Constants.MergeConfirmSubmitText, html);

        AssertIsDisclosureSummary(Constants.MergeFormSubmitText, html);

        // And the id box is not in a <form>, because Enter in a form's last
        // field submits it - which would have run the merge straight past
        // the confirmation the line above just established. A two-press
        // action gets no one-key shortcut. (The curate form beside it keeps
        // its Enter: typing a title is reversible by typing another.)
        Assert.DoesNotContain(@"<form class=""ks-merge-form""", html);
    }

    private static void AssertIsDisclosureSummary(string label, string html) =>
        Assert.Matches($@"<summary[^>]*>\s*{Regex.Escape(label)}", html);

    // The point of the whole ticket: a suggestion is a suggestion.
    // MergeBooksAsync is transactional but not undoable, so merging on a
    // match would make a false positive manual cleanup (ADR 0007).
    [Fact]
    public async Task ASuggestion_MergesNothingByItself()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        // Both the dashboard listing and the drill-in have now resolved
        // suggestions for these Books.
        await BookDetailHtmlAsync(await GetBookIdAsync("hash1"));

        var books = await GetBooksAsync();

        Assert.Equal(2, books.Count);
        Assert.All(books, book => Assert.Single(book.Documents));
    }

    // A suggestion nobody can find suggests nothing, and the drill-in is the
    // only place it is actionable - so the Books table flags the rows that
    // have one, in the Flags column beside the Split Book badge.
    [Fact]
    public async Task Dashboard_FlagsTheBooksThatHaveASuggestion()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        Assert.Contains(Constants.MergeSuggestions.Flag, await DashboardHtmlAsync(Constants.BooksTab));
    }

    [Fact]
    public async Task Dashboard_FlagsNothingWhereNoBookHasASuggestion()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Hamlet", authors = "William Shakespeare" });

        Assert.DoesNotContain(Constants.MergeSuggestions.Flag, await DashboardHtmlAsync(Constants.BooksTab));

        // A Device holds Documents, and a suggestion is about what two Books
        // are called - so the Devices table has nothing to say about one even
        // when the Device is the one that reported both titles.
        Assert.DoesNotContain(Constants.MergeSuggestions.Flag, await DashboardHtmlAsync(Constants.DevicesTab));
    }

    // The explicit action the suggestion is waiting on, taken: the two Books
    // become one, and the suggestion has nothing left to suggest.
    [Fact]
    public async Task AcceptingASuggestion_MergesTheTwoBooksAndRetiresTheSuggestion()
    {
        await SyncAsync("hash1", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });
        await SyncAsync("hash2", new DocumentMetadataRequest { title = "Dune", authors = "Frank Herbert" });

        var keep = await GetBookIdAsync("hash1");
        var suggested = Assert.Single((await GetBookAsync(keep))!.MergeSuggestionBookIds);

        Assert.Equal(MergeBooksResult.Success, await MergeAsync(keep, suggested));

        var merged = Assert.Single(await GetBooksAsync());

        Assert.Equal(keep, merged.Id);
        Assert.Equal(2, merged.Documents.Count);
        Assert.Empty(merged.MergeSuggestionBookIds);
    }

    private async Task<MergeBooksResult> MergeAsync(int keepBookId, int mergeBookId)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).MergeBooksAsync(keepBookId, mergeBookId);
    }

    private async Task<string> DashboardHtmlAsync(string tab)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync($"{AppRoutes.Dashboard}?tab={tab}")).Content.ReadAsStringAsync();
    }

    private async Task<string> BookDetailHtmlAsync(int bookId)
    {
        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        return await (await client.GetAsync(AppRoutes.BookDetail(bookId))).Content.ReadAsStringAsync();
    }

    private async Task<List<BookSummary>> GetBooksAsync()
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).GetBooksAsync();
    }

    private async Task<CurateBookResult> CurateAsync(int bookId, string? title, string? author)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).CurateBookAsync(bookId, title, author);
    }

    private async Task SyncAsync(
        string documentHash,
        DocumentMetadataRequest? metadata = null,
        string username = "admin",
        string password = "admin")
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1",
            metadata = metadata
        }, username, password);

        response.EnsureSuccessStatusCode();
    }

    private async Task<int> GetBookIdAsync(string documentHash, string username = "admin")
    {
        using var scope = Factory.CreateScopeForUser(username);
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return (await db.Documents.SingleAsync(d => d.DocumentHash == documentHash)).BookId;
    }

    private async Task<BookSummary?> GetBookAsync(int bookId, string username = "admin")
    {
        using var scope = Factory.CreateScopeForUser(username);
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).GetBookAsync(bookId);
    }
}
