using System.Net.Http.Json;
using System.Text.Json;

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
        Factory = new KosyncWebApplicationFactory();
        Client = Factory.CreateClient();
    }

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

    protected static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
