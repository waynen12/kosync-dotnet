using Kosync.Models;

namespace Kosync.Tests;

// What the refused device is told to go and visit (ADR 0006). The URL is
// composed rather than configured whole, so the composition is worth pinning
// down here: the endpoints' own tests prove the message carries it, not how
// it is built from an operator's value.
public class PortalOptionsTests
{
    [Fact]
    public void WithNothingConfigured_TheLoginUrlIsTheShippedDefault()
    {
        Assert.Equal("http://localhost:8080/login", new PortalOptions().LoginUrl);
    }

    [Theory]
    [InlineData("https://kosync.example.org")]
    [InlineData("https://kosync.example.org/")]
    public void ATrailingSlash_DoesNotBecomeADoubleSlash(string configured)
    {
        var options = new PortalOptions { BaseUrl = configured };

        Assert.Equal("https://kosync.example.org/login", options.LoginUrl);
    }

    // `Portal__BaseUrl=` in a compose file binds an empty string, not a null,
    // so the property initializer's default never gets a look in. Falling back
    // here means the message still names a host; without it the operator who
    // half-configured this ships "log in at /login", which is useless in
    // exactly the case the message exists for.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankConfiguredValue_FallsBackToTheShippedDefault(string configured)
    {
        var options = new PortalOptions { BaseUrl = configured };

        Assert.Equal("http://localhost:8080/login", options.LoginUrl);
    }
}
