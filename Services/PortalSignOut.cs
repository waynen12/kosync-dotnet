using Microsoft.AspNetCore.Authentication;

namespace Kosync.Services;

// Ending a portal session: drop the cookie, and say so in the log.
//
// One place, because there are two callers (issue #43) - the POST the
// SignOutForm control makes, and the change-password page's dead end, where
// the cookie names a User who no longer exists. The confirm screen used to be
// the single caller and so could hold this itself; with the screen gone, the
// alternative was the same three lines written twice.
//
// Takes the HttpContext rather than reading one from an accessor: signing out
// writes a Set-Cookie header to *this* response, so the context is the thing
// being acted on and not an ambient detail (ADR 0003).
public static class PortalSignOut
{
    public static async Task EndAsync(HttpContext context, IPService ipService, ILogger logger)
    {
        // Read before the sign-out, which is what empties it.
        var username = context.User.Identity?.Name;

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        logger.LogInformation(
            "[{ip}] User [{username}] signed out of the portal.", ipService.ClientIP, username);
    }
}
