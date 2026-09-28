namespace Kosync;

public static class AppRoutes
{
    public const string Login = "/login";

    public const string Dashboard = "/dashboard";

    public const string DeviceDetailTemplate = "/dashboard/devices/{id:int}";

    public static string DeviceDetail(int id) => $"/dashboard/devices/{id}";
}
