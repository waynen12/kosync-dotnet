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

        // After CreateDefaults, so there is always an admin row to act on.
        // The other order would leave a fresh install's reset token
        // unrecorded and therefore still pending, firing on the next restart
        // against a password the admin had meanwhile chosen.
        ResetAdminPasswordIfRequested(db, passwordHasher, logger);
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
        var adminUser = db.Users.FirstOrDefault(u => u.Username == Constants.Admin.Username);

        if (adminUser is null)
        {
            adminUser = new User()
            {
                Username = Constants.Admin.Username,
                // Chosen by the installer rather than by the admin, so it
                // grants nothing until replaced (ADR 0006).
                PasswordHash = passwordHasher.HashPlainText(SeedAdminPassword),
                PasswordState = PasswordState.Provisional,
            };

            db.Users.Add(adminUser);
        }

        // Reasserted on every startup, unlike the password: these are what
        // makes the account the administrator, and an install whose only
        // admin had been deactivated would otherwise have nobody who can
        // manage accounts.
        adminUser.IsActive = true;
        adminUser.IsAdministrator = true;

        db.SaveChanges();
    }

    // Recovery from a genuinely lost admin password (ADR 0006). It returns
    // the admin to a *Provisional* password rather than to a working one, so
    // the recovery path cannot itself leave a usable credential sitting in an
    // env var: all the operator can do with it is log in and replace it.
    //
    // Acted on once per distinct value, because the variable stays set in the
    // compose file that triggered it. Honouring it whenever it is merely
    // present would reinstate exactly the reset-on-every-boot behaviour this
    // ADR removed from ADMIN_PASSWORD.
    private static void ResetAdminPasswordIfRequested(KosyncDbContext db, PasswordHasher passwordHasher, ILogger logger)
    {
        var requestedToken = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.AdminPasswordReset);

        if (string.IsNullOrWhiteSpace(requestedToken))
        {
            return;
        }

        var applied = db.ServerSettings
            .FirstOrDefault(s => s.Key == Constants.ServerSettings.AppliedAdminPasswordReset);

        if (applied is not null && applied.Value == requestedToken)
        {
            return;
        }

        var adminUser = db.Users.Single(u => u.Username == Constants.Admin.Username);

        adminUser.PasswordHash = passwordHasher.HashPlainText(SeedAdminPassword);
        adminUser.PasswordState = PasswordState.Provisional;

        if (applied is null)
        {
            db.ServerSettings.Add(new ServerSetting
            {
                Key = Constants.ServerSettings.AppliedAdminPasswordReset,
                Value = requestedToken,
            });
        }
        else
        {
            applied.Value = requestedToken;
        }

        db.SaveChanges();

        logger.LogWarning(
            "{Variable} has returned user [{Username}] to a provisional password; it grants nothing until replaced in the portal. Acted on once for this value - change it to recover again.",
            Constants.EnvironmentVariables.AdminPasswordReset,
            Constants.Admin.Username);
    }

    // ADMIN_PASSWORD is read only where an admin account is being created -
    // on a fresh install, or by the one-shot reset. It is deliberately not
    // consulted for an admin that already exists (ADR 0006).
    private static string SeedAdminPassword =>
        Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.AdminPassword)
            ?? Constants.Admin.DefaultPassword;
}
