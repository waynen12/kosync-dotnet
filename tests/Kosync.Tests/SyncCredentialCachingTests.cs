using System.Net;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Authenticating a progress push is a deliberately slow KDF rather than a
// string compare now (ADR 0005), and a reading device pushes a batch per few
// page turns. These tests pin down that a device already authenticated once
// is not made to pay for it again.
public class SyncCredentialCachingTests
{
    [Fact]
    public async Task RepeatProgressPushesFromOneDevice_RunTheKdfOnce()
    {
        var kdf = new CountingPasswordKdf();

        using var factory = new KosyncWebApplicationFactory { Kdf = kdf };
        using var client = factory.CreateClient();

        for (var page = 1; page <= 3; page++)
        {
            var response = await PushProgressAsync(client, percentage: page / 10m);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, kdf.VerifyCount);
    }

    [Fact]
    public async Task ProgressPushAfterAPasswordChange_RunsTheKdfAgain()
    {
        var kdf = new CountingPasswordKdf();

        using var factory = new KosyncWebApplicationFactory { Kdf = kdf };
        using var client = factory.CreateClient();

        var create = new HttpRequestMessage(HttpMethod.Post, "/manage/users")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new UserCreateRequest { username = "reader", password = "first" })
        };
        create.SetCredentials("admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(create)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PushProgressAsync(client, 0.1m, "reader", "first")).StatusCode);

        var change = new HttpRequestMessage(HttpMethod.Put, "/manage/users/password?username=reader")
        {
            Content = System.Net.Http.Json.JsonContent.Create(
                new PasswordChangeRequest { password = "second" })
        };
        change.SetCredentials("admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(change)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PushProgressAsync(client, 0.2m, "reader", "first")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PushProgressAsync(client, 0.3m, "reader", "second")).StatusCode);
    }

    private static async Task<HttpResponseMessage> PushProgressAsync(
        HttpClient client,
        decimal percentage,
        string username = "admin",
        string password = "admin")
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/syncs/progress")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new DocumentRequest
            {
                document = "doc-hash",
                progress = $"page {percentage}",
                percentage = percentage,
                device = "Kobo",
                device_id = "device-1",
            })
        };
        request.SetCredentials(username, password);

        return await client.SendAsync(request);
    }
}
