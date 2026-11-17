using System.Net;
using System.Text;
using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// KOReader ≥ 2026.07 can be told to send a document's filename, title and
// authors alongside a progress push. What arrives is an Observed Metadata -
// one device's report about one file - so it is stored on the Document and
// never on the Book (ADR 0007, issue #29).
public class ObservedMetadataTests : IntegrationTestBase
{
    [Fact]
    public async Task APushWithMetadata_StoresItAgainstTheDocument()
    {
        var response = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub",
                title = "Dune",
                authors = "Frank Herbert"
            }
        }, "admin", "admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await DocumentAsync("hash1");

        Assert.Equal("dune.epub", document.ObservedFilename);
        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
    }

    // The case every existing KOReader in the world is in: no `metadata` key
    // at all. It means "this client sent none", so what another Device
    // already observed about the same file has to survive it.
    [Fact]
    public async Task APushWithoutMetadata_LeavesStoredMetadataIntact()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub",
                title = "Dune",
                authors = "Frank Herbert"
            }
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kindle",
            device_id = "device-2"
        }, "admin", "admin");

        var document = await DocumentAsync("hash1");

        Assert.Equal("dune.epub", document.ObservedFilename);
        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
    }

    // A later observation of the same file replaces the earlier one. The
    // Document is one file, so there is nothing to reconcile: whoever read it
    // most recently saw the version that is actually there now.
    [Fact]
    public async Task APushWithDifferentMetadata_ReplacesWhatWasObservedBefore()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub",
                title = "Dune",
                authors = "Frank Herbert"
            }
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kobo",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune-revised.epub",
                title = "Dune: 40th Anniversary Edition",
                authors = "Frank Herbert; Brian Herbert"
            }
        }, "admin", "admin");

        var document = await DocumentAsync("hash1");

        Assert.Equal("dune-revised.epub", document.ObservedFilename);
        Assert.Equal("Dune: 40th Anniversary Edition", document.ObservedTitle);
        Assert.Equal("Frank Herbert; Brian Herbert", document.ObservedAuthors);
    }

    // A file with no author in it gives KOReader nothing to send, and an
    // empty field is a device reporting that it found nothing - not a
    // correction of what another device did find.
    [Fact]
    public async Task APushWhoseMetadataFieldsAreBlank_LeavesThoseFieldsIntact()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub",
                title = "Dune",
                authors = "Frank Herbert"
            }
        }, "admin", "admin");

        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p2",
            percentage = 0.50m,
            device = "Kindle",
            device_id = "device-2",
            metadata = new DocumentMetadataRequest
            {
                filename = "dune.epub",
                title = "   ",
                authors = null
            }
        }, "admin", "admin");

        var document = await DocumentAsync("hash1");

        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
    }

    // Binding asserted against the bytes KOReader actually puts on the wire
    // (koreader/koreader PR #15306) rather than against our own model
    // round-tripping itself: `authors` is one string and not an array, and
    // the nested object sits alongside the existing flat fields.
    [Fact]
    public async Task AKoreaderPushCarryingMetadata_BindsFromTheWireFormat()
    {
        var response = await PutRawAsync("/syncs/progress", """
            {
              "document": "hash1",
              "progress": "/body/DocFragment[3]/body/div/p[12]",
              "percentage": 0.37,
              "device": "Kobo Clara",
              "device_id": "DEVICE1",
              "metadata": {
                "title": "Dune",
                "authors": "Frank Herbert",
                "filename": "dune.epub"
              }
            }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await DocumentAsync("hash1");

        Assert.Equal("Dune", document.ObservedTitle);
        Assert.Equal("Frank Herbert", document.ObservedAuthors);
        Assert.Equal("dune.epub", document.ObservedFilename);
    }

    // The roadmap's guiding constraint: the sync protocol surface stays
    // exactly what a stock KOReader expects. Sending metadata must not buy
    // the client a different response - not even an extra key, since this is
    // the one place a client could start depending on one.
    [Fact]
    public async Task APushWithMetadata_AnswersWithTheSameTwoFieldsAsOneWithout()
    {
        var withMetadata = await PutRawAsync("/syncs/progress", """
            {
              "document": "hash1",
              "progress": "p1",
              "percentage": 0.25,
              "device": "Kobo",
              "device_id": "device-1",
              "metadata": { "title": "Dune", "authors": "Frank Herbert", "filename": "dune.epub" }
            }
            """);

        var withoutMetadata = await PutRawAsync("/syncs/progress", """
            {
              "document": "hash2",
              "progress": "p1",
              "percentage": 0.25,
              "device": "Kobo",
              "device_id": "device-1"
            }
            """);

        var withBody = await BodyAsync(withMetadata);
        var withoutBody = await BodyAsync(withoutMetadata);

        Assert.Equal(HttpStatusCode.OK, withMetadata.StatusCode);
        Assert.Equal(
            withoutBody.EnumerateObject().Select(p => p.Name),
            withBody.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "document", "timestamp" }, withBody.EnumerateObject().Select(p => p.Name));
        Assert.Equal("hash1", withBody.GetProperty("document").GetString());
    }

    // ADR 0007 resolves a Book's displayed title to the *most recent*
    // observation across its Documents, so when an observation arrived is
    // part of the observation. It cannot be recovered afterwards and it is
    // not the Document's last sync time either - a Device keeps pushing
    // progress for a file whose metadata it last reported months ago.
    [Fact]
    public async Task AnObservation_RecordsWhenItArrived_AndAMetadatalessPushDoesNotMoveIt()
    {
        await PutRawAsync("/syncs/progress", """
            {
              "document": "hash1",
              "progress": "p1",
              "percentage": 0.25,
              "device": "Kobo",
              "device_id": "device-1",
              "metadata": { "title": "Dune", "authors": "Frank Herbert", "filename": "dune.epub" }
            }
            """);

        var observedAt = (await DocumentAsync("hash1")).MetadataObservedAt;

        Assert.NotNull(observedAt);
        Assert.Equal(DateTimeKind.Utc, observedAt.Value.Kind);

        await PutRawAsync("/syncs/progress", """
            {
              "document": "hash1",
              "progress": "p2",
              "percentage": 0.50,
              "device": "Kindle",
              "device_id": "device-2"
            }
            """);

        Assert.Equal(observedAt, (await DocumentAsync("hash1")).MetadataObservedAt);
    }

    [Fact]
    public async Task ADocumentNobodyEverSentMetadataFor_HasNoObservation()
    {
        await PutRawAsync("/syncs/progress", """
            {
              "document": "hash1",
              "progress": "p1",
              "percentage": 0.25,
              "device": "Kobo",
              "device_id": "device-1"
            }
            """);

        var document = await DocumentAsync("hash1");

        Assert.Null(document.MetadataObservedAt);
        Assert.Null(document.ObservedTitle);
        Assert.Null(document.ObservedAuthors);
        Assert.Null(document.ObservedFilename);
    }

    private async Task<HttpResponseMessage> PutRawAsync(string url, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.SetCredentials("admin", "admin");

        return await Client.SendAsync(request);
    }

    private async Task<Document> DocumentAsync(string documentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await db.Documents.SingleAsync(d => d.DocumentHash == documentHash);
    }
}
