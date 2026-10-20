using System.Security.Claims;
using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kosync.Tests;

// Covers issue #25 / ADR 0004: the portal is multi-tenant, and scoping is
// enforced by EF Core global query filters rather than by threading a user
// id through every service method. The two properties worth testing are
// that the filters are on (one User never sees another's rows, by listing
// or by guessing an id) and that they fail closed (an operation that never
// set a tenant sees nothing, not everything).
public class TenantScopingTests : IntegrationTestBase
{
    [Fact]
    public async Task SyncPush_GivesTheBookItCreatesAnOwningUser()
    {
        await PushAsync("admin", "admin", "hash1", "device-1");

        using var scope = Factory.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        var admin = await db.Users.SingleAsync(u => u.Username == "admin");
        var book = await db.Books.SingleAsync();

        Assert.Equal(admin.Id, book.UserId);
    }

    // The deliberate trade in ADR 0004: setting the tenant is a step that can
    // be forgotten, so forgetting it has to yield an empty dashboard - noticed
    // in seconds - rather than everybody's library.
    [Fact]
    public async Task QueryWithNoTenantSet_ReturnsNothing()
    {
        await PushAsync("admin", "admin", "hash1", "device-1");

        // Deliberately a bare scope: nothing calls SetUser or BypassFilters.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        Assert.Empty(await db.Books.ToListAsync());
        Assert.Empty(await db.Devices.ToListAsync());
        Assert.Empty(await db.Documents.ToListAsync());
        Assert.Empty(await db.SyncEvents.ToListAsync());

        Assert.Empty(await new BookDashboardService(db).GetBooksAsync());
        Assert.Empty(await new DeviceDashboardService(db).GetDevicesAsync());
    }

    // Users are the one table deliberately left unscoped: /login has to find
    // a User by name before it knows who is logging in, and an Administrator
    // manages accounts across Users (ADR 0004). What a User row does not
    // expose is any of that User's reading.
    [Fact]
    public async Task UsersAreNotTenantScoped_SoLoginCanStillFindThem()
    {
        await CreateUserAsync("reader");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        Assert.NotNull(await db.Users.SingleOrDefaultAsync(u => u.Username == "reader"));
    }

    [Fact]
    public async Task TwoUsersDashboards_AreDisjoint()
    {
        await SeedTwoReadersAsync();

        var ownBooks = await BooksForAsync("reader-a");
        var ownDevices = await DevicesForAsync("reader-a");

        Assert.Equal(["hash-a"], ownBooks.SelectMany(b => b.Documents).Select(d => d.DocumentHash));
        Assert.Equal(["device-a"], ownDevices.Select(d => d.DeviceId));
    }

    // ADR 0004 gets authorisation on lookups for free: a filtered lookup of
    // another User's row returns null and the existing "not found" path
    // handles it. That is what makes every one of these an IDOR fix rather
    // than only a listing fix.
    [Fact]
    public async Task AnotherUsersBookId_IsNotFound()
    {
        await SeedTwoReadersAsync();
        var otherBookId = (await BooksForAsync("reader-b")).Single().Id;

        using var scope = Factory.CreateScopeForUser("reader-a");
        var service = new BookDashboardService(scope.ServiceProvider.GetRequiredService<KosyncDbContext>());

        Assert.Null(await service.GetBookAsync(otherBookId));
    }

    [Fact]
    public async Task AnotherUsersDeviceId_IsNotFound()
    {
        await SeedTwoReadersAsync();
        var otherDeviceId = (await DevicesForAsync("reader-b")).Single().Id;

        using var scope = Factory.CreateScopeForUser("reader-a");
        var service = new DeviceDashboardService(scope.ServiceProvider.GetRequiredService<KosyncDbContext>());

        Assert.Null(await service.GetDeviceAsync(otherDeviceId));
    }

    [Fact]
    public async Task ResettingAnotherUsersDocument_IsNotFound_AndLeavesTheirProgressAlone()
    {
        await SeedTwoReadersAsync();
        var otherDocumentId = (await BooksForAsync("reader-b")).Single().Documents.Single().DocumentId;

        using (var scope = Factory.CreateScopeForUser("reader-a"))
        {
            var service = new BookDashboardService(scope.ServiceProvider.GetRequiredService<KosyncDbContext>());

            Assert.Equal(ResetProgressResult.DocumentNotFound, await service.ResetProgressAsync(otherDocumentId));
        }

        using var systemScope = Factory.CreateSystemScope();
        var db = systemScope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var document = await db.Documents.SingleAsync(d => d.Id == otherDocumentId);

        Assert.Null(document.LastResetAt);
    }

