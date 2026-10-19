using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Kosync.Services;

// Hashes and verifies stored credentials. Everything written to
// User.PasswordHash goes through here.
//
// Stored credentials are bcrypt over the MD5 that KOReader sends as
// `x-auth-key` (ADR 0005): the MD5 cannot be dropped without breaking the
// wire protocol, so it becomes the inner step instead. Sync clients have
// already done the MD5 and hand it to VerifyAuthKey; the portal has
// plaintext and hands it to VerifyPlainText. Both end up verifying the same
// value against the same column.
//
// Verification is no longer a string comparison but a deliberately slow KDF,
// and it happens on every authenticated sync request - so a credential that
// has already been verified is remembered for a short while and the KDF
// skipped. First contact and password changes still pay for it in full.
public class PasswordHasher
{
    private readonly IPasswordKdf _kdf;
    private readonly IMemoryCache _verifiedCredentials;
    private readonly PasswordHashingOptions _options;

    public PasswordHasher(IPasswordKdf kdf, IMemoryCache verifiedCredentials, IOptions<PasswordHashingOptions> options)
    {
        _kdf = kdf;
        _verifiedCredentials = verifiedCredentials;
        _options = options.Value;
    }

    // A configured value bcrypt would reject falls back to the default rather
    // than to the nearest bound. Clamping would send the 0 that an
    // unparseable setting binds to all the way down to bcrypt's weakest cost,
    // which is the one outcome a misconfiguration must not produce quietly.
    private int WorkFactor
    {
        get
        {
            var configured = _options.WorkFactor;

            var usable = configured >= Constants.Passwords.MinimumWorkFactor
                && configured <= Constants.Passwords.MaximumWorkFactor;

            return usable ? configured : Constants.Passwords.DefaultWorkFactor;
        }
    }

    // For a password typed into the portal or an admin form.
    public string HashPlainText(string password)
    {
        return HashAuthKey(Utility.Md5Hash(password));
    }

    // For a credential that arrives already MD5'd - a KOReader
    // `POST /users/create`, or a legacy row whose stored value is a bare MD5.
    public string HashAuthKey(string authKey)
    {
        return _kdf.Hash(authKey, WorkFactor);
    }

    public bool VerifyPlainText(string password, string storedHash)
    {
        return VerifyAuthKey(Utility.Md5Hash(password), storedHash);
    }

    public bool VerifyAuthKey(string authKey, string storedHash)
    {
        if (string.IsNullOrEmpty(authKey) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        // A non-positive lifetime turns the cache off and makes every request
        // pay the KDF - a legitimate setting for a deployment that would
        // rather not hold verified credentials in memory at all.
        var lifetime = TimeSpan.FromSeconds(_options.VerifiedCredentialCacheSeconds);

        if (lifetime <= TimeSpan.Zero)
        {
            return _kdf.Verify(authKey, storedHash);
        }

        var cacheKey = VerifiedCredentialKey(authKey, storedHash);

        if (_verifiedCredentials.TryGetValue(cacheKey, out _))
        {
            return true;
        }

        if (!_kdf.Verify(authKey, storedHash))
        {
            return false;
        }

        // Only successes are remembered, and only for as long as the
        // configured window. A changed password produces a different stored
        // hash and therefore a different key, so the old entry can never
        // authorise the new credential - or the new one the old credential.
        _verifiedCredentials.Set(cacheKey, true, lifetime);

        return true;
    }

    // A credential that predates ADR 0005 is a bare MD5 and needs wrapping;
    // one that is already a KDF hash must be left alone.
    public bool NeedsUpgrade(string storedHash)
    {
        return !string.IsNullOrEmpty(storedHash) && !_kdf.IsHash(storedHash);
    }

    // Keyed on the pair, so a cache hit means "this exact auth key was
    // verified against this exact stored credential". The auth key is a
    // password-equivalent, so it is reduced to a digest rather than held in
    // memory as-is.
    private static string VerifiedCredentialKey(string authKey, string storedHash)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{storedHash}:{authKey}"));

        return $"{nameof(PasswordHasher)}:{Convert.ToHexString(digest)}";
    }
}
