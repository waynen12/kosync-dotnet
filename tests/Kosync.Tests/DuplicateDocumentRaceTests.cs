using System.Net;
using Kosync.Database;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Two Devices pushing the same brand-new Document in the same instant: both
// find nothing, both insert, and the unique index on (UserId, DocumentHash)
// refuses the second (ADR 0008, issue #37). The push that lost must still
// land - as a SyncEvent on the Document that won - because "sync two devices
// after a trip" is exactly when this happens, and losing a push would be
// losing where somebody had read to.
//
// RivalDocumentPush is what makes the instant reproducible.
public class DuplicateDocumentRaceTests : IntegrationTestBase
{
    private const string RacedHash = "raced-hash";

    // The rival is further along, so its push stays the current progress and
    // the one that lost the race is recorded behind it. Which of the two is
    // current is the ordinary regression rule's answer and is asserted
    // separately below.
    private const decimal RivalPercentage = 0.8m;

    private const decimal OwnPercentage = 0.5m;

    protected override KosyncWebApplicationFactory CreateFactory() =>
        new() { DbInterceptor = new RivalDocumentPush(RacedHash, RivalPercentage) };

    private Task<HttpResponseMessage> PushAsync(
        decimal percentage, DocumentMetadataRequest? metadata = null) =>
        PutAsync("/syncs/progress", new DocumentRequest
        {
            document = RacedHash,
            progress = "own-progress",
            percentage = percentage,
            device = "Kobo",
            device_id = "device-1",
            metadata = metadata
        }, "admin", "admin");

    [Fact]
    public async Task LosingTheRace_StillSucceeds()
    {
        var response = await PushAsync(OwnPercentage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await BodyAsync(response);
        Assert.Equal(RacedHash, body.GetProperty("document").GetString());
    }

    [Fact]
    public async Task LosingTheRace_LeavesOneDocumentWithBothPushesOnIt()
    {
        await PushAsync(OwnPercentage);

        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var document = await db.Documents
            .Include(d => d.SyncEvents)
                .ThenInclude(s => s.Device)
            .SingleAsync(d => d.DocumentHash == RacedHash);

        Assert.Equal(
            [RivalDocumentPush.DeviceId, "device-1"],
            document.SyncEvents.OrderBy(s => s.Id).Select(s => s.Device.DeviceId));
    }

    // The Book the losing push had already built for its Document goes with
    // it: the Document it was for was never stored, so the Book would be one
    // no Document belongs to - a phantom in the reader's library (ADR 0001).
    [Fact]
    public async Task LosingTheRace_LeavesNoBookWithoutADocument()
    {
        await PushAsync(OwnPercentage);

        using var scope = Factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var books = await db.Books.Include(b => b.Documents).ToListAsync();

        Assert.All(books, book => Assert.NotEmpty(book.Documents));
    }

    // Nothing special about losing where progress is concerned: the push is
    // judged against the Document that won, by the same regression rule as
    // any other push. Behind the rival, so recorded and not promoted.
    [Fact]
    public async Task LosingTheRace_BehindTheRival_DoesNotBecomeTheCurrentProgress()
    {
        await PushAsync(OwnPercentage);

        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var current = Assert.Single(await db.SyncEvents.Where(s => s.IsCurrent).ToListAsync());

        Assert.Equal(RivalPercentage, current.Percentage);
    }

    // And ahead of the rival it is promoted, with the rival's pointer
    // cleared - so exactly one SyncEvent per Document still holds the
    // current-progress pointer after a collision.
    [Fact]
    public async Task LosingTheRace_AheadOfTheRival_BecomesTheCurrentProgress()
    {
        await PushAsync(RivalPercentage + 0.1m);

        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var current = Assert.Single(await db.SyncEvents.Where(s => s.IsCurrent).ToListAsync());

        Assert.Equal(RivalPercentage + 0.1m, current.Percentage);
    }

    // Observed Metadata lands on the Document that won rather than on a row
    // that no longer exists - the split-metadata case the issue's follow-up
    // comment is about, now that a push can carry metadata (ADR 0007).
    [Fact]
    public async Task LosingTheRace_RecordsObservedMetadataOnTheSurvivingDocument()
    {
        await PushAsync(OwnPercentage, new DocumentMetadataRequest
        {
            filename = "dune.epub",
            title = "Dune",
            authors = "Frank Herbert"
        });

        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var document = await db.Documents.SingleAsync(d => d.DocumentHash == RacedHash);

        Assert.Equal("dune.epub", document.ObservedFilename);
        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
    }
}