    // The reason Book needed a UserId of its own. Ownership used to be
    // implicit via a Book's Documents, which left this free to merge two
    // Users' Books and physically move one User's Documents into the other's
    // Book - data corruption, not merely a disclosure.
    [Fact]
    public async Task MergingAnotherUsersBook_IsRefused_AndMovesNothing()
    {
        await SeedTwoReadersAsync();

        var ownBookId = (await BooksForAsync("reader-a")).Single().Id;
        var otherBookId = (await BooksForAsync("reader-b")).Single().Id;

        using (var scope = Factory.CreateScopeForUser("reader-a"))
        {
            var service = new BookDashboardService(scope.ServiceProvider.GetRequiredService<KosyncDbContext>());

            Assert.Equal(MergeBooksResult.BookNotFound, await service.MergeBooksAsync(ownBookId, otherBookId));
        }

        using var systemScope = Factory.CreateSystemScope();
        var db = systemScope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        Assert.NotNull(await db.Books.SingleOrDefaultAsync(b => b.Id == otherBookId));
        Assert.Equal(otherBookId, (await db.Documents.SingleAsync(d => d.DocumentHash == "hash-b")).BookId);
    }

    // The same refusal from the other direction: naming your own Book as the
    // one to merge away must not reach into somebody else's Book either.
    [Fact]
    public async Task MergingIntoAnotherUsersBook_IsRefused_AndMovesNothing()
    {
        await SeedTwoReadersAsync();

        var ownBookId = (await BooksForAsync("reader-a")).Single().Id;
        var otherBookId = (await BooksForAsync("reader-b")).Single().Id;

        using (var scope = Factory.CreateScopeForUser("reader-a"))
        {
            var service = new BookDashboardService(scope.ServiceProvider.GetRequiredService<KosyncDbContext>());

            Assert.Equal(MergeBooksResult.BookNotFound, await service.MergeBooksAsync(otherBookId, ownBookId));
        }

        using var systemScope = Factory.CreateSystemScope();
        var db = systemScope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        Assert.Equal(ownBookId, (await db.Documents.SingleAsync(d => d.DocumentHash == "hash-a")).BookId);
    }

    // ADR 0004 names this as the trap: an interactive Blazor circuit has no
    // usable HttpContext after its first render (ADR 0003), so a resolver
    // built on IHttpContextAccessor passes every integration test and then
    // resolves to "no user" inside a live circuit. This test takes the
    // circuit's position deliberately - HttpContext is null, and the
    // circuit-scoped AuthenticationStateProvider is the only thing that knows
    // who is here.
    [Fact]
    public async Task ScopingWorksFromTheCircuitsAuthenticationState_WithNoHttpContext()
    {
        await SeedTwoReadersAsync();

        using var scope = Factory.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext);
        SignInCircuitAs(scope, "reader-a");

        var scoped = await scope.ServiceProvider.GetRequiredService<PortalTenantResolver>()
            .ScopeToSignedInUserAsync();

        Assert.True(scoped);

        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        var books = await new BookDashboardService(db).GetBooksAsync();

