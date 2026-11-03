using Kosync.Database;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #38: which pushes in a Document's history are a Progress
// Regression. A regression is a push that *was blocked* when it arrived -
// lower than the Document's current progress at that moment - which is not
// the same statement as "is not the current progress now". Every push a
// later one superseded reads as not-current too, and those were promoted.
//
// So the flag is derived by replaying the promotion rule
// (Controllers/SyncController.cs) over the history, oldest first. The cases
// that distinguish the two statements need three or more pushes with two or
// more promotions, which is the normal case on any real Book.
public class ProgressRegressionTests : IntegrationTestBase
{
    [Fact]
    public async Task FivePushes_OneOfWhichWasBlocked_FlagOnlyThatOne()
    {
        //  0.47  no current      -> promoted
        //  0.52  0.52 >= 0.47    -> promoted
        //  0.12  0.12 <  0.52    -> BLOCKED, the only regression
        //  0.58  0.58 >= 0.52    -> promoted
        //  0.61  0.61 >= 0.58    -> promoted
        await PushAsync(0.47m, 0.52m, 0.12m, 0.58m, 0.61m);

        var history = await GetHistoryAsync("hash1");

        var regression = Assert.Single(history, h => h.BlockedRegression);
        Assert.Equal(0.12m, regression.Percentage);
    }

    // The same five pushes as seen by a reader: a healthy Book must not have
    // most of its history accused of regressing, which is what devalues the
    // flag exactly where it is meant to mean something.
    [Fact]
    public async Task BookDetail_WithOneBlockedPushAmongFive_RendersOneFlag()
    {
        await PushAsync(0.47m, 0.52m, 0.12m, 0.58m, 0.61m);

        var html = await GetBookDetailHtmlAsync("hash1");

        Assert.Equal(1, RenderedHtml.Occurrences(html, Constants.ProgressRegressionFlag));
    }

    // A blocked push leaves the ceiling where it was - it never became the
    // current progress, so it is not what the next push is measured
    // against. Without that, 0.30 would read as a forward step from 0.20
    // and come out promoted, which is not what the endpoint did with it.
    [Fact]
    public async Task TwoPushesBlockedInARow_AreBothFlagged()
    {
        //  0.50  no current      -> promoted
        //  0.20  0.20 <  0.50    -> BLOCKED, ceiling stays 0.50
        //  0.30  0.30 <  0.50    -> BLOCKED
        //  0.55  0.55 >= 0.50    -> promoted
        await PushAsync(0.50m, 0.20m, 0.30m, 0.55m);

        var history = await GetHistoryAsync("hash1");

        Assert.Equal(
            [0.30m, 0.20m],
            history.Where(h => h.BlockedRegression).Select(h => h.Percentage));
    }

    // A reset clears the ceiling exactly as ResetProgressAsync clears the
    // pointer: the next push is promoted whatever its percentage, so it is
    // not a regression however far behind it is - and neither is the reset
    // marker itself, which is not a push at all.
    [Fact]
    public async Task AfterAReset_NeitherTheResetNorTheLowerPushThatFollowsIsARegression()
    {
        await PushAsync(0.80m);
        await ResetProgressAsync("hash1");
        await PushAsync(0.05m);

        var history = await GetHistoryAsync("hash1");

        Assert.DoesNotContain(history, h => h.BlockedRegression);

        var html = await GetBookDetailHtmlAsync("hash1");
        Assert.Equal(0, RenderedHtml.Occurrences(html, Constants.ProgressRegressionFlag));
    }

    // Drives the real sync endpoint rather than writing SyncEvents directly:
    // the flag has to agree with what the promotion rule actually did, and
    // only the endpoint runs it.
    private async Task PushAsync(params decimal[] percentages)
    {
        foreach (var percentage in percentages)
        {
            var response = await PutAsync("/syncs/progress", new DocumentRequest
            {
                document = "hash1",
                progress = "p",
                percentage = percentage,
                device = "Kobo",
                device_id = "device-1"
            }, "admin", "admin");

            response.EnsureSuccessStatusCode();
        }
    }

    private async Task ResetProgressAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);

        var result = await new BookDashboardService(db).ResetProgressAsync(document.Id);

        Assert.Equal(ResetProgressResult.Success, result);
    }

    private async Task<IReadOnlyList<SyncEventSummary>> GetHistoryAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);

        var book = await new BookDashboardService(db).GetBookAsync(document.BookId);

        return Assert.Single(book!.Documents).History;
    }

    private async Task<string> GetBookDetailHtmlAsync(string documentHash)
    {
        var bookId = await GetBookIdAsync(documentHash);

        using var client = Factory.NoRedirectClient();
        await DashboardLogin.LogInAsync(client, "admin", "admin");

        var response = await client.GetAsync(AppRoutes.BookDetail(bookId));
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<int> GetBookIdAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);

        return document.BookId;
    }
}
