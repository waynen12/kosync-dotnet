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

    // The wire shape of `GET /syncs/progress/{hash}`, which is fixed by the
    // reference server (koreader-sync-server) rather than by us.
    public static class SyncProtocol
    {
        public const string ProgressContentType = "application/json";

        // What a Document with no current progress answers with, at 200.
        // The reference server's get_progress builds an empty table, fills
        // it only if progress exists, and returns 200 either way - it has no
        // document-not-found error at all, so "this reader has not synced
        // this book yet" is 200 {} there (issue #41). A 502 in its place is
        // a server-fault code for the first sync of every book, and a client
        // that pulls before it pushes reads one as fatal and never pushes.
        public const string EmptyProgressBody = "{}";
    }

    // How a sync log line is stamped, for the one formatter that writes them
    // (RequestLogFormatter).
    public static class Logging
    {
        public const string TimestampFormat = "yyyy/MM/dd HH:mm:ss";

        // Marks a request that did *not* arrive through one of the configured
        // TRUSTED_PROXIES, so the IP beside it is the connection's own rather
        // than a forwarded one. Only ever appears once proxies are set at
        // all - with none configured there is nothing to contrast against.
        public const string UntrustedSourceMarker = "*";
    }

    // The Sync Trace: what an operator turns on to watch one device's
    // requests and the answers it got. Defaults only - the live values come
    // from the "SyncTrace" configuration section via SyncTraceOptions.
    //
    // Exists because the per-endpoint logging can only report what a
    // controller action chose to say, so it is blind to every request that
    // never reaches one: a path we do not serve, a verb we do not accept, a
    // body model binding refused. Those answer 404/405/400 silently, and a
    // reader whose device gets one sees a sync that just stops (issue #41).
    public static class SyncTrace
    {
        public const string ConfigurationSection = "SyncTrace";

        // What SyncTrace__Enabled has to say to turn the trace on. Compared
        // case-insensitively, like SELF_REGISTRATION_ENABLED, and for a
        // sharper reason than tidiness: binding this section to a `bool`
        // throws on the first request if an operator typed `yes`, and an
        // InvalidOperationException per request is a worse outage than the
        // bug they turned the trace on to find. Anything but this value
        // leaves it off, which is the safe way round.
        public const string EnabledValue = "true";

        // Enough for any protocol body - a progress push with metadata is a
        // few hundred characters - without letting a surprise payload fill
        // the log. Also what an unparseable MaxBodyCharacters falls back to.
        public const int DefaultMaxBodyCharacters = 2048;

        // A configured value below this means "trace the exchange but none of
        // its bodies", which is a legitimate thing to want. Negatives are
        // clamped up to it rather than throwing on the first request.
        public const int MinimumMaxBodyCharacters = 0;

        // One entry as the request arrives, one as the answer leaves, both
        // tagged with the marker so an operator can grep the pair out of
        // everything else, and both carrying the request's trace identifier
        // so the pair stays matched when two devices sync at once. The
        // arrows read as the device's point of view: --> is what it sent.
        public const string Marker = "Sync trace";

        public const string RequestArrow = "-->";

        public const string ResponseArrow = "<--";

        public const string RequestFormat = Marker + " {0} " + RequestArrow + " {1} {2}";

        public const string ResponseFormat = Marker + " {0} " + ResponseArrow + " {1} in {2} ms";

        // When the pipeline threw before any answer was sent. Deliberately
        // not a status code: nothing had set one yet, so printing
        // Response.StatusCode here would report a 200 the device never got.
        public const string FaultFormat =
            Marker + " {0} " + ResponseArrow + " threw after {1} ms, before any answer was sent";

        // Written once at startup when the trace is on, so an operator who
        // sees no trace lines knows the difference between "the setting did
        // not take" and "the device never reached this server" - which is the
        // whole question they are usually trying to answer.
        public const string EnabledNotice =
            "Sync Trace is ON: every sync client request and response will be logged, "
            + "including bodies. Unset " + ConfigurationSection + "__Enabled to stop.";

        public const string SectionSeparator = " | ";

        public const string HeaderSeparator = "; ";

        public const string HeaderFormat = "{0}: {1}";

        // Stands in for any value that must never reach a log file. Written
        // rather than dropped, because "this header arrived and we are not
        // printing it" and "this header never arrived" are different
        // diagnoses and the trace exists to tell them apart.
        public const string RedactedValue = "[redacted]";

        public const string EmptyBodyText = "(no body)";

        public const string TruncationSuffix = "...[truncated]";

        // Request headers whose values are never written out. The auth key is
        // the credential itself; the other three are the portal's own
        // session, which a device never sends but a browser poking at these
        // paths would.
        public static readonly string[] RedactedHeaders =
        [
            AuthHeaders.AuthKey,
            "authorization",
            "cookie",
            "set-cookie",
        ];

        // Body fields whose values are never written out, by their wire
        // names - `password` covers UserCreateRequest and
        // PasswordChangeRequest, the two bodies that carry one.
        //
        // Matched against the field names of a *parsed* body rather than its
        // text, so an escaped spelling like "password" is caught too.
        public static readonly string[] RedactedBodyFields =
        [
            "password",
        ];

        // Traced even when the request carries no x-auth-user header, which
        // is how a device that failed to send its credentials at all still
        // shows up. Everything else is recognised by that header instead, so
        // a device hitting a path nobody serves is traced too - the case this
        // whole feature exists for.
        //
        // The same spellings the endpoints are routed on, so the two cannot
        // drift apart.
        public static readonly string[] ProtocolPaths =
        [
            AppRoutes.UsersAuth,
            AppRoutes.UsersCreate,
            AppRoutes.SyncsProgress,
        ];
    }

    // What the database says when it refuses a write, for the one refusal the
    // server answers rather than reports (DocumentRegistry).
    public static class Sqlite
    {
        // SQLITE_CONSTRAINT_UNIQUE: an INSERT that would have duplicated a
        // unique index. The extended result code, because the primary one
        // (SQLITE_CONSTRAINT, 19) is every constraint alike - a not-null or a
        // foreign key failure would be indistinguishable from this one.
        public const int UniqueConstraintViolation = 2067;
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

    // Named authorization policies. One so far: the Users administration page
    // and the /manage/* endpoints are the only things in the app that ask
    // "may this User manage other Users" (GLOSSARY.md: Administrator).
    public static class Authorization
    {
        // Deliberately a policy rather than a role claim. The session cookie
        // carries only the username (ADR 0002), which is why a password
        // change does not invalidate it - and a capability baked into the
        // cookie would survive a demotion for the life of that session. The
        // policy re-reads the User row instead, so revoking the capability
        // takes effect on the next request.
        public const string Administrator = "Administrator";
    }

    // What the Users administration service refuses, and what it reports when
    // it does (issue #28). These sentences are the wire messages of
    // /manage/* as well as the portal's own, so the two cannot drift: one
    // service answers both (ADR 0003).
    public static class UserAdmin
    {
        // The three wire messages /manage/* already returned, kept to the
        // letter: an existing script reading them is not a caller this work
        // gets to break.
        public const string UserCreated = "User created successfully";

        public const string PasswordChanged = "Password changed successfully";

        public const string Deleted = "Success";

        public const string MarkedActive = "User marked as active";

        public const string MarkedInactive = "User marked as inactive";

        public const string UserDoesNotExist = "User does not exist";

        public const string UserAlreadyExists = "User already exists";

        // Deliberately says neither which User was asked about nor which hash,
        // and is the single answer for both "no such User" and "no such
        // Document" (ADR 0004, issue #35). An Administrator may delete another
        // User's Document, so this endpoint cannot hide its outcome the way
        // the GET does - but it has no reason to confirm, to a caller who
        // guessed wrong, which half of the question was the miss.
        public const string DocumentNotFound = "Document not found";

        // KOReader will not attempt to log in with a blank or whitespace-only
        // password, so a credential like that cannot do the job it exists for.
        public const string PasswordRequired = "Password cannot be empty or whitespace";

        public const string UsernameRequired = "Username cannot be empty or whitespace";

        public const string NotAnAdministrator = "Only an Administrator can manage Users";

        public const string Promoted = "User is now an Administrator";

        public const string Demoted = "User is no longer an Administrator";

        // The guards, which are the substance of issue #28. They replace the
        // blanket "cannot update admin user" refusals, which reached for
        // exactly these and caught an Administrator changing their own
        // password along with them (ADR 0006).
        //
        // Self-rules hold whoever else exists: an Administrator who demotes or
        // deletes themselves has locked themselves out of the capability with
        // no way back, and "there is another Administrator who could undo it"
        // is not something the person clicking can see.
        public const string SelfDemotionRefused =
            "You cannot remove your own Administrator capability. Ask another Administrator.";

        public const string SelfDeletionRefused =
            "You cannot delete yourself. Ask another Administrator.";

        public const string SelfDeactivationRefused =
            "You cannot deactivate yourself. Ask another Administrator.";

        // Phrased as what it protects rather than as which operation was
        // refused, because one sentence covers all three ways of removing an
        // Administrator - demotion, deactivation and deletion - and the thing
        // that matters is the server being left with nobody who can manage
        // Users.
        public const string LastAdministratorRefused =
            "This is the last active Administrator. Make another User an Administrator first.";
    }

    // KOReader's own account creation, `POST /users/create` - the one way a
    // User can come into existence without an Administrator, and off unless
    // SELF_REGISTRATION_ENABLED says otherwise (issue #23).
    public static class SelfRegistration
    {
        // What SELF_REGISTRATION_ENABLED has to say for a device to be
        // allowed to register. Compared case-insensitively, so =True opts in
        // as the person writing it plainly meant; anything else leaves
        // registration off, which is the safe way round.
        public const string EnabledValue = "true";

        // What KOReader shows the person when registration is refused. It
        // names the way accounts are made instead, because "disabled" alone
        // tells a reader with no account nothing about what to do next.
        public const string Refused =
            "User registration is disabled. Ask an Administrator to create your account.";
    }

    // The Users administration page (issue #28), folded in from the
    // prototype's UsersVariantB: a dense table of Users with a slide-over
    // detail panel. Copy moved here from PrototypeConstants rather than
    // copied - the prototype folder went with issue #19.
    public static class Users
    {
        public const string PageTitle = "Manage Users";

        // Says what an Administrator is for, on the one screen where the
        // question comes up: tenancy is absolute, so this page shows counts
        // and never a Book or a Device (ADR 0004).
        public const string Subtitle =
            "Users and nothing inside them — an Administrator never reads another User's library.";

        public const string NavGroupAdministration = "Administration";

        public const string NavUsersLabel = "Users";

        public const string ColumnUsername = "Username";

        public const string ColumnRole = "Role";

        public const string ColumnStatus = "Status";

        public const string ColumnDevices = "Devices";

        public const string ColumnBooks = "Books";

        public const string ColumnActions = "Actions";

        public const string RoleLabel = "Role";

        public const string RoleAdministrator = "Administrator";

        // A User who is not an Administrator, named for what they do rather
        // than for what they lack. GLOSSARY.md has no term for this because
        // there is no such entity - it is a User without the capability.
        public const string RoleUser = "User";

        public const string StatusActive = "Active";

        public const string StatusInactive = "Inactive";

        public const string PasswordProvisional = "Provisional";

        public const string PasswordSet = "Password set";

        public const string PasswordLabel = "Password";

        public const string ProvisionalHint =
            "Inert until this User replaces the password — the portal and sync both refuse it (ADR 0006).";

        // Marks the row of the Administrator doing the looking, which is the
        // row whose actions are guarded.
        public const string SelfSuffix = "(you)";

        public const string SearchPlaceholder = "Search accounts...";

        // What the filter input points at and the table answers to - the
        // dashboard's mechanism exactly (Dashboard.TableId), because it is the
        // same script over a second table.
        public const string TableId = "users-table";

        public const string UserNoun = "User";

        public const string AdministratorNoun = "administrator";

        public const string NewUserSummaryText = "New User";

        public const string CreateUserSubmitText = "Create User";

        public const string UsernamePlaceholder = "e.g. alice";

        // Deliberately no minimum-length hint: this password exists to be
        // replaced, and PasswordPolicy does not constrain a Provisional
        // Password (ADR 0006).
        public const string ProvisionalPasswordHint =
            "Hand this password over. It identifies the new User and grants them nothing until they replace it.";

        public const string PanelSectionUser = "User";

        public const string PanelSectionLibrary = "Library";

        public const string PanelSectionActions = "Actions";

        public const string CountsOnlyNote =
            "Counts only — ADR 0004 means an Administrator never opens another User's library.";

        public const string CloseText = "Close";

        // Every row's one control, the way the dashboard's rows have one
        // "Open": it opens the slide-over where the actions live, so the
        // table itself stays scannable.
        public const string ManageAction = "Manage";

        public const string ResetPasswordAction = "Reset password";

        public const string ResetPasswordSubmitText = "Set provisional password";

        public const string DeactivateAction = "Deactivate";

        public const string ReactivateAction = "Reactivate";

        public const string PromoteAction = "Make Administrator";

        public const string DemoteAction = "Remove Administrator";

        public const string DeleteAction = "Delete";

        // Deleting a User takes their library with them (the cascade in
        // KosyncDbContext), which is not what "delete account" sounds like -
        // so the confirmation says it, and is a <details> rather than a
        // dialog because a confirmation must not round-trip (ADR 0003).
        public const string DeleteConfirmWarning =
            "This also deletes their Books, Devices and sync history. It cannot be undone.";

        public const string DeleteConfirmSubmitText = "Delete this User";

        public const string NoUsersText = "No Users yet.";

        public const string NoMatchesText = Dashboard.NoMatchesText;

        // The two reasons an action is disabled, shown in the panel so a
        // greyed-out button is not a mystery.
        public const string SelfGuardReason =
            "You cannot change your own role, deactivate yourself or delete yourself.";

        public const string LastAdministratorGuardReason =
            "The last active Administrator cannot be demoted, deactivated or deleted.";

        // The slide-over is opened by linking to it: the panel is the URL
        // fragment's target, shown by a `:target` rule in app.css. No
        // JavaScript and no request - the browser changes the fragment
        // itself - which is what ADR 0003's amendment asks of a disclosure.
        //
        // An <a> rather than the prototype's @@onclick or a label over a
        // hidden radio, because an anchor is the one control that is all
        // three things at once: focusable, activated by Enter, and able to
        // close the panel again by pointing somewhere else.
        // The ids the links point at live in AppRoutes, which is where the
        // two hrefs are built (AppRoutes.UsersPanel) - they are URLs, not
        // copy.
        public const string PanelIdFormat = "users-panel-{0}";

        // Blazor requires a unique form name per form on a page, and this
        // page renders one form per User plus the create form. The User's id
        // is what makes each unique, and it is also how the posted action
        // finds its target without trusting a field.
        public const string ActionFormNameFormat = "users-action-{0}";

        // The panel's password field, which is also rendered once per User -
        // so its id cannot be the literal its siblings on /login and
        // /change-password are, or every <label> on the page would point at
        // the first one.
        public const string ResetFieldIdFormat = "users-reset-{0}";

        public const string CreateFormName = "users-create";
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

        // KOReader's own account creation, off unless this is set (ADR 0006,
        // issue #23). Named for what setting it does rather than as the
        // inverted REGISTRATION_DISABLED it replaces: an opt-out variable
        // whose default had flipped to "disabled" would read as a double
        // negative in every compose file.
        public const string SelfRegistrationEnabled = "SELF_REGISTRATION_ENABLED";
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
    // prototype folder went with issue #19.
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
    }

    public const string NoDevicesSyncedText = "No devices have synced yet.";

    public const string NoBooksSyncedText = "No books have synced yet.";

    // A Device's detail view lists that Device's Documents, so its empty
    // state has to talk about Documents - not Books (issue #13). The
    // dashboard used to need this too, when a Device's card listed its
    // Documents; the table it became shows a count instead, where an empty
    // Device reads as a plain 0 and cannot name the wrong thing.
    public const string NoDeviceDocumentsSyncedText = "No documents synced yet";

    // The Book page's half of the same statement (issue #39). It said "No
    // sync history yet." until the two detail pages became one table - which
    // talked about history where the line above it talks about Documents,
    // for a page that lists Documents either way. Same wording shape, and
    // deliberately not the same constant: these are two pages that happen to
    // agree, and a Book's Documents are not a Device's.
    public const string NoBookDocumentsSyncedText = "No documents synced yet";

    public const string NoBookProgressText = "No progress yet";

    // What the two drill-ins render for an id they cannot find. Also what
    // they render for an id belonging to somebody else, which is the same
    // path on purpose (ADR 0004): a tenant-filtered lookup returns null, so
    // "not yours" and "not there" are indistinguishable from outside - and
    // must stay so, or the page becomes an oracle for which ids another User
    // owns.
    public const string DeviceNotFoundText = "Device not found.";

    public const string BookNotFoundText = "Book not found.";

    // What a Book is called when nobody curated a title and no Device ever
    // observed one (ADR 0007). A permanent fallback, not a transitional one:
    // KOReader's "send document metadata" setting is opt-in and recent, so
    // Books with nothing to show are a case that stays. Not under Dashboard -
    // the detail page and its page title name a Book this way too.
    public const string BookFallbackNameFormat = "Book #{0}";

    // What a Book says when it has no title and no Device has ever reported
    // anything about its files (BookMetadataRule.NeedsMetadataHint).
    // KOReader's setting is off by default, so a Book with nothing to show is
    // the normal first experience - and without this it reads as a broken
    // feature rather than a switched-off one (ADR 0007, issue #31). Names the
    // setting exactly as KOReader does, because the reader has to find it in
    // that list.
    //
    // Carried inline on the Book, the way a Split Book is flagged inline,
    // rather than collected into an inbox of things to fix.
    public const string MissingMetadataHint =
        "No title yet — enable Send document metadata in KOReader's sync settings.";

    // The two drill-ins, which since issue #39 are one design: a Book's
    // Documents and a Device's Documents are the same kind of list - scanned
    // for the one that went wrong - so both render the dashboard's
    // `hub-table`, a row expanding into the full hash and whatever history
    // that page has (docs/ROADMAP.md, "The detail pages - decided").
    //
    // The headings are here rather than in the markup for the reason
    // Dashboard's and Users' are: CLAUDE.md's "do not hard code any values",
    // and a column whose name is a literal in one page's <th> is a column
    // the other page can spell differently.
    public static class DetailPages
    {
        public const string ColumnDocument = "Document";

        // Which Device holds the Document's current progress. Only on the
        // Book page: on a Device page every row is that Device's already.
        public const string ColumnDevice = "Device";

        public const string ColumnProgress = "Progress";

        // The Device page's progress column instead of the plain word. A
        // Device's row shows that Device's own most recent push, which is
        // not the Document's current progress - another Device may have
        // pushed further since (DeviceDashboardService). "Progress" alone
        // would state the stronger thing.
        public const string ColumnDeviceProgress = "This device's progress";

        // Not "Last synced", which is what the dashboard's row-level column
        // is called. A row here is a Document, and what is being dated is
        // the arrival of a SyncEvent - a push.
        public const string ColumnLastPush = "Last push";

        public const string ColumnFlags = "Flags";

        // Labels the full hash inside an expanded row. The collapsed row
        // carries the label only (DocumentLabelRule), because ~32 monospace
        // characters per row is what stopped the table being a table; the
        // hash is reference material you fetch while diagnosing, so it is
        // behind the disclosure but never replaced by the label (ADR 0007).
        public const string HashLabel = "Hash";

        // Marks the one SyncEvent holding the Document's current-progress
        // pointer. New with issue #39 because the old page said this by the
        // *absence* of a Progress Regression flag, which is not the same
        // claim: since issue #38 the flag means "this push was blocked", and
        // most of a healthy history is neither blocked nor current.
        public const string CurrentFlag = "Current";

        // A Document whose current-progress pointer was cleared and which
        // nothing has pushed to since: percentage 0 and no Device name. Worth
        // saying rather than rendering as 0% beside a blank cell, which reads
        // as a Document somebody has not started instead of one somebody
        // reset.
        public const string NoCurrentProgressText = "not the current progress";

        // What the toolbar above each table counts. Singular, because
        // DashboardFormatting.Pluralize is what turns a count into a phrase.
        public const string DocumentNoun = "document";

        // The Device's own identifier, which KOReader generates and which is
        // the only way to tell two Devices with the same name apart.
        public const string DeviceIdLabel = "Device ID";

        // The header's progress and recency, which were literals in the
        // markup until this issue rewrote the line they sit on. Whole format
        // strings rather than the fragments they replaced, like
        // BookFallbackNameFormat: a bare "progress" concatenated in the
        // markup leaves the word order in the Razor, where a translator or
        // a rewording cannot reach it.
        public const string ProgressFormat = "{0} progress";

        public const string LastSyncedFormat = "last synced {0}";

        // The browser tab before the Book or Device has loaded, and for one
        // that was not found - the only moment either page has no name of
        // its own to show.
        public const string BookPageTitle = "Book";

        public const string DevicePageTitle = "Device";

        // The row expander, named here because App.razor's <script> and the
        // test asserting it never reaches the server have to mean the same
        // file. It is the one piece of these pages that needs script: a
        // <details> cannot wrap two <tr>s, so a row and its drawer cannot be
        // one native disclosure (ADR 0003's amendment - local UI state must
        // not round-trip, so the alternative was an @onclick).
        public const string RowExpandScript = "row-expand.js";
    }

    // How a Document is named on the two detail pages (ADR 0007, issue #31).
    public static class DocumentLabel
    {
        // How much of a hash stands in for a filename nobody observed.
        // Enough leading characters of an md5 to recognise the one KOReader
        // just showed you, short enough to read as a label rather than as a
        // hash - which is also why the full hash stays on the page beside it.
        public const int TruncatedHashLength = 8;

        public const string TruncationSuffix = "...";
    }

    // The Book detail page's Curated Metadata editor (ADR 0007, issue #30):
    // where a User types the title and author they know to be right.
    public static class CuratedMetadata
    {
        public const string SectionTitle = "Title and author";

        public const string TitleLabel = "Title";

        public const string AuthorLabel = "Author";

        public const string TitlePlaceholder = "e.g. Dune";

        public const string AuthorPlaceholder = "e.g. Frank Herbert";

        // Says which way the precedence runs, at the point where somebody is
        // about to rely on it. Without this the editor looks like it might
        // lose to the next sync.
        public const string Hint =
            "What you type here is kept, and no sync will overwrite it.";

        public const string SaveButton = "Save";

        // Only offered where there is something to clear - and it says what
        // happens next, because "clear" alone reads like "lose the title".
        public const string ClearButton = "Clear and use what devices report";

        public const string SaveFailedError = "Save failed - Book not found.";
    }

    public const string ProgressRegressionFlag = "Progress Regression";

    public const string SplitBookFlag = "Split Book";

    public const string ProgressResetFlag = "Progress Reset";

    public const string ProgressResetLabel = "Progress reset";

    public const string ResetProgressButton = "Reset progress";

    // The reset asks twice, through the same <details> shape the merge uses
    // (issue #39). It had no warning at all while it was a button on every
    // Document row - one press from an irreversible action, in a column you
    // scan past. Written like MergeConfirmWarning: what it touches, then
    // what it costs, in the second person and without "reader" (GLOSSARY,
    // "User").
    //
    // Says "this Document only" because the reset is per Document and the
    // Book above it has others - the mistake available here is resetting the
    // wrong row, not misreading what a reset does.
    public const string ResetProgressConfirmWarning =
        "This clears the progress on this Document only. You start it again from the beginning.";

    public const string ResetProgressFailedError = "Reset failed - document not found.";

    // The manual merge form (issue #8), still the only way to merge two Books
    // that nothing suggested - which the narrow suggestion rule leaves as the
    // common case (ADR 0007).
    public const string MergeFormSectionTitle = "Merge another Book into this one";

    public const string MergeFormPlaceholder = "Book #";

    public const string MergeFormSubmitText = "Merge";

    // Both ways of starting a merge ask twice, through these two strings.
    // MergeBooksAsync is transactional but not undoable (ADR 0007), and both
    // entry points can be wrong about which Books are one Book: a typo in the
    // form picks a Book nobody looked at, and a suggestion was put on screen
    // by a rule rather than by the reader.
    //
    // Says what moves where rather than only "are you sure": the merge is not
    // symmetric, and which Book survives is the part a reader can still get
    // wrong at this point - by merging from the wrong page.
    public const string MergeConfirmWarning =
        "This moves every Document from that Book onto this one and removes it. A merge cannot be undone.";

    public const string MergeConfirmSubmitText = "Merge these Books";

    public const string MergeBookIdRequiredError = "Enter a Book # to merge.";

    public const string MergeSameBookError = "Can't merge a Book into itself.";

    public const string MergeBookNotFoundErrorFormat = "Book #{0} was not found.";

    public const string MergeFailedError = "Merge failed.";

    // Merge Suggestions (GLOSSARY.md, ADR 0007, issue #32): the Books that
    // report an identical title and author to the one on screen. Suggested,
    // never merged - which is why every string here is about a choice the
    // reader is being offered rather than one already taken.
    public static class MergeSuggestions
    {
        public const string SectionTitle = "Books that look like this one";

        // Says what matched and what will not happen. A merge is
        // transactional but not undoable, so the reader deciding has to know
        // both halves before pressing anything (ADR 0007).
        public const string Hint =
            "Matched on an identical title and author. Nothing is merged until you choose it, and a merge cannot be undone.";

        // Why this Book is in the list, beside the Book it is about. Short
        // because the hint above has already explained the rule.
        public const string Reason = "Same title and author";

        // Names the direction of the merge, because it is not symmetric: the
        // Book on screen is the one that survives, and a reader who wanted
        // the other id to survive has to open that Book instead.
        public const string MergeAction = "Merge into this Book";

        // The dashboard's Flags column, where a Book row has no room for the
        // prose above - it says only that there is something to look at, and
        // the drill-in says what (issue #18's table).
        public const string Flag = "Merge suggestion";

        // Since issue #39 the suggestions live inside the merge disclosure
        // rather than unconditionally above the Documents, so the count has
        // to reach the summary: closed, the disclosure is the only thing on
        // screen that can say there is something in there. The *flag* is
        // unaffected and stays inline and unconditional on the Book header
        // and in the dashboard's Flags column - only the action moved
        // (docs/ROADMAP.md, "The detail pages - decided").
        //
        // MergeFormSubmitText is what the summary reads with no suggestions,
        // which is why this carries the word rather than only the suffix.
        public const string MergeDisclosureFormat = "Merge ({0} suggested)";
    }

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
