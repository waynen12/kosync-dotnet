using Microsoft.Extensions.Options;

namespace Kosync.Services;

// Whether a password a User picked for themselves is one the portal will
// accept, and the sentence to show them when it is not.
//
// Its own class rather than a few `if`s on the change-password page, because
// two more callers are coming: the Users administration page (issue #28) and
// PUT /manage/users/password, which today enforces only "not blank". A
// policy they can all share is the only way the rule stays one rule.
public class PasswordPolicy
{
    private readonly PasswordPolicyOptions _options;

    public PasswordPolicy(IOptions<PasswordPolicyOptions> options)
    {
        _options = options.Value;
    }

    // A configured value below 1 falls back to the default rather than being
    // honoured, for the reason PasswordHasher.WorkFactor does the same: an
    // unparseable setting binds to 0, and "the operator fat-fingered the
    // config" must not quietly turn the rule off. A deployment that really
    // wants no minimum sets 1.
    public int MinimumLength =>
        _options.MinimumLength >= 1
            ? _options.MinimumLength
            : Constants.Passwords.DefaultMinimumPasswordLength;

    // Null when the password is acceptable; otherwise the message to show.
    // Returning the sentence rather than a bool keeps "which rule failed"
    // and "what to say about it" in one place - the caller is a form that
    // has to render one or the other anyway.
    //
    // `currentPassword` is the one being replaced, where the caller knows it.
    // An Administrator setting somebody else's password does not, and passes
    // nothing: the reuse rule is then simply not in play, rather than being
    // checked against a password the caller had to go and fetch.
    public string? Validate(string? password, string? currentPassword = null)
    {
        // Whitespace-only is treated as empty, not as a short password:
        // KOReader will not even attempt to log in with one, so a credential
        // like that cannot be used for the thing it is for.
        if (string.IsNullOrWhiteSpace(password))
        {
            return Constants.ChangePassword.EmptyPasswordError;
        }

        // Ahead of the length rule, not after it: a Provisional Password
        // that fails the minimum - `admin` does - would otherwise be
        // answered with "use at least N characters", which invites the User
        // to pad the password somebody else chose rather than replace it.
        // Re-typing it has to be refused as what it is, because recording it
        // as the User's own choice while leaving that credential in place is
        // exactly what ADR 0006 exists to prevent.
        if (currentPassword is not null && password == currentPassword)
        {
            return Constants.ChangePassword.UnchangedPasswordError;
        }

        if (password.Length < MinimumLength)
        {
            return string.Format(Constants.ChangePassword.TooShortErrorFormat, MinimumLength);
        }

        return null;
    }
}
