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

        // The shortest password a User may choose for themselves, when the
        // deployment has not said otherwise (PasswordPolicyOptions). It binds
        // only where a User picks their own - it is deliberately not applied
        // to the Provisional Passwords an Administrator hands over, which the
        // forced change replaces anyway (ADR 0006).
        public const int DefaultMinimumPasswordLength = 8;
    }

    // Where this server's portal can be reached from outside (ADR 0006).
    // Defaults only - the live value comes from the "Portal" configuration
    // section via PortalOptions.
    public static class Portal
    {
        public const string ConfigurationSection = "Portal";

        // The server cannot work out its own public address: it sits behind
        // whatever reverse proxy and domain the operator put in front of it,
        // and the Host header a KOReader device sends is whatever that device
        // was pointed at. So this is a deployment setting, and the default is
        // only right for the one case the server can assume - somebody
        // running it on the machine they are reading this from, on the port
        // the container publishes.
        public const string DefaultBaseUrl = "http://localhost:8080";

        // Named for the person holding the device, who sees it as a sync
        // error and whose instinct is to re-type the password. A bare refusal
        // is indistinguishable from a wrong password to stock KOReader, and
        // KOReader surfaces the server's message - so this is the only
        // channel that reaches them (ADR 0006).
        public const string PasswordChangeRequiredFormat =
            "Password change required — log in at {0} to set a new password.";
    }

    // PasswordChangeGateMiddleware, which holds a Provisional-Password User
    // on the change-password page (ADR 0006). Routing rather than copy,
    // which is why it is not in ChangePassword below.
    public static class PortalGate
    {
        // Request paths the gate never redirects, beyond the change-password
        // page, logout and /login themselves (AppRoutes). These are the
        // framework's own endpoints: Blazor's script and its circuit
        // negotiation, and any Razor component library's static assets.
        // Redirecting those answers a script request with an HTML page,
        // which breaks the very page the gate is sending the User to.
        //
        // Matched as path prefixes. wwwroot's own files never reach the gate -
        // UseStaticFiles short-circuits them before authentication runs.
        public static readonly string[] ExemptPathPrefixes =
        [
            "/_blazor",
            "/_framework",
            "/_content",
        ];
    }

    // The portal's forced first password change (ADR 0006, issue #24).
    public static class ChangePassword
    {
        public const string PageTitle = "Change password";

        // Under the title, in the card's .hub-login-sub - the same slot
        // LoginHeading fills on /login.
        public const string Subheading = "Choose a new password";

        // Shown only to a User the gate sent here, so it is written as the
        // answer to "why am I on this screen" rather than as a page
        // subtitle.
        public const string RequiredNotice =
            "This password was set for you, so it does nothing until you replace it. Choose your own to use the portal and sync your devices.";

        // The non-optional half of ADR 0006: portal password and sync
        // password are the same stored credential, so completing this change
        // invalidates the old one everywhere it is held - and by
        // construction that happens to every User exactly once, right here.
        // The ADR accepted that cost in exchange for one credential doing
        // both jobs, and named this warning as the mitigation.
        public const string DeviceInvalidationWarning =
            "Changing your password signs out every KOReader device. Each one keeps syncing with the old password until you open its sync settings and enter the new one.";

        public const string CurrentPasswordLabel = "Current password";

        public const string NewPasswordLabel = "New password";

        public const string ConfirmPasswordLabel = "Confirm new password";

        public const string SubmitText = "Change password";

        // Says only "that wasn't it", the way the login error does: this page
        // is reached with a session, but a session is not proof that the
        // person at the keyboard knows the password.
        public const string CurrentPasswordWrongError = "That is not your current password.";

        public const string ConfirmationMismatchError = "The two new passwords do not match.";

        // Replacing a Provisional Password with itself would flip the state
        // to UserChosen while leaving the credential somebody else chose in
        // place - which is the whole thing ADR 0006 is preventing.
        public const string UnchangedPasswordError = "Choose a password different from your current one.";

        // Reads the configured minimum rather than a literal, so the message
        // cannot disagree with the rule - see PasswordPolicy.
        public const string TooShortErrorFormat = "Use at least {0} characters.";

        public const string EmptyPasswordError = "Enter a new password.";

        // The way out that isn't completing the change, for a User who
        // reached this screen on somebody else's account or simply wants to
        // leave. The gate lets it through for exactly that reason.
        public const string SignOutLinkText = "Sign out instead";
    }

    // The portal's sign-out screen (AppRoutes.Logout).
    public static class Logout
    {
        public const string PageTitle = "Sign out";

        public const string Subheading = "Sign out of the portal?";

        // What signing out of the portal does and does not touch. A User
        // whose devices have just stopped syncing is the one most likely to
        // be here, so the screen says plainly that this is not that.
        public const string Explanation =
            "This ends your portal session on this browser. Your KOReader devices are not affected.";

        public const string SubmitText = "Sign out";

        public const string CancelText = "Stay signed in";
    }

    // The settings the app reads straight from the process environment rather
    // than through configuration. Named here so that the tests exercising
    // them share one spelling with the code that reads them.
    public static class EnvironmentVariables
    {
        public const string AdminPassword = "ADMIN_PASSWORD";

        // One-shot recovery for a genuinely lost admin password (ADR 0006).
        // Honoured once per distinct value, because the variable itself stays
        // set in a compose file across every restart - so "has this one been
        // applied already" is a fact about the server, recorded in
        // ServerSettings, not something the variable's presence can answer.
        public const string AdminPasswordReset = "ADMIN_PASSWORD_RESET";

        public const string RegistrationDisabled = "REGISTRATION_DISABLED";
    }

    // The first account, created on a fresh install (ADR 0006).
    public static class Admin
    {
        public const string Username = "admin";

        // Where the admin's password comes from when ADMIN_PASSWORD is not
        // given, and what ADMIN_PASSWORD_RESET falls back to. Provisional
        // either way, so it grants nothing until the User replaces it.
        public const string DefaultPassword = "admin";
    }

    // Keys into the ServerSetting table. These are not configuration: they
    // record what this server has already done, which is why they outlive a
    // restart and an env var cannot replace them.
    public static class ServerSettings
    {
        public const string AppliedAdminPasswordReset = "AdminPasswordReset.Applied";
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

    // The dashboard's two sections. These are URL values, not labels: the
    // section is chosen by navigating to ?tab=, so they are what AppRoutes
    // writes into a link and what the page reads back out of the query.
    public const string DevicesTab = "devices";

    public const string BooksTab = "books";

    // The hub dashboard - a sidebar beside a dense table of Devices or Books
    // (issue #18, docs/ROADMAP.md "Screen layouts"). Moved here from
    // PrototypeConstants.cs when variant B won, rather than copied: the
    // prototype folder goes with issue #19.
    public static class Dashboard
    {
        public const string PageTitle = "Dashboard";

        public const string NavGroupLibrary = "Library";

        // The sidebar's two section labels. Separate from DevicesTab/BooksTab
        // above, which are the URL spelling of the same choice.
        public const string DevicesLabel = "Devices";

        public const string BooksLabel = "Books";

        public const string ColumnName = "Name";

        public const string ColumnDocuments = "Documents";

        public const string ColumnProgress = "Progress";

        public const string ColumnLastSynced = "Last synced";

        public const string ColumnFlags = "Flags";

        // The actions column's heading is deliberately blank on screen - the
        // column holds one button per row and a label over it is noise - so
        // this is read out rather than shown (.hub-sr-only).
        public const string ColumnActions = "Actions";

        // Every row's one action, and the reason the whole row need not be a
        // click target - see the no-row-onclick comment in Dashboard.razor.
        public const string OpenAction = "Open";

        // A row with nothing to flag still gets a cell, so the Flags column
        // stays a column you can scan down rather than a ragged one.
        public const string NoFlagsText = "—";

        public const string SearchPlaceholder = "Search by device, book or hash...";

        public const string NoMatchesText = "Nothing matches that filter.";

        // What the filter input points at and the table answers to. An id
        // rather than a walk up the DOM, so the script stays four lines of
        // "find the scope, hide what doesn't match".
        public const string TableId = "dashboard-table";

        // The filter's implementation, named here because both App.razor's
        // <script> and the test asserting it never reaches the server have to
        // mean the same file.
        public const string FilterScript = "dashboard-filter.js";

        // What the toolbar counts. Singular, because
        // DashboardFormatting.Pluralize is what turns a count into a phrase.
        public const string DeviceNoun = "device";

        public const string BookNoun = "book";

        // The flagged half of that count, which counts *rows* - and a row is
        // a Book in one section and a Device in the other. A Device is not a
        // Split Book (GLOSSARY.md): it is a Device holding a Document that
        // belongs to one. So the two sections cannot share a phrase, and the
        // Devices wording would be a lie if they did.
        public const string FlaggedBooksNoun = "split book";

        public const string FlaggedDevicesFormat = "{0} with a split Book";

        // A Book has no title of its own until Phase 3 (ADR 0007) observes or
        // curates one, so the dashboard names it by its id.
        public const string BookNameFormat = "Book #{0}";
    }

    public const string NoDevicesSyncedText = "No devices have synced yet.";

    public const string NoBooksSyncedText = "No books have synced yet.";

    // A Device's detail view lists that Device's Documents, so its empty
    // state has to talk about Documents - not Books (issue #13). The
    // dashboard used to need this too, when a Device's card listed its
    // Documents; the table it became shows a count instead, where an empty
    // Device reads as a plain 0 and cannot name the wrong thing.
    public const string NoDeviceDocumentsSyncedText = "No documents synced yet";

    public const string NoBookProgressText = "No progress yet";

    // What the two drill-ins render for an id they cannot find. Also what
    // they render for an id belonging to somebody else, which is the same
    // path on purpose (ADR 0004): a tenant-filtered lookup returns null, so
    // "not yours" and "not there" are indistinguishable from outside - and
    // must stay so, or the page becomes an oracle for which ids another User
    // owns.
    public const string DeviceNotFoundText = "Device not found.";

    public const string BookNotFoundText = "Book not found.";

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

    // The product name, rendered as the login card's title and the app
    // shell's brand.
    public const string BrandName = "Kosync";

    // The one-line pitch under a page heading. Brand copy rather than
    // dashboard copy, which is why it sits here and not in Dashboard below.
    public const string BrandTagline = "Your reading, across every device.";

    // The app shell's theme toggle carries no visible text - it is an icon
    // button - so this is its accessible name rather than a label.
    public const string ThemeToggleLabel = "Toggle theme";

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
    // User exists at all or is merely inactive - an inactive account being
    // the only thing besides the credential that refuses a session, since
    // issue #26 dropped the Administrator requirement.
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

        // For an icon that is the whole control, inside a .hub-icon-btn.
        public const int ButtonSize = 18;

        // For an icon standing beside a nav label in the dashboard sidebar -
        // a size up from inline, a size down from a control.
        public const int SidebarSize = 17;

        // For an icon inside a .hub-badge or a .hub-btn-sm, where the text it
        // sits against is 11.5-12px.
        public const int BadgeSize = 12;

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
