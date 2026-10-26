using System.Net.Http.Json;
using System.Text.Json;
using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests.TestSupport;

// xUnit creates a fresh instance of the test class per [Fact]/[Theory], so
// the factory (and its private in-memory database) is naturally isolated
// per test with no shared state to reset between tests.
public abstract class IntegrationTestBase : IDisposable
{
    protected KosyncWebApplicationFactory Factory { get; }
    protected HttpClient Client { get; }

    protected IntegrationTestBase()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient();
    }

    // The default fixture: an admin who has already replaced their seeded
    // password, because most tests log in as admin/admin to get at something
    // else (see SeededAdminFixtureTests). A test class whose subject *is* the
    // fixture - a Provisional Password, a configured setting - overrides this
    // rather than standing up a second host alongside the inherited one.
    protected virtual KosyncWebApplicationFactory CreateFactory() => new();

    protected async Task<HttpResponseMessage> GetAsync(string url, string? username = null, string? password = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (username is not null && password is not null)
        {
            request.SetCredentials(username, password);
        }

        return await Client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string url, string? username = null, string? password = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        if (username is not null && password is not null)
        {
            request.SetCredentials(username, password);
        }

        return await Client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> PostAsync<T>(string url, T body, string? username = null, string? password = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        if (username is not null && password is not null)
        {
            request.SetCredentials(username, password);
        }

        return await Client.SendAsync(request);
    }

    protected async Task<HttpResponseMessage> PutAsync<T>(string url, T body, string? username = null, string? password = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body)
        };
        if (username is not null && password is not null)
        {
            request.SetCredentials(username, password);
        }

        return await Client.SendAsync(request);
    }

    // A second User who can actually sync. Created the way an Administrator
    // creates one, then taken past the forced password change - because the
    // Provisional Password an Administrator hands over refuses sync
    // (ADR 0006), and the tests reaching for this are about tenancy, document
    // counts or credential caching rather than about that refusal. The tests
    // that *are* about it live in ProvisionalPasswordSyncRefusalTests.
    protected async Task CreateSyncingUserAsync(string username, string password)
    {
        var response = await PostAsync(
            "/manage/users",
            new UserCreateRequest { username = username, password = password },
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);

        response.EnsureSuccessStatusCode();

        Factory.SetPasswordState(username, PasswordState.UserChosen);
    }

    protected static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    // Drives the manual Split Book merge action (issue #8) for tests that
    // just need two Documents already merged into one Book: each
    // auto-created its own 1:1 Book on first sync, this repoints both onto
    // a single Book via the real BookDashboardService. Returns the
    // surviving Book's id.
    protected async Task<int> MergeIntoOneBookAsync(string keepDocumentHash, string mergeDocumentHash)
    {
        using var scope = Factory.CreateScopeForUser("admin");
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var keep = await db.Documents.SingleAsync(d => d.DocumentHash == keepDocumentHash);
        var merge = await db.Documents.SingleAsync(d => d.DocumentHash == mergeDocumentHash);

        var service = new BookDashboardService(db);
        await service.MergeBooksAsync(keep.BookId, merge.BookId);

        return keep.BookId;
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
