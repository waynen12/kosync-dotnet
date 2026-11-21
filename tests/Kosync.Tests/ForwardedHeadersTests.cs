using System.Net;
using Microsoft.AspNetCore.Http;

namespace Kosync.Tests;

// Issue #52: app.UseForwardedHeaders() was a no-op, since nothing ever
// configured ForwardedHeadersOptions - the shipped default is
// ForwardedHeaders.None. These prove it now rewrites Request.Scheme from
// X-Forwarded-Proto, but only for a request whose immediate peer is inside
// a configured network - never unconditionally, and never by default.
//
// Driven through TestServer.SendAsync rather than the HttpClient the rest of
// the suite uses: the rewrite lands on HttpContext.Request.Scheme, which an
// HttpResponseMessage has no way to observe, and SendAsync is also the one
// place a test can put a specific address on Connection.RemoteIpAddress -
// the thing the middleware actually gates on.
public class ForwardedHeadersTests
{
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("172.20.0.5");

    private static async Task<HttpContext> SendAsync(
        KosyncWebApplicationFactory factory, IPAddress remoteAddress, string? forwardedProto = "https")
    {
        return await factory.Server.SendAsync(context =>
        {
            context.Request.Method = "GET";
            context.Request.Path = "/login";
            context.Connection.RemoteIpAddress = remoteAddress;

            if (forwardedProto is not null)
            {
                context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
            }
        });
    }

    [Fact]
    public async Task WithNoNetworkConfigured_AForwardedProtoHeader_IsIgnored()
    {
        using var factory = new KosyncWebApplicationFactory();

        var context = await SendAsync(factory, ProxyAddress);

        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task WithTheRequestsNetworkConfigured_AForwardedProtoHeader_IsHonoured()
    {
        using var factory = new KosyncWebApplicationFactory
        {
            ForwardedHeadersKnownNetworks = "172.20.0.0/16",
        };

        var context = await SendAsync(factory, ProxyAddress);

        Assert.Equal("https", context.Request.Scheme);
    }

    // The configured network is a allow-list, not a switch: a peer outside
    // it is exactly the unconfigured case, even once some other network has
    // been named.
    [Fact]
    public async Task FromAPeerOutsideTheConfiguredNetwork_AForwardedProtoHeader_IsStillIgnored()
    {
        using var factory = new KosyncWebApplicationFactory
        {
            ForwardedHeadersKnownNetworks = "172.20.0.0/16",
        };

        var context = await SendAsync(factory, IPAddress.Parse("203.0.113.9"));

        Assert.Equal("http", context.Request.Scheme);
    }
}
