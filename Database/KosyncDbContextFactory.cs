using Microsoft.EntityFrameworkCore.Design;

namespace Kosync.Database;

// Used by `dotnet ef migrations add` at design time — the running app configures
// KosyncDbContext itself in Program.cs, this is only so the EF CLI has a context
// to work with without booting the whole host.
public class KosyncDbContextFactory : IDesignTimeDbContextFactory<KosyncDbContext>
{
    public KosyncDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<KosyncDbContext>();
        optionsBuilder.UseSqlite("Data Source=data/kosync.sqlite3");

        // One of the three places ADR 0004 lets out of the tenant filters.
        // At design time there is no request, no session and no User to scope
        // to, and `dotnet ef` only ever reads the model.
        return new KosyncDbContext(
            optionsBuilder.Options,
            TenantContext.ForSystemOperation(Constants.Tenancy.DesignTimeBypassReason));
    }
}
