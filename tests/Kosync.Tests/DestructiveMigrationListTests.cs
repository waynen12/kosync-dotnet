using Kosync.Database;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kosync.Tests;

// Constants.Migrations.Destructive is maintained by hand (ADR 0009: EF Core
// can say which migrations are pending, nothing in it can say whether one
// deletes rows). This is the test that catches the failure that matters: a
// typo or a rename in the list silently disables the gate, and that is the
// failure that costs data. No database needed - migration ids are assembly
// metadata.
public class DestructiveMigrationListTests
{
    [Fact]
    public void EveryDeclaredId_ResolvesToARealMigrationInTheAssembly()
    {
        using var db = TestDb.CreateSystemContext(
            new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite("Data Source=:memory:").Options);

        var everyMigrationId = db.Database.GetMigrations().ToHashSet();

        foreach (var declaredId in Constants.Migrations.Destructive)
        {
            Assert.Contains(declaredId, everyMigrationId);
        }
    }

    // Declaring the same migration twice would make an operator's single
    // ALLOW_DESTRUCTIVE_MIGRATIONS entry look like it covers two refusals
    // instead of one - a copy-paste mistake worth catching here rather than
    // the hard way.
    [Fact]
    public void TheListHasNoDuplicates()
    {
        Assert.Equal(Constants.Migrations.Destructive.Length, Constants.Migrations.Destructive.Distinct().Count());
    }
}
