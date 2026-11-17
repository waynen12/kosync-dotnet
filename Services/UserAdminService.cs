namespace Kosync.Services;

// One User as the administration page lists them: the account facts, plus the
// size of the library behind them and nothing from inside it. Tenancy is
// absolute (ADR 0004), so these counts are as close as an Administrator ever
// gets to another User's reading.
public record UserSummary(
    int Id,
    string Username,
    bool IsAdministrator,
    bool IsActive,
    bool HasProvisionalPassword,
    int DeviceCount,
    int BookCount,
    int DocumentCount,
    DateTime? LastSyncedAt);

// Why an operation was refused, separately from the sentence shown for it:
// the portal renders the sentence, and /manage/* needs the reason to pick a
// status code.
public enum UserAdminOutcome
{
    Succeeded,
    NotAnAdministrator,
    UserNotFound,
    UsernameRequired,
    UsernameTaken,
    PasswordRequired,
    PasswordRejected,
    SelfDemotionRefused,
    SelfDeactivationRefused,
    SelfDeletionRefused,
    LastAdministratorRefused,
    DocumentNotFound,
}

public record UserAdminResult(UserAdminOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome is UserAdminOutcome.Succeeded;

    public static UserAdminResult Ok(string message) => new(UserAdminOutcome.Succeeded, message);

    public static UserAdminResult Refused(UserAdminOutcome outcome, string message) => new(outcome, message);
}

// Administering Users: listing them, creating them, resetting their
// passwords, activating and deactivating them, granting and revoking the
// Administrator capability, and deleting them.
//
// One service rather than one per caller (issue #28). The portal's Users page
// calls it directly, in-process (ADR 0003), and ManagementController
// delegates to it rather than keeping a second copy of the same rules - which
// is what let the blanket "cannot update admin user" refusals drift away from
// the guards they were reaching for (ADR 0006).
//
// Every method takes the acting User's id rather than resolving one from
// ambient state, for the reason ADR 0004 gives: the callers know "who" by
// incompatible means - a controller from the x-auth-user header, a portal
// page from its cookie - and an ambient chain has a branch that resolves to
// nobody. Here that branch would disable the self-guards, which are the
// substance of this service.
public class UserAdminService
{
    private readonly KosyncDbContext _db;
    private readonly PasswordHasher _passwordHasher;
    private readonly PasswordPolicy _passwordPolicy;

    public UserAdminService(KosyncDbContext db, PasswordHasher passwordHasher, PasswordPolicy passwordPolicy)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _passwordPolicy = passwordPolicy;
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync()
    {
        // Account operations reach across Users by design - that is what an
        // Administrator is (ADR 0004) - so this says so with
        // IgnoreQueryFilters rather than inheriting a tenant. It has to:
        // the page that calls it sets no tenant at all, because it reads
        // nobody's library. What crosses Users here is a count, never a row
        // of one.
        return await _db.Users
            .IgnoreQueryFilters()
            .OrderBy(u => u.Username)
            .Select(u => new UserSummary(
                u.Id,
                u.Username,
                u.IsAdministrator,
                u.IsActive,
                u.PasswordState != PasswordState.UserChosen,
                u.Devices.Count,
                u.Books.Count,
                u.Documents.Count,
                u.Devices
                    .SelectMany(d => d.SyncEvents)
                    .Max(s => (DateTime?)s.Timestamp)))
            .ToListAsync();
    }

    public async Task<UserAdminResult> CreateAsync(
        int actingUserId, string? username, string? password, bool isAdministrator)
    {
        if (await ActingAdministratorAsync(actingUserId) is null)
        {
            return NotAnAdministrator();
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return UserAdminResult.Refused(
                UserAdminOutcome.UsernameRequired, Constants.UserAdmin.UsernameRequired);
        }

        // Trimmed, because the only way back to a User is an exact match on
        // this column: " alice" would be stored, listed, and then not found
        // by anybody typing the name they can see.
        username = username.Trim();

        // Only "not blank", deliberately: this password exists to be handed
        // over and replaced, and PasswordPolicy's minimum binds where a User
        // picks their own (ADR 0006).
        if (string.IsNullOrWhiteSpace(password))
        {
            return PasswordRequired();
        }

        if (await _db.Users.AnyAsync(u => u.Username == username))
        {
            return UserAdminResult.Refused(
                UserAdminOutcome.UsernameTaken, Constants.UserAdmin.UserAlreadyExists);
        }

        _db.Users.Add(new User
        {
            Username = username,
            PasswordHash = _passwordHasher.HashPlainText(password),
            IsAdministrator = isAdministrator,
            // PasswordState is left at its default of Provisional, which is
            // what this operation wants: an Administrator picked this
            // password and is about to hand it over, so it grants the new
            // User nothing until they replace it (ADR 0006).
        });

        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(Constants.UserAdmin.UserCreated);
    }

