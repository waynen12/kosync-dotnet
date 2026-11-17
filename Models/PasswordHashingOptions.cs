namespace Kosync.Models;

// Bound from the "Passwords" configuration section, so both the work factor
// and the cache lifetime are deployment decisions rather than compiled-in
// ones (appsettings.json, or Passwords__WorkFactor in a container).
public class PasswordHashingOptions
{
    public int WorkFactor { get; set; } = Constants.Passwords.DefaultWorkFactor;

    public int VerifiedCredentialCacheSeconds { get; set; } = Constants.Passwords.DefaultVerifiedCredentialCacheSeconds;
}
