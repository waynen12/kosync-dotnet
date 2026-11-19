using Kosync.Models;

namespace Kosync.Tests;

// Where the database is told to live (issue #47). Pinned down here the way
// PortalOptionsTests pins down the login URL's composition: the point is not
// the path's exact text but that an unset or half-configured section cannot
// move an existing install's database.
public class StorageOptionsTests
{
    [Fact]
    public void WithNothingConfigured_TheDataDirectoryIsTodaysDefault()
    {
        Assert.Equal("data", new StorageOptions().ResolvedDataDirectory);
    }

    // Storage__DataDirectory= in a compose file binds an empty string, not a
    // null, so the property initializer's default never gets a look in.
    // Falling back here means a half-configured install still finds its
    // existing database next time it starts; without it, the database would
    // resolve into the content root itself instead of the "data"
    // subdirectory it has always lived in.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankConfiguredValue_FallsBackToTheShippedDefault(string configured)
    {
        var options = new StorageOptions { DataDirectory = configured };

        Assert.Equal("data", options.ResolvedDataDirectory);
    }

    [Fact]
    public void AConfiguredValue_IsUsedAsIs()
    {
        var options = new StorageOptions { DataDirectory = "/data" };

        Assert.Equal("/data", options.ResolvedDataDirectory);
    }
}