    // Sets a User's password. Whose choice it counts as is decided by whether
    // the target is the caller (ADR 0006): a password you set for yourself is
    // yours, one set for somebody else is Provisional and grants that User
    // nothing until they replace it - whether or not they had replaced one
    // before.
    public async Task<UserAdminResult> ResetPasswordAsync(int actingUserId, string username, string? password)
    {
        var (acting, target, refusal) = await ResolveAsync(actingUserId, username);

        if (refusal is not null)
        {
            return refusal;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return PasswordRequired();
        }

        // Compared by id rather than by name: the caller is already resolved,
        // so this cannot be thrown off by how the username reached us.
        var isOwnPassword = target.Id == acting.Id;

        // The policy binds only where a User picks their own password
        // (ADR 0006). It deliberately does not constrain a Provisional
        // Password, which the forced change replaces anyway - and padding one
        // to the minimum would be the wrong thing to ask for.
        //
        // The current password is not passed: this service never sees it, so
        // the "not the one you already have" rule is simply not in play here.
        // The change-password page is where a User replaces their own, and it
        // does know it.
        if (isOwnPassword && _passwordPolicy.Validate(password) is string policyError)
        {
            return UserAdminResult.Refused(UserAdminOutcome.PasswordRejected, policyError);
        }

        target.PasswordHash = _passwordHasher.HashPlainText(password);
        target.PasswordState = isOwnPassword ? PasswordState.UserChosen : PasswordState.Provisional;

        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(Constants.UserAdmin.PasswordChanged);
    }

    public async Task<UserAdminResult> SetActiveAsync(int actingUserId, string username, bool isActive)
    {
        var (acting, target, refusal) = await ResolveAsync(actingUserId, username);

        return refusal ?? await SetActiveAsync(acting, target, isActive);
    }

    // The shape /manage/users/active has always had, kept because that is its
    // documented behaviour: it flips the state rather than setting it. The
    // portal uses SetActiveAsync instead - it rendered the current state, so
    // it can say which state it means and a stale page cannot flip the wrong
    // way.
    public async Task<UserAdminResult> ToggleActiveAsync(int actingUserId, string username)
    {
        var (acting, target, refusal) = await ResolveAsync(actingUserId, username);

        return refusal ?? await SetActiveAsync(acting, target, !target.IsActive);
    }

    public async Task<UserAdminResult> SetAdministratorAsync(
        int actingUserId, string username, bool isAdministrator)
    {
        var (acting, target, refusal) = await ResolveAsync(actingUserId, username);

        if (refusal is not null)
        {
            return refusal;
        }

        // Granting the capability creates an Administrator rather than
        // removing one, so only the revoking direction is guarded.
        if (!isAdministrator &&
            await RefuseRemovingAsync(acting, target, UserAdminOutcome.SelfDemotionRefused) is UserAdminResult guarded)
        {
            return guarded;
        }

        target.IsAdministrator = isAdministrator;
        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(isAdministrator
            ? Constants.UserAdmin.Promoted
            : Constants.UserAdmin.Demoted);
    }

    public async Task<UserAdminResult> DeleteAsync(int actingUserId, string username)
    {
        var (acting, target, refusal) = await ResolveAsync(actingUserId, username);

        if (refusal is not null)
        {
            return refusal;
        }

        if (await RefuseRemovingAsync(acting, target, UserAdminOutcome.SelfDeletionRefused) is UserAdminResult guarded)
        {
            return guarded;
        }

        // Their Books, Documents, Devices and SyncEvents go with them, by the
        // cascades on KosyncDbContext's relationships. No orphan sweep is
        // needed the way DELETE /manage/users/documents needs one: since
        // ADR 0004 gave a Book an owner, every row in a User's library hangs
        // off the User row being removed here.
        _db.Users.Remove(target);
        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(Constants.UserAdmin.Deleted);
    }

