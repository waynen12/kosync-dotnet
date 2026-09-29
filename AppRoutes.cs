namespace Kosync;

public static class AppRoutes
{
    public const string Login = "/login";

    public const string Dashboard = "/dashboard";

    public static string DashboardBooksTab => $"{Dashboard}?tab={Constants.BooksTab}";

    public const string DeviceDetailTemplate = "/dashboard/devices/{id:int}";

    public static string DeviceDetail(int id) => $"/dashboard/devices/{id}";

    public const string BookDetailTemplate = "/dashboard/books/{id:int}";

    public static string BookDetail(int id) => $"/dashboard/books/{id}";
}
