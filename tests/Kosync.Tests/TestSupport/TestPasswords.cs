using Kosync.Models;
using Kosync.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Kosync.Tests.TestSupport;

// Builds PasswordHashers for unit tests. The work factor defaults to
// bcrypt's minimum: these tests are about the scheme, not about how slow the
// KDF is, and the real default of 12 would cost ~250ms per hash.
public static class TestPasswords
{
    // A hasher with nothing configured at all, so the shipped defaults are
    // what reaches the KDF.
    public static PasswordHasher CreateHasherWithDefaults()
    {
        return new PasswordHasher(
            new BCryptPasswordKdf(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new PasswordHashingOptions()));
    }

    public static PasswordHasher CreateHasher(
        int workFactor = Constants.Passwords.MinimumWorkFactor,
        IPasswordKdf? kdf = null,
        int verifiedCredentialCacheSeconds = Constants.Passwords.DefaultVerifiedCredentialCacheSeconds)
    {
        var options = Options.Create(new PasswordHashingOptions
        {
            WorkFactor = workFactor,
            VerifiedCredentialCacheSeconds = verifiedCredentialCacheSeconds,
        });

        return new PasswordHasher(
            kdf ?? new BCryptPasswordKdf(),
            new MemoryCache(new MemoryCacheOptions()),
            options);
    }
}
