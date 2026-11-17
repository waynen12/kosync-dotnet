namespace Kosync.Models;

// What a User may choose as their own password. Bound from the same
// "Passwords" configuration section as PasswordHashingOptions, but kept a
// separate class because the two answer different questions: that one is how
// a credential is stored (a KDF decision), this one is what the portal will
// accept from the person typing it (a policy decision). A deployment tunes
// them independently - Passwords__MinimumLength next to
// Passwords__WorkFactor.
//
// Deliberately absent from the shipped appsettings.json, unlike the two
// hashing values beside it in that section. A value there would win over this
// initializer in production while the tests, whose content root has no
// appsettings.json, went on exercising the constant - so the rule a User
// meets could change with the suite still green. Same reasoning as
// PortalOptions: one default, in one place.
public class PasswordPolicyOptions
{
    public int MinimumLength { get; set; } = Constants.Passwords.DefaultMinimumPasswordLength;
}
