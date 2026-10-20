using Kosync.Database.Legacy;

namespace Kosync.Database;

public static class KosyncDbInitializer
{
    // `tenant` is taken rather than assumed so that the bypass below is a
    // visible part of this method's contract: startup is one of the three
    // places ADR 0004 allows outside the tenant filters, and a caller handing
    // over a tenant that is already acting for a User gets an exception
    // instead of a silently half-scoped migration.
    public static void Initialize(KosyncDbContext db, TenantContext tenant, string dataDirectory, PasswordHasher passwordHasher, ILogger logger)
    {
        // Startup migrates the schema, imports the legacy LiteDB file and
        // seeds the first admin. All of that happens before any User could
        // have logged in, so there is no tenant to scope to - and under the
        // fail-closed default, an unscoped read here would see nothing at all
        // and the seeding would silently do the wrong thing.
        tenant.BypassFilters(Constants.Tenancy.StartupBypassReason);

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
