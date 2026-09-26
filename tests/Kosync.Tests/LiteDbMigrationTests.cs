using System.Net;
using System.Text.Json;
using Kosync.Database;
using Kosync.Database.Legacy;
using Kosync.Tests.TestSupport;
using LiteDB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kosync.Tests;

public class LiteDbMigrationTests
{
    [Fact]
    public async Task ExistingLiteDbUsersAndProgress_AreMigratedIntoSqlite()
    {
        var fixturePath = Path.Combine(Path.GetTempPath(), $"kosync-legacy-{Guid.NewGuid()}.db");
        try
        {
            SeedLegacyFixture(fixturePath);

            using var factory = new KosyncWebApplicationFactory();
            factory.SeedLegacyLiteDbFile(fixturePath);
            using var client = factory.CreateClient();

            var authRequest = new HttpRequestMessage(HttpMethod.Get, "/users/auth");
            authRequest.SetCredentialsWithRawKey("legacyuser", "raw-hashed-key");
            var authResponse = await client.SendAsync(authRequest);
            Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);

            // The inactive legacy user's IsActive flag must also survive the import.
            var inactiveAuthRequest = new HttpRequestMessage(HttpMethod.Get, "/users/auth");
            inactiveAuthRequest.SetCredentialsWithRawKey("inactivereader", "raw-key-2");
            var inactiveAuthResponse = await client.SendAsync(inactiveAuthRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, inactiveAuthResponse.StatusCode);

            var progressRequest = new HttpRequestMessage(HttpMethod.Get, "/syncs/progress/book1hash");
            progressRequest.SetCredentialsWithRawKey("legacyuser", "raw-hashed-key");
            var progressResponse = await client.SendAsync(progressRequest);
            var body = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(await progressResponse.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);
            Assert.Equal("50%", body.GetProperty("progress").GetString());
            Assert.Equal(0.5m, body.GetProperty("percentage").GetDecimal());
            Assert.Equal("OldKobo", body.GetProperty("device").GetString());
            Assert.Equal("old-device-1", body.GetProperty("device_id").GetString());

            // legacyuser's second document (a different book) must also have made the trip.
            var documentsRequest = new HttpRequestMessage(HttpMethod.Get, "/manage/users/documents?username=legacyuser");
            documentsRequest.SetCredentialsWithRawKey("legacyuser", "raw-hashed-key");
            var documentsResponse = await client.SendAsync(documentsRequest);
            var documentsBody = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(await documentsResponse.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, documentsResponse.StatusCode);
            var documentHashes = documentsBody.EnumerateArray().Select(d => d.GetProperty("documentHash").GetString()).ToList();
            Assert.Equal(2, documentHashes.Count);
            Assert.Contains("book1hash", documentHashes);
            Assert.Contains("book2hash", documentHashes);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    [Fact]
    public void MigrateIfNeeded_DoesNotDuplicateData_WhenRunTwice()
    {
        var fixturePath = Path.Combine(Path.GetTempPath(), $"kosync-legacy-{Guid.NewGuid()}.db");
        try
        {
            SeedLegacyFixture(fixturePath);

            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite(connection).Options;
            using var db = new KosyncDbContext(options);
            db.Database.Migrate();

            LiteDbMigrator.MigrateIfNeeded(db, fixturePath, NullLogger.Instance);
            Assert.Equal(2, db.Users.Count());
            Assert.Equal(3, db.Documents.Count());
            Assert.Equal(3, db.SyncEvents.Count());

            LiteDbMigrator.MigrateIfNeeded(db, fixturePath, NullLogger.Instance);
            Assert.Equal(2, db.Users.Count());
            Assert.Equal(3, db.Documents.Count());
            Assert.Equal(3, db.SyncEvents.Count());
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    private static void SeedLegacyFixture(string fixturePath)
    {
        using var liteDb = new LiteDatabase($"Filename={fixturePath};Connection=direct");

        var legacyUser = new LegacyUser
        {
            Username = "legacyuser",
            PasswordHash = "raw-hashed-key",
            IsActive = true,
            IsAdministrator = false,
            Documents = new List<LegacyDocument>
            {
                new LegacyDocument
                {
                    DocumentHash = "book1hash",
                    Progress = "50%",
                    Percentage = 0.5m,
                    Device = "OldKobo",
                    DeviceId = "old-device-1",
                    Timestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                },
                new LegacyDocument
                {
                    DocumentHash = "book2hash",
                    Progress = "10%",
                    Percentage = 0.1m,
                    Device = "OldKobo",
                    DeviceId = "old-device-1",
                    Timestamp = new DateTime(2025, 1, 2, 9, 0, 0, DateTimeKind.Utc),
                }
            }
        };

        var inactiveLegacyUser = new LegacyUser
        {
            Username = "inactivereader",
            PasswordHash = "raw-key-2",
            IsActive = false,
            IsAdministrator = false,
            Documents = new List<LegacyDocument>
            {
                new LegacyDocument
                {
                    DocumentHash = "book3hash",
                    Progress = "5%",
                    Percentage = 0.05m,
                    Device = "OldNook",
                    DeviceId = "old-device-2",
                    Timestamp = new DateTime(2025, 1, 3, 9, 0, 0, DateTimeKind.Utc),
                }
            }
        };

        var usersCollection = liteDb.GetCollection<LegacyUser>("users");
        usersCollection.Insert(legacyUser);
        usersCollection.Insert(inactiveLegacyUser);
    }
}
