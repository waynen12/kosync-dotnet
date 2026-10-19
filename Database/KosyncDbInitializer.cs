using Kosync.Database.Legacy;

namespace Kosync.Database;

public static class KosyncDbInitializer
{
    public static void Initialize(KosyncDbContext db, string dataDirectory, PasswordHasher passwordHasher, ILogger logger)
    {
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        db.Database.Migrate();

        LiteDbMigrator.MigrateIfNeeded(db, Path.Combine(dataDirectory, "Kosync.db"), passwordHasher, logger);

        UpgradePasswordHashes(db, passwordHasher, logger);

        CreateDefaults(db, passwordHasher);
    }

    // Credentials written before ADR 0005 are bare MD5s. The stored value is
    // already md5(password), which is exactly the inner step of the new
    // scheme, so upgrading is bcrypt(stored) - no plaintext and no password
    // reset. Runs on every startup and is a no-op once every row is wrapped.
    private static void UpgradePasswordHashes(KosyncDbContext db, PasswordHasher passwordHasher, ILogger logger)
    {
        var legacyUsers = db.Users
            .ToList()
            .Where(u => passwordHasher.NeedsUpgrade(u.PasswordHash))
            .ToList();

        if (legacyUsers.Count == 0)
        {
            return;
        }

        foreach (var user in legacyUsers)
        {
            user.PasswordHash = passwordHasher.HashAuthKey(user.PasswordHash);
        }

        db.SaveChanges();

        logger.LogInformation("Upgraded {UserCount} stored password(s) to the hashed scheme.", legacyUsers.Count);
    }

    private static void CreateDefaults(KosyncDbContext db, PasswordHasher passwordHasher)
    {
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (adminPassword is null)
        {
            adminPassword = "admin";
        }

        var adminUser = db.Users.FirstOrDefault(u => u.Username == "admin");
        if (adminUser is null)
        {
            adminUser = new User()
            {
                Username = "admin",
                IsAdministrator = true,
            };
            db.Users.Add(adminUser);
        }

        adminUser.PasswordHash = passwordHasher.HashPlainText(adminPassword);
        adminUser.IsActive = true;
        adminUser.IsAdministrator = true;

        db.SaveChanges();
    }
}
