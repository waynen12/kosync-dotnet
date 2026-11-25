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

    // Bound retention (issue #50), parsed the same lenient way as
    // SyncTraceOptions.MaxBodyCharacters: binding straight to an `int` would
    // throw on a mistyped value, and a typo here must not be a worse outage
    // than the disk it is there to protect.
    [Fact]
    public void WithNothingConfigured_RetentionIsTodaysDefault()
    {
        Assert.Equal(Constants.Storage.DefaultMaxBackupsRetained, new StorageOptions().ResolvedMaxBackupsRetained);
    }

    [Fact]
    public void AConfiguredRetentionCount_IsWhatGetsUsed()
    {
        Assert.Equal(3, new StorageOptions { MaxBackupsRetained = "3" }.ResolvedMaxBackupsRetained);
    }

    // A limit below the minimum would let the backup just taken be pruned on
    // the same pass that created it, so it is clamped up rather than honoured
    // as-is.
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void ARetentionCountBelowTheMinimum_IsClampedUpToIt(string configured)
    {
        var options = new StorageOptions { MaxBackupsRetained = configured };

        Assert.Equal(Constants.Storage.MinimumMaxBackupsRetained, options.ResolvedMaxBackupsRetained);
    }

    [Theory]
    [InlineData("lots")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnparseableRetentionCount_FallsBackToTheShippedDefault(string configured)
    {
        var options = new StorageOptions { MaxBackupsRetained = configured };

        Assert.Equal(Constants.Storage.DefaultMaxBackupsRetained, options.ResolvedMaxBackupsRetained);
    }
}
