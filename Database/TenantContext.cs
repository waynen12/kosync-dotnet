namespace Kosync.Database;

// The current User that KosyncDbContext's global query filters scope every
// read to (ADR 0004). Deliberately a piece of state that somebody has to
// set, not something resolved ambiently: the four callers that know "who"
// know it by incompatible means - controllers from the x-auth-user header,
// /login from a cookie claim, interactive Blazor circuits from
// AuthenticationStateProvider (they have no usable HttpContext after the
// first render), and startup code from nothing at all - so any single
// ambient chain has a branch that resolves to "no user".
//
// Which is why this fails closed. With no User set, the filters compare
// against Constants.Tenancy.NoTenantUserId, which no row can carry, so an
// operation that forgot to set the tenant renders an empty dashboard -
// noticed in seconds - rather than quietly serving everybody's library.
//
// Has no dependencies on purpose: KosyncDbContext depends on this, so
// anything this depended on could not use the database.
public sealed class TenantContext
{
    private int? _userId;
    private string? _bypassReason;

    // The User every read is scoped to, or null if none has been set.
    public int? UserId => _userId;

    // What the global query filters actually compare a row's UserId against.
    public int FilterUserId => _userId ?? Constants.Tenancy.NoTenantUserId;

    public bool IsBypassed => _bypassReason is not null;

    // Why the filters are off. Named at the call site and reported back in
    // the exception if something later tries to act for a User in the same
    // scope, so "who turned the filters off" is answerable from the error
    // rather than from a debugger.
    public string? BypassReason => _bypassReason;

    // Call at the start of an operation performed on behalf of a User, before
    // the first query that reads their Books, Devices or Documents.
    public void SetUser(int userId)
    {
        if (_bypassReason is not null)
        {
            throw new InvalidOperationException(
                string.Format(Constants.Tenancy.SetUserAfterBypassErrorFormat, _bypassReason));
        }

        // Re-setting the same User is how a Blazor page behaves: its
        // OnInitializedAsync runs once during static SSR and again when the
        // circuit starts, and again on every navigation within the circuit.
        // Two *different* Users inside one scope is not a thing any caller
        // legitimately does, and silently honouring the second would mean a
        // query somewhere above it already ran as somebody else.
        if (_userId is int existing && existing != userId)
        {
            throw new InvalidOperationException(
                string.Format(Constants.Tenancy.TenantReassignedErrorFormat, existing, userId));
        }

        _userId = userId;
    }

    // The explicit, deliberate opt-out ADR 0004 requires for code that
    // legitimately runs outside any User: startup seeding, the LiteDB import,
    // and `dotnet ef` at design time. Nothing else may call this. The reason
    // is mandatory so that every bypass names itself at its call site, which
    // is the thing to review hardest in this whole mechanism.
    public void BypassFilters(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_userId is not null)
        {
            throw new InvalidOperationException(Constants.Tenancy.BypassAfterSetUserError);
        }

        _bypassReason = reason;
    }

    // For the places that build a KosyncDbContext by hand rather than taking
    // one from the container - the design-time factory, chiefly.
    public static TenantContext ForSystemOperation(string reason)
    {
        var tenant = new TenantContext();
        tenant.BypassFilters(reason);

        return tenant;
    }
}
