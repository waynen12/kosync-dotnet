using Kosync.Models;
using Microsoft.AspNetCore.Http;

namespace Kosync.Tests;

// Issue #51: the shipped default for the portal's session cookie must be
// Always (HTTPS-only), not the SameAsRequest that both run-local.sh and
// KosyncWebApplicationFactory override back to for the dev loop and this
// suite. Asserted against the bare POCO, with nothing configured and no
// fixture involved, so an edit to either of those overrides cannot quietly
// become the production default without this test noticing.
//
// Bound as a string and parsed leniently for the same reason as
// SyncTraceOptions: configuration binding throws on a value it cannot
// convert, and a typo in this setting must not be a worse outage than the
// plain-HTTP cookie it exists to prevent.
public class AuthenticationOptionsTests
{
    [Fact]
    public void WithNothingConfigured_TheCookieSecurePolicyIsAlwaysHttps()
    {
        Assert.Equal(CookieSecurePolicy.Always, new AuthenticationOptions().ResolvedCookieSecurePolicy);
    }

    [Theory]
    [InlineData("SameAsRequest")]
    [InlineData("sameasrequest")]
    [InlineData(" SameAsRequest ")]
    public void AConfiguredPolicy_IsWhatGetsUsedHoweverItWasTyped(string configured)
    {
        Assert.Equal(
            CookieSecurePolicy.SameAsRequest,
            new AuthenticationOptions { CookieSecurePolicy = configured }.ResolvedCookieSecurePolicy);
    }

    // Including blank, which falls back to the shipped default rather than
    // to the enum's own zero value (None - no Secure attribute at all,
    // under any scheme) for the same reason an unparseable SyncTrace limit
    // falls back to the shipped default instead of to zero bodies.
    [Theory]
    [InlineData("Alwyas")]
    [InlineData("https-only")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnparseableValue_FallsBackToTheShippedDefault(string configured)
    {
        var options = new AuthenticationOptions { CookieSecurePolicy = configured };

        Assert.Equal(CookieSecurePolicy.Always, options.ResolvedCookieSecurePolicy);
    }
}
