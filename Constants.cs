namespace Kosync;

public static class Constants
{
    // The two credential headers every KOReader sync request carries. Their
    // names are fixed by the client, not by us.
    public static class AuthHeaders
    {
        public const string Username = "x-auth-user";

        public const string AuthKey = "x-auth-key";
    }

    // Password storage settings (ADR 0005). Defaults only - the live values
    // come from the "Passwords" configuration section via
    // PasswordHashingOptions.
    public static class Passwords
    {
        public const string ConfigurationSection = "Passwords";

        // The standard bcrypt work factor. Verification happens on every
        // authenticated sync request, which is what the verified-credential
        // cache is for; lowering this instead was rejected (ADR 0005).
        public const int DefaultWorkFactor = 12;

        // bcrypt's own bounds. A configured value outside them would make the
        // KDF throw on the first login, so it is clamped instead.
        public const int MinimumWorkFactor = 4;

        public const int MaximumWorkFactor = 31;

        // How long a verified credential stays trusted without re-running the
        // KDF. Long enough to cover a reading session's page-turn batches,
        // short enough that it is not a second credential store.
        public const int DefaultVerifiedCredentialCacheSeconds = 300;
    }

    // Multi-tenant scoping (ADR 0004). See Database/TenantContext.cs for the
    // mechanism these belong to.
    public static class Tenancy
    {
        // What the global query filters compare a row's UserId against when no
        // User has been set. Identity columns start at 1, so no Book, Device,
        // Document or SyncEvent can ever carry this - which is exactly the
        // point: an unscoped read matches nothing instead of everything.
        public const int NoTenantUserId = -1;

        public const string StartupBypassReason =
            "Startup migrates the schema and seeds the first admin before any User has logged in.";

        public const string DesignTimeBypassReason =
            "`dotnet ef` builds the model at design time with no request, no session and no User.";

        public const string TestFixtureBypassReason =
            "A test is asserting on what is stored rather than on what a User can see.";

        public const string SetUserAfterBypassErrorFormat =
            "The tenant filters have already been bypassed for a system operation ({0}); this scope cannot also act on behalf of a User.";

        public const string BypassAfterSetUserError =
            "This scope is already acting on behalf of a User; the tenant filters cannot be bypassed inside it.";

        public const string TenantReassignedErrorFormat =
            "This scope is already acting on behalf of User {0} and cannot be reassigned to User {1}.";

        public const string LiteDbImportRequiresBypassError =
            "Importing the legacy LiteDB store writes rows for every User at once and must run with the tenant filters bypassed.";

        // Named onto the migration's staging CHECK constraint, because SQLite
        // reports a failed named CHECK by name and that is the only way raw
        // migration SQL gets to explain itself. Safe to reword: the staging
        // table lives and dies inside the migration, so the name never
        // survives into an applied database.
        public const string BookOwnershipConflictMessage =
            "a Book whose Documents span multiple Users cannot be given an owner automatically - resolve it by hand, then migrate";
    }

    public const string DevicesTab = "devices";

    public const string BooksTab = "books";

    public const string NoDevicesSyncedText = "No devices have synced yet.";

    public const string NoBooksSyncedText = "No books have synced yet.";

    // A Device's card and detail view list that Device's Documents, so the
    // empty state has to talk about Documents - not Books.
    public const string NoDeviceDocumentsSyncedText = "No documents synced yet";

    public const string NoBookProgressText = "No progress yet";

    public const string ProgressRegressionFlag = "Progress Regression";

    public const string SplitBookFlag = "Split Book";

    public const string ProgressResetFlag = "Progress Reset";

    public const string ProgressResetLabel = "Progress reset";

    public const string ResetProgressButton = "Reset progress";

    public const string ResetProgressFailedError = "Reset failed - document not found.";

    public const string MergeBookIdRequiredError = "Enter a Book # to merge.";

    public const string MergeSameBookError = "Can't merge a Book into itself.";

    public const string MergeBookNotFoundErrorFormat = "Book #{0} was not found.";

    public const string MergeFailedError = "Merge failed.";

    // The product name, rendered as the login card's title and (issue #17)
    // the app shell's brand.
    public const string BrandName = "Kosync";

    // One voice across the screen: the card, the submit button and the
    // browser tab all say "sign in", not a mix of "log in" and "sign in".
    public const string LoginPageTitle = "Sign in";

    public const string LoginHeading = "Sign in to continue";

    public const string LoginUsernameLabel = "Username";

    public const string LoginPasswordLabel = "Password";

    public const string LoginUsernamePlaceholder = "your username";

    public const string LoginPasswordPlaceholder = "••••••••";

    public const string LoginSubmitText = "Sign in";

    // Portal password and sync password are the same stored credential
    // (ADR 0005), which is not obvious from a login form - so the form says
    // so rather than letting a reader guess at a separate portal account.
    public const string LoginSyncHint =
        "Use the same username and password your KOReader devices sync with.";

    // Deliberately says neither which of the two was wrong nor whether the
    // User exists, is inactive or isn't an Administrator.
    public const string LoginErrorText = "Invalid username or password";

    // Icon vocabulary for Components/Shared/HubIcon; HubIconPaths holds the
    // geometry. HubIcon.Name is a plain string, so these do not make a typo a
    // build error - what they do is give call sites one spelling to share and
    // keep the set of available icons discoverable from one place.
    public static class Icons
    {
        // For an icon sitting on a line of body text rather than standing on
        // its own - HubIconPaths.DefaultSize is the standalone size.
        public const int InlineSize = 15;

        public const string Alert = "alert";

        public const string BookOpen = "book-open";

        public const string ChevronRight = "chevron-right";

        public const string Close = "x";

        public const string Device = "device";

        public const string KeyRound = "key-round";

        public const string Library = "library";

        public const string LogOut = "log-out";

        public const string Merge = "merge";

        public const string Moon = "moon";

        public const string Plus = "plus";

        public const string Rotate = "rotate";

        public const string Search = "search";

        public const string Shield = "shield";

        public const string Sun = "sun";

        public const string Trash = "trash";

        public const string User = "user";

        public const string UserCheck = "user-check";

        public const string UserPlus = "user-plus";

        public const string Users = "users";

        public const string UserX = "user-x";
    }
}
