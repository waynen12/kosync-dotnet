namespace Kosync.Database.Legacy;

public static class LiteDbMigrator
{
    public static void MigrateIfNeeded(KosyncDbContext db, string liteDbPath, PasswordHasher passwordHasher, ILogger logger)
    {
        // The import reads and writes every User's rows at once, so it is one
        // of the three places ADR 0004 allows outside the tenant filters. Its
        // caller is where that bypass is declared by name; this is the check
        // that it actually was, because an import running fail-closed would
        // see an empty database and quietly do the wrong thing rather than
        // fail.
        if (!db.TenantFiltersBypassed)
        {
            throw new InvalidOperationException(Constants.Tenancy.LiteDbImportRequiresBypassError);
        }

        if (!File.Exists(liteDbPath))
        {
            return;
        }

        if (db.Users.Any())
        {
            return;
        }

        using var liteDb = new LiteDatabase($"Filename={liteDbPath};Connection=direct;ReadOnly=true");
        var legacyUsers = liteDb.GetCollection<LegacyUser>("users").FindAll().ToList();

        foreach (var legacyUser in legacyUsers)
        {
            // The legacy store holds a bare md5(password) - the inner step of
            // the current scheme - so the import wraps it rather than
            // carrying it across as-is (ADR 0005).
            var user = new User
            {
                Username = legacyUser.Username,
                PasswordHash = passwordHasher.HashAuthKey(legacyUser.PasswordHash),
                IsActive = legacyUser.IsActive,
                IsAdministrator = legacyUser.IsAdministrator,
                // The other route where the password is not Provisional: an
                // imported User has been syncing with this one for as long as
                // the legacy server ran, and importing their data is not
                // somebody choosing a password for them (ADR 0006).
                PasswordState = PasswordState.UserChosen,
            };

            var devicesByDeviceId = new Dictionary<string, Device>();

            // The legacy server read a User's documents by hash with no
            // constraint under them, exactly as the push path used to, so its
            // file can hold two records for one hash (ADR 0008, issue #37).
            // They are one file twice: one Document, with the 1:1 Book it
            // would have had anyway, and a push each. Without this the import
            // hits the unique index and takes startup down with it.
            var documentsByHash = new Dictionary<string, Document>();
            var currentEventByHash = new Dictionary<string, SyncEvent>();

            foreach (var legacyDocument in legacyUser.Documents)
            {
                if (!documentsByHash.TryGetValue(legacyDocument.DocumentHash, out var document))
                {
                    var book = new Book { User = user };
                    document = new Document
                    {
                        DocumentHash = legacyDocument.DocumentHash,
                        User = user,
                        Book = book,
                    };

                    documentsByHash[legacyDocument.DocumentHash] = document;

                    user.Documents.Add(document);
                    db.Books.Add(book);
                    db.Documents.Add(document);
                }

                if (!devicesByDeviceId.TryGetValue(legacyDocument.DeviceId, out var device))
                {
                    device = new Device
                    {
                        DeviceId = legacyDocument.DeviceId,
                        DeviceName = legacyDocument.Device,
                        User = user,
                    };
                    devicesByDeviceId[legacyDocument.DeviceId] = device;
                    user.Devices.Add(device);
                    db.Devices.Add(device);
                }

                // Exactly one SyncEvent per Document holds the
                // current-progress pointer (SyncEvent.IsCurrent). A single
                // legacy record is that one by definition; where two records
                // shared a hash, the furthest-along keeps it - the same rule
                // the collapsing migration applies to a pair it finds in an
                // already-migrated database.
                currentEventByHash.TryGetValue(legacyDocument.DocumentHash, out var currentEvent);
                var isCurrent = currentEvent is null || legacyDocument.Percentage >= currentEvent.Percentage;

                var syncEvent = new SyncEvent
                {
                    Document = document,
                    Device = device,
                    Progress = legacyDocument.Progress,
                    Percentage = legacyDocument.Percentage,
                    Timestamp = DateTime.SpecifyKind(legacyDocument.Timestamp, DateTimeKind.Utc),
                    IsCurrent = isCurrent,
                };

                if (isCurrent)
                {
                    if (currentEvent is not null)
                    {
                        currentEvent.IsCurrent = false;
                    }

                    currentEventByHash[legacyDocument.DocumentHash] = syncEvent;
                }

                db.SyncEvents.Add(syncEvent);
            }

            db.Users.Add(user);
        }

        db.SaveChanges();

        logger.LogInformation("Migrated {UserCount} user(s) from legacy LiteDB data file [{Path}].", legacyUsers.Count, liteDbPath);
    }
}
