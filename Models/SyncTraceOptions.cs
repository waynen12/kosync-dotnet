namespace Kosync.Models;

// Bound from the "SyncTrace" configuration section, so an operator can watch
// a misbehaving device's exchange with `SyncTrace__Enabled=true` in the
// container and turn it off again without a rebuild - see
// Constants.SyncTrace.
//
// Both settings bind as strings and are parsed here rather than bound
// straight to a `bool` and an `int`. Configuration binding *throws* on a
// value it cannot convert - `Enabled=yes` or `MaxBodyCharacters=lots` would
// fault every request from the first one onwards - and a mistyped
// diagnostic setting must not be a worse outage than the bug it was turned
// on to find. (The same trap is live in PasswordPolicyOptions, whose comment
// says an unparseable value "binds to 0"; it does not, it throws.)
//
// Deliberately *not* also written into the shipped appsettings.json, for the
// same reason PortalOptions is not: a value there would win over these
// defaults in production while the tests, whose content root has no
// appsettings.json, went on exercising the constants.
public class SyncTraceOptions
{
    public string? Enabled { get; set; }

    public string? MaxBodyCharacters { get; set; }

    // Off unless the value says exactly the one thing that means on, so
    // every typo - and leaving it unset - leaves it off.
    public bool IsEnabled =>
        string.Equals(
            Enabled?.Trim(),
            Constants.SyncTrace.EnabledValue,
            StringComparison.OrdinalIgnoreCase);

    // What the middleware truncates at. A value that is not a number at all
    // falls back to the shipped default; a negative one means the same as
    // zero - trace the exchange, none of its bodies.
    public int BodyCharacterLimit
    {
        get
        {
            if (!int.TryParse(MaxBodyCharacters?.Trim(), out int configured))
            {
                return Constants.SyncTrace.DefaultMaxBodyCharacters;
            }

            return Math.Max(Constants.SyncTrace.MinimumMaxBodyCharacters, configured);
        }
    }
}
