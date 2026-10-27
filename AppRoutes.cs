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

    public const string Dashboard = "/dashboard";

    public static string DashboardBooksTab => $"{Dashboard}?tab={Constants.BooksTab}";

    public const string DeviceDetailTemplate = "/dashboard/devices/{id:int}";

    public static string DeviceDetail(int id) => $"/dashboard/devices/{id}";

    public const string BookDetailTemplate = "/dashboard/books/{id:int}";

    public static string BookDetail(int id) => $"/dashboard/books/{id}";
}
