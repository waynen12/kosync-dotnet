using Microsoft.AspNetCore.Components.Authorization;

namespace Kosync.Services;

// How a portal page scopes itself to the User looking at it (ADR 0004).
// Every page that reads Books, Devices or Documents calls this at the top of
// its OnInitializedAsync - explicitly, because a page is one of four callers
// that know "who" by four incompatible means and this is the one that applies
// to a Blazor circuit.
//
// The source is AuthenticationStateProvider rather than IHttpContextAccessor
// on purpose: an interactive circuit has no usable HttpContext after its
// first render (ADR 0003), so an HttpContext-based resolver would pass every
// integration test and then resolve to "no user" inside a live circuit. Under
// a fail-closed filter that is an empty dashboard; under an allow-by-default
// one it would have been everybody's library.
public class PortalTenantResolver
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly KosyncDbContext _db;
    private readonly TenantContext _tenant;

    public PortalTenantResolver(
        AuthenticationStateProvider authenticationStateProvider,
        KosyncDbContext db,
        TenantContext tenant)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _db = db;
        _tenant = tenant;
    }

    // Returns false when there is nobody to scope to - an anonymous visitor,
    // or a cookie naming a User who has since been deleted. The tenant is
    // then left unset, so the caller's queries return nothing rather than
    // somebody else's rows.
    public async Task<bool> ScopeToSignedInUserAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var username = state.User.Identity?.Name;

        if (string.IsNullOrEmpty(username))
        {
            return false;
        }

        // Users are deliberately not tenant-scoped, which is what lets this
        // run before a tenant exists.
        var userId = await _db.Users
            .Where(u => u.Username == username)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync();

        if (userId is null)
        {
            return false;
        }

        _tenant.SetUser(userId.Value);

        return true;
    }
}
