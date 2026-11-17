using Kosync.Models;
using Kosync.Services;
using Microsoft.Extensions.Options;

namespace Kosync.Tests;

// What the portal will accept as a password a User chose for themselves
// (ADR 0006). The change-password page's tests prove the rules reach a
// person; these pin down the rules themselves, including the configured
// values no page test can reach.
public class PasswordPolicyTests
{
    [Fact]
    public void APasswordAtTheMinimumLength_IsAccepted()
    {
        Assert.Null(Policy(8).Validate("12345678"));
    }

    [Fact]
    public void APasswordBelowTheMinimum_IsRefusedWithTheMinimumNamed()
    {
        var error = Policy(8).Validate("1234567");

        // The 8 is this test's own configured input, so the assertion still
        // disagrees with the code if the message stops carrying the number -
        // which is the point of the format string. A message naming a
        // different minimum than the rule enforces would be worse than none.
        Assert.Equal(string.Format(Constants.ChangePassword.TooShortErrorFormat, 8), error);
    }

    // Re-typing the password being replaced is not a change. The rule lives
    // here rather than on the page because the Users administration page
    // (issue #28) and PUT /manage/users/password need the same answer.
    [Fact]
    public void APasswordThatIsTheCurrentOne_IsRefusedAsUnchanged()
    {
        var error = Policy(8).Validate("the-same-password", currentPassword: "the-same-password");

        Assert.Equal(Constants.ChangePassword.UnchangedPasswordError, error);
    }

    // Ahead of the length rule: `admin` fails both, and "use at least eight
    // characters" would invite the User to pad the password somebody else
    // chose rather than replace it (ADR 0006).
    [Fact]
    public void AnUnchangedPasswordThatIsAlsoTooShort_IsRefusedAsUnchanged()
    {
        var error = Policy(8).Validate(
            Constants.Admin.DefaultPassword, currentPassword: Constants.Admin.DefaultPassword);

        Assert.Equal(Constants.ChangePassword.UnchangedPasswordError, error);
    }

    // An Administrator setting somebody else's password does not know the one
    // being replaced, so the reuse rule is simply not in play - rather than
    // the caller having to fetch a password to pass in.
    [Fact]
    public void WithNoCurrentPasswordGiven_TheReuseRuleDoesNotApply()
    {
        Assert.Null(Policy(8).Validate("12345678"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void AnEmptyOrWhitespacePassword_IsRefusedAsEmptyRatherThanAsShort(string? password)
    {
        // Whitespace is not a short password, it is no password: KOReader
        // will not even attempt to log in with one, so a credential like that
        // cannot do the job it is for.
        Assert.Equal(Constants.ChangePassword.EmptyPasswordError, Policy(8).Validate(password));
    }

    [Fact]
    public void AConfiguredMinimum_ReplacesTheDefault()
    {
        Assert.Equal(16, Policy(16).MinimumLength);
        Assert.NotNull(Policy(16).Validate("still-too-short"));
    }

    // `Passwords__MinimumLength=` or a typo binds to 0, which would turn the
    // rule off rather than relax it - the one outcome a misconfiguration must
    // not produce quietly. Same fallback PasswordHasher's work factor takes,
    // for the same reason. A deployment that really wants no minimum sets 1.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AMisconfiguredMinimum_FallsBackToTheDefault(int configured)
    {
        Assert.Equal(Constants.Passwords.DefaultMinimumPasswordLength, Policy(configured).MinimumLength);
    }

    [Fact]
    public void WithNothingConfigured_TheMinimumIsTheShippedDefault()
    {
        var policy = new PasswordPolicy(Options.Create(new PasswordPolicyOptions()));

        // The literal is the spec: README documents eight characters, so
        // reading the constant here would make the test agree by
        // construction with whatever the code now says.
        Assert.Equal(8, policy.MinimumLength);
    }

    private static PasswordPolicy Policy(int minimumLength)
    {
        return new PasswordPolicy(
            Options.Create(new PasswordPolicyOptions { MinimumLength = minimumLength }));
    }
}
