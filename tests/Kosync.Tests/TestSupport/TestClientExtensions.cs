using Microsoft.AspNetCore.Mvc.Testing;

namespace Kosync.Tests.TestSupport;

public static class TestClientExtensions
{
    public static HttpClient NoRedirectClient(this KosyncWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }
}
