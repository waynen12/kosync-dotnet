using Kosync.Models;

namespace Kosync.Tests;

// What an operator's configured values turn into, including the values they
// should not have typed. The middleware's own tests prove the settings are
// honoured; these are about the parsing, which is deliberately lenient:
// configuration binding throws on a value it cannot convert, and a mistyped
// diagnostic setting must not fault every request.
public class SyncTraceOptionsTests
{
    [Fact]
    public void WithNothingConfigured_TheTraceIsOffAtTheShippedBodyLimit()
    {
        var options = new SyncTraceOptions();

        Assert.False(options.IsEnabled);
        Assert.Equal(Constants.SyncTrace.DefaultMaxBodyCharacters, options.BodyCharacterLimit);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    public void TheValueThatMeansOn_TurnsItOnHoweverItWasTyped(string configured)
    {
        Assert.True(new SyncTraceOptions { Enabled = configured }.IsEnabled);
    }

    // Including "yes", which binding to a `bool` would have thrown on. Off is
    // the safe way round: a trace that failed to start is a worse diagnosis
    // than a trace that never did, but both beat a server that 500s.
    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingElse_LeavesItOff(string configured)
    {
        Assert.False(new SyncTraceOptions { Enabled = configured }.IsEnabled);
    }

    [Fact]
    public void AConfiguredLimit_IsWhatGetsUsed()
    {
        Assert.Equal(64, new SyncTraceOptions { MaxBodyCharacters = "64" }.BodyCharacterLimit);
    }

    // Zero is a real thing to ask for - trace the exchange, none of its
    // bodies - and a negative is a typo for it.
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ANegativeLimit_MeansNoBodyRatherThanAnException(string configured)
    {
        var options = new SyncTraceOptions { MaxBodyCharacters = configured };

        Assert.Equal(Constants.SyncTrace.MinimumMaxBodyCharacters, options.BodyCharacterLimit);
    }

    // Not a number at all is a different mistake from a number out of range,
    // and falls back to the shipped default rather than to no bodies: the
    // operator was asking for bodies, they just misspelled how many.
    [Theory]
    [InlineData("lots")]
    [InlineData("2048 characters")]
    public void AnUnparseableLimit_FallsBackToTheShippedDefault(string configured)
    {
        var options = new SyncTraceOptions { MaxBodyCharacters = configured };

        Assert.Equal(Constants.SyncTrace.DefaultMaxBodyCharacters, options.BodyCharacterLimit);
    }
}
