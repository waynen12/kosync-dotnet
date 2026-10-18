namespace Kosync.Components.Prototype;

// THROWAWAY PROTOTYPE - not production code.
//
// Answers: "what should the login screen, user management screen, and overall
// look and feel be, and does Blazor InteractiveServer (ADR 0003) survive?"
//
// The look-and-feel question is constrained: it must match
// /home/wayne/repos/AIResources/ai-hub (Next.js 16 + Tailwind v4 + shadcn +
// Geist + the orange->indigo gradient). So all three variants share that
// design language and differ structurally instead - specifically in how far
// ai-hub's shapes stretch over kosync's extra per-account data, and in how
// much of the interaction goes back to the server.
//
//   A  Hub parity        - closest port of ai-hub. Gradient hero, amber tab
//                          bar, 3-col card grid, accounts in a MODAL exactly
//                          like ai-hub's AccountsModal. Tabs are @onclick
//                          (a SignalR round-trip) - the control condition.
//   B  Admin console     - same tokens, but a sidebar shell and a dense
//                          accounts TABLE, because kosync tracks more per
//                          account than ai-hub's three fields. Nav is real
//                          <a href> navigation.
//   C  Zero round-trips  - same tokens, single stream + client-side chip
//                          filters and inline-expand rows. Nothing touches
//                          SignalR. The ADR 0003 experiment.
//
// Per CLAUDE.md's no-hardcoded-values standard these strings live here rather
// than inline in the variants - but when a variant wins, its surviving strings
// move to the real Constants.cs, not this file. Delete this whole folder then.
public static class PrototypeConstants
{
    public const string VariantQueryParam = "variant";

    public const string VariantA = "A";
    public const string VariantB = "B";
    public const string VariantC = "C";

    public static readonly string[] Variants = [VariantA, VariantB, VariantC];

    public static string Normalize(string? variant) =>
        Variants.Contains(variant, StringComparer.OrdinalIgnoreCase)
            ? variant!.ToUpperInvariant()
            : VariantA;

    // Shown in the floating switcher so it is obvious which design is on screen.
    public static readonly Dictionary<string, string> VariantNames = new()
    {
        [VariantA] = "Hub parity",
        [VariantB] = "Admin console",
        [VariantC] = "Zero round-trips",
    };

    public static readonly Dictionary<string, string> VariantInteractionModel = new()
    {
        [VariantA] = "server round-trip",
        [VariantB] = "real navigation",
        [VariantC] = "client-only",
    };

    // ---------------------------------------------------------------------
    // Shell / brand
    // ---------------------------------------------------------------------
    public const string BrandName = "Kosync";
    public const string BrandTagline = "Your reading, across every device.";
    public const string ThemeToggleLabel = "Toggle theme";
    public const string SignOutLabel = "Sign out";
    public const string ChangePasswordLabel = "Change password";
    public const string ManageAccountsLabel = "Manage accounts";

    // ---------------------------------------------------------------------
    // Login
    // ---------------------------------------------------------------------
    public const string LoginHeading = "Sign in to continue";
    public const string LoginUsernameLabel = "Username";
    public const string LoginPasswordLabel = "Password";
    public const string LoginUsernamePlaceholder = "your username";
    public const string LoginPasswordPlaceholder = "••••••••";
    public const string LoginSubmitText = "Sign in";
    // The error text is NOT here. It survives whichever variant wins (all
    // three render it), and the fold-in (#19) can't delete this file while
    // the test suite references the string from it - so #13 moved it ahead
    // of the rest to Constants.LoginErrorText.
    public const string LoginSyncHint = "Use the same username and password your KOReader devices sync with.";

    public static readonly string[] LoginFeatureBullets =
    [
        "Every device that has synced, and when",
        "Progress per book, furthest-along wins",
        "Sync issues flagged where they happened",
    ];

    // ---------------------------------------------------------------------
    // Dashboard
    // ---------------------------------------------------------------------
    public const string DashboardSearchPlaceholder = "Search by device, book or hash...";
    public const string FilterAllLabel = "Everything";
    public const string FilterDevicesLabel = "Devices";
    public const string FilterBooksLabel = "Books";
    public const string FilterFlaggedLabel = "Flagged";
    public const string NoMatchesText = "Nothing matches that filter.";
    public const string DevicesGroupLabel = "Devices";
    public const string BooksGroupLabel = "Books";

    public const string NavDevicesLabel = "Devices";
    public const string NavBooksLabel = "Books";
    public const string NavUsersLabel = "Accounts";
    public const string NavAccountLabel = "My account";
    public const string NavLibraryGroupLabel = "Library";
    public const string NavAdminGroupLabel = "Administration";

    public const string StatDevicesLabel = "Devices";
    public const string StatBooksLabel = "Books";
    public const string StatFlaggedLabel = "Flagged";