    // Deletes one Document of one User's library - the only operation here
    // that touches a library at all, and the only one anybody but an
    // Administrator may call.
    //
    // An Administrator may delete another User's Document. That is a
    // deliberate carve-out from "tenancy is absolute" rather than the gap
    // ADR 0004 originally left it in (issue #35): a Document whose hash went
    // bad, or a Split Book that wants clearing, is exactly what an
    // Administrator gets asked to sort out, and "delete the whole account, it
    // cascades" is wildly disproportionate. Reading a library stays banned -
    // GET /manage/users/documents refuses an Administrator outright - and the
    // asymmetry is the point: that GET returned a whole library, while this
    // can only answer a question about a hash the caller already holds, and a
    // Document hash is an MD5 over file content with nothing to enumerate.
    //
    // Which is why the refusals here say as little as they can. A missing
    // User and a missing Document are one answer that names neither, so a
    // caller who guessed wrong is not told which half of the question missed.
    public async Task<UserAdminResult> DeleteDocumentAsync(
        int actingUserId, string username, string documentHash)
    {
        var acting = await ActingUserAsync(actingUserId);

        // Nobody usable is asking - an inactive User, or an id with no row
        // behind it. The Administrator sentence is a loose fit for either,
        // and it is the backstop rather than the answer anybody sees: both
        // callers turn those away before reaching here, the endpoint with its
        // own "Unauthorized".
        if (acting is null)
        {
            return NotAnAdministrator();
        }

        // The one query in this service that escapes the tenant filters to
        // read a library, and a documented bypass rather than an oversight -
        // so don't "fix" it. Its Books come along so the orphan sweep below
        // needs no second bypass of its own: one place escapes the filters
        // here, and this is it.
        var target = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Documents)
            .Include(u => u.Books)
            .FirstOrDefaultAsync(u => u.Username == username);

        // Anybody may delete their own; reaching into somebody else's is the
        // Administrator capability.
        //
        // "Their own" is answered by id where the target resolved, so it
        // cannot be thrown off by how the username reached us - and by name,
        // case-insensitively, where it did not. The second half is not
        // belt-and-braces: a name differing only in case still names the
        // caller to the caller, the exact-match lookup above cannot say so,
        // and without it somebody asking about their own library in the
        // wrong case would be refused as if they had asked about a stranger's
        // instead of told their Document was not found.
        var isOwnLibrary = target?.Id == acting.Id ||
            string.Equals(username, acting.Username, StringComparison.OrdinalIgnoreCase);

        if (!acting.IsAdministrator && !isOwnLibrary)
        {
            return NotAnAdministrator();
        }

        // Safe as a Single because of the unique index on (UserId,
        // DocumentHash), not because nobody has written the duplicate yet
        // (ADR 0008, issue #37) - this delete was the one route out of a
        // duplicate pair, and threw on it like everything else.
        var document = target?.Documents.SingleOrDefault(d => d.DocumentHash == documentHash);

        if (document is null)
        {
            return UserAdminResult.Refused(
                UserAdminOutcome.DocumentNotFound, Constants.UserAdmin.DocumentNotFound);
        }

        target!.Documents.Remove(document);
        _db.Documents.Remove(document);

