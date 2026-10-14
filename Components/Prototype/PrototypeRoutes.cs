namespace Kosync.Components.Prototype;

// THROWAWAY PROTOTYPE routes. Mirrors AppRoutes so the @page literals stay in
// one place. The Users screen has no real page yet (Phase 2.5), so it lives
// under the existing /dashboard prefix rather than inventing a new top-level
// structure. Delete with Components/Prototype/.
public static class PrototypeRoutes
{
    public const string Users = "/dashboard/prototype-users";
}
