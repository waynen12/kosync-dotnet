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

        return new KosyncDbContext(optionsBuilder.Options);
    }
}