        // A Book with no Documents left is a phantom in the reader's library
        // (ADR 0001), so it goes with the last Document on it. Answered from
        // the rows already loaded rather than by querying again: every
        // Document on a Book belongs to that Book's owning User (ADR 0004),
        // so this collection is the whole truth about what is left on it.
        // That invariant is enforced rather than assumed - the AddBookUserId
        // migration's `CHECK (OwnerCount = 1)` aborts rather than backfill a
        // Book two Users share - which is what makes an in-memory answer as
        // sound as the second filter bypass it replaced.
        if (!target.Documents.Any(d => d.BookId == document.BookId) &&
            target.Books.FirstOrDefault(b => b.Id == document.BookId) is Book orphaned)
        {
            _db.Books.Remove(orphaned);
        }

        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(Constants.UserAdmin.Deleted);
    }

    private async Task<UserAdminResult> SetActiveAsync(User acting, User target, bool isActive)
    {
        // Reactivating removes nobody, so neither guard is in play.
        if (!isActive &&
            await RefuseRemovingAsync(acting, target, UserAdminOutcome.SelfDeactivationRefused) is UserAdminResult guarded)
        {
            return guarded;
        }

        target.IsActive = isActive;
        await _db.SaveChangesAsync();

        return UserAdminResult.Ok(isActive
            ? Constants.UserAdmin.MarkedActive
            : Constants.UserAdmin.MarkedInactive);
    }

    // Who is asking and who the operation is about - the pair every method
    // but Create needs - or the refusal that stands in their place. Acting
    // and Target are null only when Refusal is not, which is the invariant
    // the callers lean on when they return early.
    private async Task<(User Acting, User Target, UserAdminResult? Refusal)> ResolveAsync(
        int actingUserId, string username)
    {
        var acting = await ActingAdministratorAsync(actingUserId);

        if (acting is null)
        {
            return (null!, null!, NotAnAdministrator());
        }

        // Users carry no tenant filter (ADR 0004), so a target is found by
        // name with no scoping step - which is what lets this run on a page
        // that has set no tenant at all.
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

        return target is null
            ? (acting, null!, UserAdminResult.Refused(
                UserAdminOutcome.UserNotFound, Constants.UserAdmin.UserDoesNotExist))
            : (acting, target, null);
    }

    // The two guards ADR 0006 describes, applied to the three operations that
    // remove an Administrator - demotion, deactivation and deletion - since
    // the question each asks is the same one.
    //
    // The last-Administrator rule is checked first on purpose. On a server
    // with one Administrator, every way of removing them is that person
    // acting on themselves, so the self-rules would catch all of it and
    // answer "ask another Administrator" - advice to ask nobody. Checked in
    // this order, each refusal names the obstacle that is really there: the
    // self-rules are then what you meet when another Administrator does
    // exist.
    private async Task<UserAdminResult?> RefuseRemovingAsync(
        User acting, User target, UserAdminOutcome selfOutcome)
    {
        if (target.IsAdministrator && target.IsActive && !await AnotherActiveAdministratorExistsAsync(target.Id))
        {
            return UserAdminResult.Refused(
                UserAdminOutcome.LastAdministratorRefused, Constants.UserAdmin.LastAdministratorRefused);
        }

        if (target.Id == acting.Id)
        {
            return UserAdminResult.Refused(selfOutcome, SelfRefusalMessage(selfOutcome));
        }

        return null;
    }

    // "Active", because an Administrator who cannot log in is not somebody
    // the server can fall back on - leaving only that account is leaving
    // nobody who can manage Users.
    private Task<bool> AnotherActiveAdministratorExistsAsync(int exceptUserId) =>
        _db.Users.AnyAsync(u => u.Id != exceptUserId && u.IsAdministrator && u.IsActive);

    // The caller, if they may administer Users at all. Null is the refusal,
    // and it covers an inactive Administrator and a User id that no longer
    // exists as well as a plain non-Administrator: all three are "you cannot
    // do this", and the page and the API each say so their own way.
    //
    // Both callers check this for themselves too - the page through its
    // authorization policy, the controller through UserService - so this is
    // the backstop that makes the service safe to re-expose, not the only
    // check in the path.
    private async Task<User?> ActingAdministratorAsync(int actingUserId)
    {
        return await ActingUserAsync(actingUserId) is { IsAdministrator: true } acting ? acting : null;
    }

    // The caller, if they may act at all. Only DeleteDocumentAsync stops
    // here: it is the one operation a User may call on their own behalf, so
    // it asks "who is this" and decides about the Administrator capability
    // itself. Everything else wants the Administrator check folded in.
    private async Task<User?> ActingUserAsync(int actingUserId)
    {
        var acting = await _db.Users.FirstOrDefaultAsync(u => u.Id == actingUserId);

        return acting is { IsActive: true } ? acting : null;
    }

    private static string SelfRefusalMessage(UserAdminOutcome outcome) => outcome switch
    {
        UserAdminOutcome.SelfDemotionRefused => Constants.UserAdmin.SelfDemotionRefused,
        UserAdminOutcome.SelfDeactivationRefused => Constants.UserAdmin.SelfDeactivationRefused,
        _ => Constants.UserAdmin.SelfDeletionRefused,
    };

    private static UserAdminResult NotAnAdministrator() => UserAdminResult.Refused(
        UserAdminOutcome.NotAnAdministrator, Constants.UserAdmin.NotAnAdministrator);

    private static UserAdminResult PasswordRequired() => UserAdminResult.Refused(
        UserAdminOutcome.PasswordRequired, Constants.UserAdmin.PasswordRequired);
}
