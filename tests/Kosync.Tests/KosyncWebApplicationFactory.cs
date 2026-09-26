using Kosync.Database;
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
    private SqliteConnection? _connection;

    public string ContentRoot { get; }

    public string DataDirectory => Path.Combine(ContentRoot, "data");

    public KosyncWebApplicationFactory()
    {
        ContentRoot = Directory.CreateTempSubdirectory("kosync-tests-").FullName;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(ContentRoot);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<KosyncDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<KosyncDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _connection?.Dispose();

        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
