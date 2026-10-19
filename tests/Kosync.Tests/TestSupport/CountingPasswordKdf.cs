using Kosync.Services;

namespace Kosync.Tests.TestSupport;

// The real KDF, with a tally. Verifying a credential is deliberately slow, so
// "this request did not run the KDF" is a behaviour worth asserting rather
// than timing (ADR 0005).
public class CountingPasswordKdf : IPasswordKdf
{
    private readonly IPasswordKdf _inner = new BCryptPasswordKdf();

    public int VerifyCount { get; private set; }

    public string Hash(string value, int workFactor) => _inner.Hash(value, workFactor);

    public bool Verify(string value, string hash)
    {
        VerifyCount++;
        return _inner.Verify(value, hash);
    }

    public bool IsHash(string value) => _inner.IsHash(value);
}
