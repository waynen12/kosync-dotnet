using Kosync.Database.Legacy;

namespace Kosync.Database;

public static class KosyncDbInitializer
{
    public static void Initialize(KosyncDbContext db, string dataDirectory, ILogger logger)
    {
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        db.Database.Migrate();

        LiteDbMigrator.MigrateIfNeeded(db, Path.Combine(dataDirectory, "Kosync.db"), logger);

        CreateDefaults(db);
    }

    private static void CreateDefaults(KosyncDbContext db)
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

        adminUser.PasswordHash = Utility.HashPassword(adminPassword);
        adminUser.IsActive = true;
        adminUser.IsAdministrator = true;

        db.SaveChanges();
    }
}
