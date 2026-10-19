using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Spins up the real app against a private in-memory SQLite database (one
// per factory instance) so tests never touch the developer's data/ folder.
// Each test constructs its own factory to keep databases from leaking
// state across tests.
public class KosyncWebApplicationFactory : WebApplicationFactory<Program>
{
    // Opened in the constructor rather than when the host is configured, so
    // tests can put rows in the database before the app's first startup pass
    // sees it. An in-memory SQLite database lives only as long as a
    // connection to it is held open.
    private readonly SqliteConnection _connection;

    public int WorkFactor { get; init; } = Constants.Passwords.MinimumWorkFactor;

    // Replaces the app's KDF, so a test can observe how often the real
    // server actually runs it across requests.
    public IPasswordKdf? Kdf { get; init; }

    public string ContentRoot { get; }

    public string DataDirectory => Path.Combine(ContentRoot, "data");

    public KosyncWebApplicationFactory()
    {
        ContentRoot = Directory.CreateTempSubdirectory("kosync-tests-").FullName;

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Force deterministic values regardless of what's set in the ambient
        // shell/CI environment - these are read directly from the process
        // environment by the app, not from configuration.
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "admin");
        Environment.SetEnvironmentVariable("REGISTRATION_DISABLED", null);
    }

    // Must be called before the first client/server is created (i.e. before
    // CreateClient()) so the app sees the file on its first startup pass.
    public void SeedLegacyLiteDbFile(string sourceFilePath)
    {
        Directory.CreateDirectory(DataDirectory);
        File.Copy(sourceFilePath, Path.Combine(DataDirectory, "Kosync.db"), overwrite: true);
    }

    // Writes a User straight into the database with whatever is given as its
    // stored credential - including a pre-ADR-0005 bare MD5. Must be called
    // before the first client/server is created so the app's startup pass
    // sees the row.
    public void SeedUserWithStoredPasswordHash(string username, string storedPasswordHash)
    {
        var options = new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite(_connection).Options;

        using var db = new KosyncDbContext(options);
        db.Database.Migrate();

        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = storedPasswordHash,
        });

        db.SaveChanges();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(ContentRoot);

        // The production work factor costs ~250ms per hash and every test
        // seeds an admin credential on startup. These tests are about whether
        // the right value reaches the verifier, not about KDF cost, so they
        // run at bcrypt's minimum - which also exercises the work factor
        // actually being configurable.
        builder.UseSetting($"{Constants.Passwords.ConfigurationSection}:WorkFactor", WorkFactor.ToString());

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<KosyncDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<KosyncDbContext>(options => options.UseSqlite(_connection));

            if (Kdf is not null)
            {
                services.AddSingleton<IPasswordKdf>(Kdf);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _connection.Dispose();

        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
