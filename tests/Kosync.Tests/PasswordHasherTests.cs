using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// The storage scheme is bcrypt over the MD5 that KOReader puts on the wire
// (ADR 0005), so these tests work in terms of both inputs: the plaintext a
// portal user types, and the MD5 auth key a sync client sends.
public class PasswordHasherTests
{
    [Fact]
    public void HashOfPlainText_VerifiesAgainstTheAuthKeyKoreaderSends()
    {
        var hasher = TestPasswords.CreateHasher();

        var stored = hasher.HashPlainText("correct horse");

        Assert.True(hasher.VerifyAuthKey(Utility.Md5Hash("correct horse"), stored));
    }

    [Fact]
    public void StoredCredential_UsesTheConfiguredWorkFactor()
    {
        var stored = TestPasswords.CreateHasher(workFactor: 5).HashPlainText("correct horse");

        Assert.Equal(5, WorkFactorOf(stored));
    }

    // The literal is the point: ADR 0005 and the issue both name 12, so this
    // asserts the documented number rather than whatever the constant says.
    [Fact]
    public void WithNothingConfigured_StoredCredentialsUseAWorkFactorOf12()
    {
        var stored = TestPasswords.CreateHasherWithDefaults().HashPlainText("correct horse");

        Assert.Equal(12, WorkFactorOf(stored));
    }

    // A work factor bcrypt would reject - including the 0 that an unparseable
    // setting binds to - must not quietly become the weakest hash bcrypt can
    // produce.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void WorkFactorOutsideWhatBcryptAccepts_FallsBackToTheDefault(int configured)
    {
        var stored = TestPasswords.CreateHasher(workFactor: configured).HashPlainText("correct horse");

        Assert.Equal(Constants.Passwords.DefaultWorkFactor, WorkFactorOf(stored));
    }

    [Fact]
    public void RepeatVerificationOfTheSameCredential_RunsTheKdfOnce()
    {
        var kdf = new CountingPasswordKdf();
        var hasher = TestPasswords.CreateHasher(kdf: kdf);
        var stored = hasher.HashPlainText("correct horse");
        var authKey = Utility.Md5Hash("correct horse");

        Assert.True(hasher.VerifyAuthKey(authKey, stored));
        Assert.True(hasher.VerifyAuthKey(authKey, stored));
        Assert.True(hasher.VerifyAuthKey(authKey, stored));

        Assert.Equal(1, kdf.VerifyCount);
    }

    [Fact]
    public void WithTheCacheLifetimeConfiguredToZero_EveryVerificationRunsTheKdf()
    {
        var kdf = new CountingPasswordKdf();
        var hasher = TestPasswords.CreateHasher(kdf: kdf, verifiedCredentialCacheSeconds: 0);
        var stored = hasher.HashPlainText("correct horse");
        var authKey = Utility.Md5Hash("correct horse");

        Assert.True(hasher.VerifyAuthKey(authKey, stored));
        Assert.True(hasher.VerifyAuthKey(authKey, stored));

        Assert.Equal(2, kdf.VerifyCount);
    }

    [Fact]
    public void WrongAuthKey_IsStillRefusedAfterTheRightOneHasBeenVerified()
    {
        var hasher = TestPasswords.CreateHasher();
        var stored = hasher.HashPlainText("correct horse");

        Assert.True(hasher.VerifyAuthKey(Utility.Md5Hash("correct horse"), stored));

        Assert.False(hasher.VerifyAuthKey(Utility.Md5Hash("battery staple"), stored));
    }

    [Fact]
    public void AfterAPasswordChange_TheOldAuthKeyIsRefusedAndTheKdfRunsAgain()
    {
        var kdf = new CountingPasswordKdf();
        var hasher = TestPasswords.CreateHasher(kdf: kdf);
        var oldStored = hasher.HashPlainText("correct horse");
        var oldAuthKey = Utility.Md5Hash("correct horse");

        Assert.True(hasher.VerifyAuthKey(oldAuthKey, oldStored));

        var newStored = hasher.HashPlainText("battery staple");

        Assert.False(hasher.VerifyAuthKey(oldAuthKey, newStored));
        Assert.True(hasher.VerifyAuthKey(Utility.Md5Hash("battery staple"), newStored));
        Assert.Equal(3, kdf.VerifyCount);
    }

    // The cost is part of the hash itself, so the library can be asked what
    // work factor a stored credential was produced at.
    private static int WorkFactorOf(string storedHash)
    {
        return int.Parse(BCrypt.Net.BCrypt.InterrogateHash(storedHash).WorkFactor);
    }
}
