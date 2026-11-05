using Kosync.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kosync.Tests.TestSupport;

// Arranges the one instant issue #37 is about: another Device's push for the
// same brand-new Document landing between this request reading no such
// Document and inserting one. Both read nothing, both insert, and the unique
// index on (UserId, DocumentHash) refuses the second.
//
// A real race cannot be tested by firing two requests and hoping, so the
// rival push is injected exactly where the window is - SavingChanges runs
// after the request's own read and before the transaction its insert goes in,
// so the rival's rows are committed and visible by the time that insert
// executes. Raw SQL, on the connection the app is already using: going
// through the entities would put the rival's rows in the very change tracker
// whose behaviour is under test.
public class RivalDocumentPush : SaveChangesInterceptor
{
    // Everything the rival's push leaves behind: its own Device, its own 1:1
    // Book, the Document it won the race with, and the SyncEvent it pushed.
    private const string RivalPushSql = """
        INSERT INTO "Books" ("UserId") VALUES ({0});
        INSERT INTO "Devices" ("DeviceId", "DeviceName", "UserId") VALUES ({1}, {2}, {0});
        INSERT INTO "Documents" ("DocumentHash", "UserId", "BookId") VALUES ({3}, {0}, (SELECT MAX("Id") FROM "Books"));
        INSERT INTO "SyncEvents" ("DocumentId", "DeviceId", "Progress", "Percentage", "Timestamp", "IsCurrent")
            VALUES ((SELECT MAX("Id") FROM "Documents"), (SELECT MAX("Id") FROM "Devices"), {4}, {5}, {6}, 1);
        """;

    public const string DeviceId = "rival-device";
    public const string DeviceName = "Rival";
    public const string Progress = "rival-progress";

    private readonly string _documentHash;
    private readonly decimal _percentage;
    private bool _pushed;

    public RivalDocumentPush(string documentHash, decimal percentage)
    {
        _documentHash = documentHash;
        _percentage = percentage;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Push(eventData);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Push(eventData);

        return ValueTask.FromResult(result);
    }

    private void Push(DbContextEventData eventData)
    {
        var context = eventData.Context;
        if (_pushed || context is null) { return; }

        // Only the save that inserts the raced Document: this interceptor is
        // on the app's context for the whole test, and the rival pushed once.
        var inserting = context.ChangeTracker.Entries<Document>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .FirstOrDefault(document => document.DocumentHash == _documentHash);

        if (inserting is null) { return; }

        _pushed = true;

        context.Database.ExecuteSqlRaw(
            RivalPushSql,
            inserting.User.Id,
            DeviceId,
            DeviceName,
            _documentHash,
            Progress,
            _percentage,
            DateTime.UtcNow);
    }
}
