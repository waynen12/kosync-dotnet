namespace Kosync.Database.Entities;

// A durable fact about this server, keyed by name - deliberately not
// configuration. Configuration arrives fresh on every boot from env vars and
// appsettings and therefore cannot remember anything; these rows are what the
// server carries across a restart, which is the only thing that can make a
// once-only action actually happen once (Constants.ServerSettings).
//
// Belongs to no User, so the tenant filters leave it alone - see
// KosyncDbContext.ApplyTenantFilters.
public class ServerSetting
{
    public string Key { get; set; } = default!;

    public string Value { get; set; } = default!;
}
