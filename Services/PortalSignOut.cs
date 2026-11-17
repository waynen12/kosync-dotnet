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
//
// Takes a factory rather than a logger, so that the category is this module's
// and not each caller's. Handed an ILogger, the two callers would write the
// same sentence under two different categories - Kosync.Logout from the
// endpoint and Kosync.Components.Pages.ChangePassword from the page - and an
// operator filtering their log for sign-outs would see half of them.
public static class PortalSignOut
{
    public static async Task EndAsync(
        HttpContext context, IPService ipService, ILoggerFactory loggerFactory)
    {
        // Read before the sign-out, which is what empties it.
        var username = context.User.Identity?.Name;

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        loggerFactory.CreateLogger(Constants.Logout.LogCategory).LogInformation(
            "[{ip}] User [{username}] signed out of the portal.", ipService.ClientIP, username);
    }
}
