namespace Kosync.Database.Entities;

// Whether a User's stored credential is one they chose themselves, or one
// somebody else chose for them - a Provisional Password (GLOSSARY).
//
// Deliberately not a bool. ADR 0006 anticipates password expiry, which has to
// be able to re-raise "must change password now" long after a User replaced
// their initial password - something a one-shot "has changed it once" flag
// could never express. Expiry arrives here as a third state, and
// User.MustChangePassword is where it gets to mean something.
public enum PasswordState
{
    // Chosen by somebody else: the installer's ADMIN_PASSWORD, or whatever an
    // Administrator handed over when creating the account. Zero, so it is
    // also what a caller that forgot to say ends up with - the safe way
    // round, because this is the state that grants nothing.
    Provisional = 0,

    // Chosen by the User whose account it is.
    UserChosen = 1,
}
