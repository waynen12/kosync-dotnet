namespace Kosync.Services;

// The key derivation function underneath PasswordHasher. It exists as its
// own seam because a deliberately slow KDF is the one collaborator tests
// cannot afford to exercise honestly on every path - notably the test that
// the verified-credential cache really does skip it.
public interface IPasswordKdf
{
    string Hash(string value, int workFactor);

    bool Verify(string value, string hash);

    // True when the value is one of this KDF's own hashes, rather than a
    // credential stored by an older scheme that still needs upgrading.
    bool IsHash(string value);
}
