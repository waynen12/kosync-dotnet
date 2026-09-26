namespace Kosync.Database.Legacy;

public static class LiteDbMigrator
{
    public static void MigrateIfNeeded(KosyncDbContext db, string liteDbPath, ILogger logger)
    {
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
            var user = new User
            {
                Username = legacyUser.Username,
                PasswordHash = legacyUser.PasswordHash,
                IsActive = legacyUser.IsActive,
                IsAdministrator = legacyUser.IsAdministrator,
            };

            var devicesByDeviceId = new Dictionary<string, Device>();

            foreach (var legacyDocument in legacyUser.Documents)
            {
                var book = new Book();
                var document = new Document
                {
                    DocumentHash = legacyDocument.DocumentHash,
                    User = user,
                    Book = book,
                };

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

                var syncEvent = new SyncEvent
                {
                    Document = document,
                    Device = device,
                    Progress = legacyDocument.Progress,
                    Percentage = legacyDocument.Percentage,
                    Timestamp = DateTime.SpecifyKind(legacyDocument.Timestamp, DateTimeKind.Utc),
                    IsCurrent = true,
                };

                user.Documents.Add(document);
                db.Books.Add(book);
                db.Documents.Add(document);
                db.SyncEvents.Add(syncEvent);
            }

            db.Users.Add(user);
        }

        db.SaveChanges();

        logger.LogInformation("Migrated {UserCount} user(s) from legacy LiteDB data file [{Path}].", legacyUsers.Count, liteDbPath);
    }
}