        Assert.Equal(["hash-a"], books.SelectMany(b => b.Documents).Select(d => d.DocumentHash));
    }

    // Nobody signed in means nothing to scope to, so the tenant stays unset -
    // and under the fail-closed default that is an empty page rather than
    // everybody's library.
    [Fact]
    public async Task ScopingWithNobodySignedIn_LeavesTheTenantUnset()
    {
        await SeedTwoReadersAsync();

        using var scope = Factory.Services.CreateScope();
        SignInCircuitAs(scope, username: null);

        await AssertLeavesTheTenantUnsetAsync(scope);
    }

    // The other way the circuit can name nobody usable: a session cookie that
    // outlived the User it names, after an Administrator deleted the account.
    // Still fail-closed - there is no "nearest match" to fall back to.
    [Fact]
    public async Task ScopingAsADeletedUser_LeavesTheTenantUnset()
    {
        await SeedTwoReadersAsync();

        using var scope = Factory.Services.CreateScope();
        SignInCircuitAs(scope, "reader-who-no-longer-exists");

        await AssertLeavesTheTenantUnsetAsync(scope);
    }

    private static async Task AssertLeavesTheTenantUnsetAsync(IServiceScope scope)
    {
        var scoped = await scope.ServiceProvider.GetRequiredService<PortalTenantResolver>()
            .ScopeToSignedInUserAsync();

        Assert.False(scoped);
        Assert.Null(scope.ServiceProvider.GetRequiredService<TenantContext>().UserId);

        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();
        Assert.Empty(await new BookDashboardService(db).GetBooksAsync());
    }

    // `dotnet ef` builds the model with no request, no session and no User, so
    // the design-time factory is one of the three places allowed out of the
    // filters - and the one most likely to break silently, since nothing else
    // constructs a KosyncDbContext by hand.
    [Fact]
    public void DesignTimeFactory_BypassesTheTenantFilters()
    {
        using var db = new KosyncDbContextFactory().CreateDbContext([]);

        Assert.True(db.TenantFiltersBypassed);
        Assert.NotNull(db.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(Book.UserId)));
    }

    // Startup migrates, imports the legacy LiteDB file and seeds the first
    // admin before anybody has logged in. Every test in this suite boots the
    // app, so a broken bypass there would fail the whole suite - this pins the
    // reason why.
    [Fact]
    public void Startup_RunsWithTheTenantFiltersBypassed()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        Assert.False(db.TenantFiltersBypassed);

        KosyncDbInitializer.Initialize(
            db,
            scope.ServiceProvider.GetRequiredService<TenantContext>(),
            Factory.DataDirectory,
            scope.ServiceProvider.GetRequiredService<PasswordHasher>(),
            NullLogger.Instance);

        Assert.True(db.TenantFiltersBypassed);
        Assert.NotNull(db.Users.SingleOrDefault(u => u.Username == "admin"));
    }

    // The guards on the bypass itself. Mixing "acting for a User" and "system
    // operation" inside one scope is not something any caller legitimately
    // does, and honouring it silently would mean a query above it already ran
    // as somebody else - or as everybody.
    [Fact]
    public void BypassingTheFiltersForAUserScopedOperation_Throws()
    {
        var tenant = new TenantContext();
        tenant.SetUser(1);

        Assert.Throws<InvalidOperationException>(
            () => tenant.BypassFilters(Constants.Tenancy.TestFixtureBypassReason));
    }

    [Fact]
    public void ActingForAUserInsideABypassedScope_Throws()
    {
        var tenant = TenantContext.ForSystemOperation(Constants.Tenancy.TestFixtureBypassReason);

        Assert.Throws<InvalidOperationException>(() => tenant.SetUser(1));
    }

    [Fact]
    public void ReassigningTheTenantToADifferentUser_Throws()
    {
        var tenant = new TenantContext();
        tenant.SetUser(1);

        // Re-setting the same User is normal - a Blazor page's
        // OnInitializedAsync runs again on every navigation in its circuit.
        tenant.SetUser(1);

        Assert.Throws<InvalidOperationException>(() => tenant.SetUser(2));
    }

    // Puts the scope in the state a live circuit is in: its
    // AuthenticationStateProvider has been handed the signed-in principal,
    // and there is no HttpContext anywhere. A null username stands in for an
    // anonymous visitor.
    private static void SignInCircuitAs(IServiceScope scope, string? username)
    {
        var identity = username is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], "TestCircuit");

        var provider = (ServerAuthenticationStateProvider)
            scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();

        provider.SetAuthenticationState(
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    // Two Users, one Device and one Book each, with nothing shared but the
    // server. "reader-a" is always the one looking.
    private async Task SeedTwoReadersAsync()
    {
        await CreateUserAsync("reader-a");
        await CreateUserAsync("reader-b");

        await PushAsync("reader-a", "reader-a", "hash-a", "device-a");
        await PushAsync("reader-b", "reader-b", "hash-b", "device-b");
    }

    private async Task<List<BookSummary>> BooksForAsync(string username)
    {
        using var scope = Factory.CreateScopeForUser(username);
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new BookDashboardService(db).GetBooksAsync();
    }

    private async Task<List<DeviceSummary>> DevicesForAsync(string username)
    {
        using var scope = Factory.CreateScopeForUser(username);
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return await new DeviceDashboardService(db).GetDevicesAsync();
    }

    private Task<HttpResponseMessage> CreateUserAsync(string username)
    {
        return PostAsync("/manage/users", new UserCreateRequest { username = username, password = username }, "admin", "admin");
    }

    private Task<HttpResponseMessage> PushAsync(
        string username,
        string password,
        string documentHash,
        string deviceId,
        decimal percentage = 0.42m)
    {
        return PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = percentage,
            device = deviceId,
            device_id = deviceId,
        }, username, password);
    }
}
