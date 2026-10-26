namespace Kosync.Models;

// Bound from the "Portal" configuration section, so the address a refused
// sync tells a User to visit is a deployment decision (appsettings.json, or
// Portal__BaseUrl in a container) rather than a compiled-in guess at an
// address only the operator knows - see Constants.Portal.DefaultBaseUrl.
public class PortalOptions
{
    public string BaseUrl { get; set; } = Constants.Portal.DefaultBaseUrl;

    // The login page, not the bare host. The message this ends up in says
    // "log in at ...", and the person reading it off a KOReader screen is
    // typing it into a browser by hand - so it should land them where they
    // can actually do the thing being asked of them.
    //
    // A blank or whitespace BaseUrl falls back to the default rather than
    // producing a bare "/login" that names no host at all, which would make
    // the message useless in exactly the case it matters most.
    public string LoginUrl
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(BaseUrl)
                ? Constants.Portal.DefaultBaseUrl
                : BaseUrl;

            return $"{configured.TrimEnd('/')}{AppRoutes.Login}";
        }
    }
}
