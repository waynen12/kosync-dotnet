using Microsoft.AspNetCore.Authorization;

namespace Kosync.Services;

// "This User may manage other Users" (GLOSSARY.md: Administrator), as an
// authorization requirement the portal's router can enforce - so the Users
// administration page refuses at the route rather than inside the page
// (issue #28).
//
// Deliberately not a role claim on the session cookie. The cookie carries
// only the username (ADR 0002), which is why it survives a password change;
// a capability written into it would also survive a demotion, for as long as
// that session lasted. This re-reads the User row, so revoking the capability
// takes effect on the next request - the same choice UserService makes for
// MustChangePassword, for the same reason.
//
// /manage/* does not use this: those endpoints authenticate from the
// x-auth-user header per request and have no ClaimsPrincipal to evaluate a
// policy against, so they keep their own UserService.IsAdmin checks. Both
// paths end up at the same place anyway - UserAdminService refuses a caller
// who is not an active Administrator whoever asks.
public class AdministratorRequirement : IAuthorizationRequirement;

public class AdministratorRequirementHandler : AuthorizationHandler<AdministratorRequirement>
{
    private readonly KosyncDbContext _db;

    public AdministratorRequirementHandler(KosyncDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdministratorRequirement requirement)
    {
        var username = context.User.Identity?.Name;

        if (string.IsNullOrEmpty(username))
        {
            return;
        }

        // Inactive counts as not an Administrator: an account that cannot log
        // in is not one the server falls back on, and the same reading makes
        // the last-Administrator guard in UserAdminService mean what it says.
        //
        // Users carry no tenant filter (ADR 0004), so this read needs no
        // scoping step - which it could not do anyway, since it runs before
        // the page it is authorizing.
        var isAdministrator = await _db.Users
            .Where(u => u.Username == username)
            .Select(u => u.IsAdministrator && u.IsActive)
            .FirstOrDefaultAsync();

        if (isAdministrator)
        {
            context.Succeed(requirement);
        }
    }
}
