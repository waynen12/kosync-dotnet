namespace Kosync;

public static class AppRoutes
{
    public const string Login = "/login";

    // The forced first password change (ADR 0006) and the one way out of it
    // that isn't completing it. Both are escape routes from
    // PasswordChangeGateMiddleware, which redirects every other
    // cookie-authenticated route here while a User's Password is Provisional.
    public const string ChangePassword = "/change-password";

    public const string Logout = "/logout";

    // The Users administration page (issue #28). A top-level route rather
    // than a /dashboard child: the dashboard is a User's own library, and
    // this page is the one screen that is deliberately not about the signed-in
    // User's reading at all. The protocol's own /users/auth and /users/create
    // are sibling paths under the same prefix and do not collide with it.
    public const string Users = "/users";

    // Where a row links to open its slide-over, and where the backdrop and
    // the close button link to shut it (issue #28). The panel on screen is
    // the fragment's target, so this is the whole of that state - no script
    // and no request (ADR 0003).
    //
    // Written as a full path rather than a bare "#id", which is the trap
    // here: App.razor sets <base href="/">, and a fragment-only href
    // resolves against the document base - so "#users-panel-3" means
    // "/#users-panel-3", a different document, and clicking it navigates
    // away from the page instead of targeting anything on it.
    public static string UsersPanel(string panelId) => $"{Users}#{panelId}";

    // Closing points at a fragment that names no element, so nothing is
    // :target and every panel's rule stops matching.
    //
    // Deliberately not a bare "#", which is the obvious spelling and does not
    // work: Chromium answered the click with a background fetch of the page,
    // because an empty fragment is not the fragment-only change that
    // Blazor's enhanced navigation and the browser both leave alone. One
    // named fragment to another is.
    public static string UsersPanelClosed => $"{Users}#{UsersPanelClosedFragment}";

    private const string UsersPanelClosedFragment = "users-panel-closed";

    public const string Dashboard = "/dashboard";

    public static string DashboardBooksTab => $"{Dashboard}?tab={Constants.BooksTab}";

    public const string DeviceDetailTemplate = "/dashboard/devices/{id:int}";

    public static string DeviceDetail(int id) => $"/dashboard/devices/{id}";

    public const string BookDetailTemplate = "/dashboard/books/{id:int}";

    public static string BookDetail(int id) => $"/dashboard/books/{id}";
}
