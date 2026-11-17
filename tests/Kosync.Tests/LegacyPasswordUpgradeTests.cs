using System.Net;
using Kosync.Database;
using Kosync.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Credentials stored before ADR 0005 are bare MD5s. Because that is exactly
// the inner step of bcrypt(md5(password)), they can be wrapped in place - no
// plaintext, no password reset.
public class LegacyPasswordUpgradeTests
{
    [Fact]
    public async Task UserStoredAsABareMd5_StillAuthenticatesAndIsRehashedOnStartup()
    {
        var legacyStoredHash = Utility.Md5Hash("old password");

        using var factory = new KosyncWebApplicationFactory();
        factory.SeedUserWithStoredPasswordHash("legacyreader", legacyStoredHash);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/users/auth");
        request.SetCredentials("legacyreader", "old password");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(legacyStoredHash, StoredHashOf(factory, "legacyreader"));
    }

    private static string StoredHashOf(KosyncWebApplicationFactory factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return db.Users.Single(u => u.Username == username).PasswordHash;
    }
}
