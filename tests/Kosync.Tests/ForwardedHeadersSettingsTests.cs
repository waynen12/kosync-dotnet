using Kosync.Models;

namespace Kosync.Tests;

// What an operator's configured KnownNetworks turns into. The middleware's
// own behaviour - honoured or ignored - is ForwardedHeadersTests' business;
// these are about the parsing, and about pinning the one fact issue #52
// exists to guarantee: nothing configured parses to nothing trusted, so the
// permissive configuration can never arrive by accident.
public class ForwardedHeadersSettingsTests
{
    [Fact]
    public void WithNothingConfigured_NoNetworksAreTrusted()
    {
        Assert.Empty(new ForwardedHeadersSettings().ParsedKnownNetworks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankValue_AlsoTrustsNoNetworks(string configured)
    {
        Assert.Empty(new ForwardedHeadersSettings { KnownNetworks = configured }.ParsedKnownNetworks);
    }

    [Fact]
    public void ACidrNetwork_Parses()
    {
        var settings = new ForwardedHeadersSettings { KnownNetworks = "172.20.0.0/16" };

        var network = Assert.Single(settings.ParsedKnownNetworks);
        Assert.True(network.Contains(System.Net.IPAddress.Parse("172.20.5.9")));
        Assert.False(network.Contains(System.Net.IPAddress.Parse("172.21.0.1")));
    }

    // A bare address is accepted as the one-host network it implies, the
    // same leniency TRUSTED_PROXIES allows a single proxy address.
    [Fact]
    public void ABareAddress_IsTreatedAsAOneHostNetwork()
    {
        var settings = new ForwardedHeadersSettings { KnownNetworks = "10.0.0.5" };

        var network = Assert.Single(settings.ParsedKnownNetworks);
        Assert.True(network.Contains(System.Net.IPAddress.Parse("10.0.0.5")));
        Assert.False(network.Contains(System.Net.IPAddress.Parse("10.0.0.6")));
    }

    [Fact]
    public void SeveralNetworks_AreAllParsed()
    {
        var settings = new ForwardedHeadersSettings { KnownNetworks = "10.0.0.0/24, 172.20.0.0/16" };

        Assert.Equal(2, settings.ParsedKnownNetworks.Count);
    }

    // A typo in one entry does not sink the ones either side of it - the
    // same leniency ProxyService.TryParseProxy allows.
    [Fact]
    public void AnUnparseableEntry_IsSkippedRatherThanThrowing()
    {
        var settings = new ForwardedHeadersSettings { KnownNetworks = "10.0.0.0/24, not-a-network, 172.20.0.0/16" };

        Assert.Equal(2, settings.ParsedKnownNetworks.Count);
    }
}
