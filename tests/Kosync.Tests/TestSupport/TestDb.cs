using Kosync.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests.TestSupport;

// Every read through KosyncDbContext is scoped to a User (ADR 0004), so a
// test reaching into the database directly has to say whose view it is
// taking. These are the two answers: "whatever is actually stored", for
// tests asserting on the data, and "this User's view", for tests exercising
// the scoping itself or predating it.
public static class TestDb
{
    // A context that can see every row, the way startup code does.
    public static KosyncDbContext CreateSystemContext(DbContextOptions<KosyncDbContext> options)
    {
        return new KosyncDbContext(
            options,
            TenantContext.ForSystemOperation(Constants.Tenancy.TestFixtureBypassReason));
    }

    public static IServiceScope CreateSystemScope(this KosyncWebApplicationFactory factory)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>()
            .BypassFilters(Constants.Tenancy.TestFixtureBypassReason);

        return scope;
    }

    // A scope that sees exactly what the named User sees - the same view the
    // dashboard takes. Throws if the User does not exist, so a typo surfaces
    // as a failure rather than as a mysteriously empty result.
    public static IServiceScope CreateScopeForUser(this KosyncWebApplicationFactory factory, string username)
    {
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var userId = db.Users.Single(u => u.Username == username).Id;

        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(userId);

        return scope;
    }
}
