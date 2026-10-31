using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    // What the seeded admin's password counts as once the app has started.
    //
    // The app really seeds a Provisional one (ADR 0006), which the portal and
    // the sync API refuse until the User replaces it. Roughly two dozen tests
    // in this suite log in as admin/admin to get at something else entirely,
    // and would all fail on that refusal - so the default here is an admin
    // who has already replaced theirs, and a test about the forced change
    // asks for Provisional instead.
    public PasswordState AdminPasswordState { get; init; } = PasswordState.UserChosen;

    // Stands in for the operator's own Portal:BaseUrl. Null leaves the
    // setting unconfigured, so the app falls back to its shipped default the
    // way a deployment that never set it does.
    public string? PortalBaseUrl { get; init; }

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
        Environment.SetEnvironmentVariable(
            Constants.EnvironmentVariables.AdminPassword, Constants.Admin.DefaultPassword);
        Environment.SetEnvironmentVariable(Constants.EnvironmentVariables.SelfRegistrationEnabled, null);

        // Would otherwise reset the seeded admin to a Provisional password on
        // startup if it happened to be set in the shell or on CI.
        Environment.SetEnvironmentVariable(Constants.EnvironmentVariables.AdminPasswordReset, null);
    }

    // The seeding in KosyncDbInitializer runs as the host starts, so this is
    // the first moment AdminPasswordState can be applied: CreateHost is the
    // one hook that gets to run after the app's own startup pass.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        if (AdminPasswordState is not PasswordState.Provisional)
        {
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>()
                .BypassFilters(Constants.Tenancy.TestFixtureBypassReason);

            var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
            db.Users.Single(u => u.Username == Constants.Admin.Username).PasswordState = AdminPasswordState;
            db.SaveChanges();
        }

        return host;
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

        using var db = TestDb.CreateSystemContext(options);
        db.Database.Migrate();

        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = storedPasswordHash,
            // A row that predates PasswordState is one the AddUserPasswordState
            // migration takes as UserChosen: the password works today and
            // upgrading the server is not a reason to lock anybody out
            // (ADR 0006). Said here rather than left at the entity default,
            // because the default is Provisional and that would make this
            // fixture a User the real migration never produces.
            PasswordState = PasswordState.UserChosen,
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

        if (PortalBaseUrl is not null)
        {
            builder.UseSetting($"{Constants.Portal.ConfigurationSection}:BaseUrl", PortalBaseUrl);
        }

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