    public const string ColumnName = "Name";
    public const string ColumnDocuments = "Documents";
    public const string ColumnProgress = "Progress";
    public const string ColumnLastSynced = "Last synced";
    public const string ColumnFlags = "Flags";
    public const string DocumentsSuffix = "documents";
    public const string ProgressSuffix = "progress";
    public const string NeverSyncedText = "never";

    // ---------------------------------------------------------------------
    // User management
    // ---------------------------------------------------------------------
    public const string UsersTitle = "Manage Accounts";
    public const string UsersSubtitle = "Accounts only - an Administrator never reads another User's library.";
    public const string UsersSearchPlaceholder = "Search accounts...";
    public const string NewUserButtonText = "New Account";
    public const string CreateUserSubmitText = "Create Account";
    public const string CancelText = "Cancel";
    public const string CloseText = "Close";
    public const string SetText = "Set";
    public const string ActiveGroupFormat = "Active ({0})";
    public const string InactiveGroupFormat = "Inactive ({0})";
    public const string NoActiveAccountsText = "No active accounts.";
    public const string SelfSuffix = "(you)";

    public const string RoleAdministrator = "Administrator";
    public const string RoleReader = "Reader";
    public const string RoleLabel = "Role";
    public const string StatusActive = "Active";
    public const string StatusInactive = "Inactive";
    public const string PasswordProvisional = "Provisional";
    public const string PasswordSet = "Password set";
    public const string ProvisionalHint = "Inert until this User replaces the password - portal and sync both refuse it (ADR 0006).";

    public const string ResetPasswordAction = "Reset password";
    public const string DeactivateAction = "Deactivate";
    public const string ReactivateAction = "Reactivate";
    public const string DeleteAction = "Delete";
    public const string NewPasswordPlaceholder = "New password (min 8 chars)";
    public const string UsernamePlaceholder = "e.g. alice";
    public const string NewPasswordHint = "At least 8 characters";

    public const string SelfGuardReason = "You can't change your own role or delete yourself.";
    public const string LastAdminGuardReason = "The last Administrator can't be demoted or removed.";
    public const string CommandBarPlaceholder = "Filter, or run a command: new <name> / reset <name> / deactivate <name>";
    public const string CommandBarHint = "Prototype only - commands filter the list but nothing is saved.";

    public const string StubNoticeText =
        "Stub data. Phase 2.5 (tenancy, bcrypt, forced first-login change) is not built yet, so there is no service to call.";

    // Stub accounts for the Users screen.
    public static readonly PrototypeUser[] StubUsers =
    [
        new("admin", IsAdministrator: true, IsActive: true, IsProvisional: false, Devices: 2, Books: 31, LastSync: "4 minutes ago", Created: "14 Sep 2026", IsSelf: true),
        new("wayne", IsAdministrator: true, IsActive: true, IsProvisional: false, Devices: 3, Books: 118, LastSync: "an hour ago", Created: "14 Sep 2026", IsSelf: false),
        new("rachel", IsAdministrator: false, IsActive: true, IsProvisional: false, Devices: 1, Books: 47, LastSync: "yesterday", Created: "2 Oct 2026", IsSelf: false),
        new("theo", IsAdministrator: false, IsActive: true, IsProvisional: true, Devices: 0, Books: 0, LastSync: null, Created: "7 Oct 2026", IsSelf: false),
        new("kobo-spare", IsAdministrator: false, IsActive: false, IsProvisional: false, Devices: 1, Books: 9, LastSync: "3 months ago", Created: "3 Jul 2026", IsSelf: false),
    ];

    public static int AdministratorCount => StubUsers.Count(u => u.IsAdministrator && u.IsActive);
}

public record PrototypeUser(
    string Username,
    bool IsAdministrator,
    bool IsActive,
    bool IsProvisional,
    int Devices,
    int Books,
    string? LastSync,
    string Created,
    bool IsSelf)
{
    public string Role => IsAdministrator ? PrototypeConstants.RoleAdministrator : PrototypeConstants.RoleReader;

    // ADR 0006: nobody can demote or delete themselves, and the last
    // Administrator can't be removed. Surfaced as a disabled control with the
    // reason, rather than an error after the fact.
    public string? GuardReason =>
        IsSelf ? PrototypeConstants.SelfGuardReason
        : IsAdministrator && PrototypeConstants.AdministratorCount <= 1 ? PrototypeConstants.LastAdminGuardReason
        : null;

    public bool IsGuarded => GuardReason is not null;

    // Everything the client-side text filters match against.
    public string SearchText =>
        $"{Username} {Role} {(IsActive ? PrototypeConstants.StatusActive : PrototypeConstants.StatusInactive)} {(IsProvisional ? PrototypeConstants.PasswordProvisional : "")}";
}
