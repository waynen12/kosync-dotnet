namespace Kosync.Models;

// Bound from the "Authentication" configuration section, so the portal's
// session cookie ships HTTPS-only (Authentication__CookieSecurePolicy in a
// container) rather than defaulting to the permissive setting a deployment
// could forget to tighten (issue #51). See
// Constants.Authentication.DefaultCookieSecurePolicy for why the default is
// Always rather than SameAsRequest.
//
// Bound as a string and parsed below, like StorageOptions.MaxBackupsRetained
// and SyncTraceOptions.MaxBodyCharacters: binding straight to the enum throws
// on a mistyped value, and a typo in a security setting must not be a worse
// outage than the plain-HTTP cookie it exists to prevent.
//
// Deliberately *not* also written into the shipped appsettings.json, for the
// same reason as PortalOptions and StorageOptions: a value there would win
// over this initializer in production while the tests, whose content root
// has no appsettings.json, went on exercising the constant.
public class AuthenticationOptions
{
    public string? CookieSecurePolicy { get; set; }

    // Anything that doesn't parse - unset, or a typo - falls back to the
    // shipped default (Always) rather than throwing, so a misconfigured
    // value fails safe instead of failing to start.
    public CookieSecurePolicy ResolvedCookieSecurePolicy =>
        Enum.TryParse<CookieSecurePolicy>(CookieSecurePolicy?.Trim(), ignoreCase: true, out var configured)
            ? configured
            : Constants.Authentication.DefaultCookieSecurePolicy;
}
