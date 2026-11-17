using Microsoft.AspNetCore.Mvc.Controllers;

namespace Kosync.Middleware;

// A User holding a Provisional Password reaches nothing in the portal but
// the change-password page and logout (ADR 0006). Until they replace the
// password somebody else chose for them, the account grants nothing.
//
// Middleware rather than a check in MainLayout, which ADR 0006 settled: a
// layout check runs per component render, so a page that takes a different
// layout - or no layout - sidesteps it silently, and nothing fails until
// somebody notices the hole. Middleware sees every request, including the
// ones that never reach a component.
//
// Only the portal is in scope. The KOReader protocol and /manage/* endpoints
// are controller actions that authenticate from headers per request and
// refuse a Provisional Password themselves
// (ProvisionalPasswordSyncRefusalTests), so this steps aside for them -
// deliberately by what the endpoint is rather than by whether a cookie came
// with the request. A browser signed into the portal sends its cookie to
// every path on the host, and keying on the cookie would answer those calls
// with an HTML redirect: that would break PUT /manage/users/password, the
// route ADR 0006 leans on as the way a User with no working portal replaces
// their password.
public class PasswordChangeGateMiddleware
{
    private readonly RequestDelegate _next;

    public PasswordChangeGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // KosyncDbContext is scoped, so it is taken per invocation rather than
    // through the constructor.
    public async Task InvokeAsync(HttpContext context, KosyncDbContext db)
    {
        var username = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name
            : null;

        if (string.IsNullOrEmpty(username)
            || IsApiEndpoint(context)
            || IsEscapeRoute(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Users carry no tenant filter (ADR 0004), which is what lets this
        // read run before any page has scoped the request to a User.
        var mustChangePassword = await db.Users
            .Where(u => u.Username == username)
            .Select(u => (bool?)(u.PasswordState != PasswordState.UserChosen))
            .FirstOrDefaultAsync();

        // Asked of the enum here rather than through User.MustChangePassword,
        // which is [NotMapped] and so cannot be translated into the query.
        // The spelling is the same deliberate one: "anything that is not the
        // User's own choice", so a PasswordState added later - the expiry
        // ADR 0006 anticipates - gates until somebody exempts it.
        if (mustChangePassword is not true)
        {
            await _next(context);
            return;
        }

        // A plain redirect rather than a challenge: the User is
        // authenticated, there is nothing wrong with their session, and the
        // thing they have to do has a page of its own.
        context.Response.Redirect(AppRoutes.ChangePassword);
    }

    // The KOReader protocol surface and /manage/*, which are the only
    // controller actions this app has - everything else it serves is a Razor
    // component. Asked of the matched endpoint rather than of the path, so
    // an endpoint added to either controller is covered without a second
    // list of prefixes to keep in step.
    //
    // Note the direction: this exempts a known surface, and anything
    // unrecognised still gates. A new portal route is therefore gated the
    // moment it exists, rather than waiting to be added to a list.
    private static bool IsApiEndpoint(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null;
    }

    // The change-password page and logout, plus the framework endpoints the
    // page itself depends on (Constants.PortalGate.ExemptPathPrefixes).
    // /login is in here too: a signed-in User who navigates to it should see
    // the login screen, not be bounced to a page about a password they are
    // apparently trying to sign in with as somebody else.
    private static bool IsEscapeRoute(PathString path)
    {
        if (path.Equals(AppRoutes.ChangePassword, StringComparison.OrdinalIgnoreCase)
            || path.Equals(AppRoutes.Logout, StringComparison.OrdinalIgnoreCase)
            || path.Equals(AppRoutes.Login, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Constants.PortalGate.ExemptPathPrefixes
            .Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
