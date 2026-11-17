using System.Net;
using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #26: every User logs into the portal with their sync
// credentials, not only an Administrator (ADR 0004). Administrator becomes a
// capability - managing other Users - rather than a precondition for seeing
// your own reading.
//
// Everything here goes through HTTP the way a browser does: scrape the Static
// SSR form, post it back, then follow the cookie. That is deliberate. The
// tenancy this ticket leans on is already proven one layer down in
// TenantScopingTests; what was never provable before is the whole path, since
// until now no non-Administrator could obtain a session to walk it with.
public class PortalLoginForEveryUserTests : IntegrationTestBase
{
    [Fact]
    public async Task NonAdministrator_CanLogIntoThePortal()
    {
        await CreateSyncingUserAsync("reader", PasswordFor("reader"));

        using var client = Factory.NoRedirectClient();

        var response = await DashboardLogin.LogInAsync(client, "reader", PasswordFor("reader"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(AppRoutes.Dashboard, response.Headers.Location!.PathAndQuery);
        DashboardLogin.AssertIssuedASessionCookie(response);
    }

    // The point of the ticket, not merely the absence of a refusal: a session
    // that renders an empty dashboard would satisfy every "sees none of
    // another User's data" assertion below and still be useless.
    [Fact]
    public async Task NonAdministrator_SeesTheirOwnDevicesAndBooks()
    {
        await SeedTwoReadersAsync();

        var devices = await PortalPageAsync("reader-a", AppRoutes.Dashboard);
        var books = await PortalPageAsync("reader-a", AppRoutes.DashboardBooksTab);

        Assert.Contains("Device A", devices);
        Assert.DoesNotContain(Constants.NoDevicesSyncedText, devices);

        // A Book has no title of its own yet, so its Document's hash is what
        // identifies it on screen - in the row's filter index, since issue #18
        // took the hash column away (DashboardViewTests).
        Assert.Matches(@"data-ks-text=""[^""]*hash-a[^""]*""", books);
        Assert.DoesNotContain(Constants.NoBooksSyncedText, books);
    }

    // The acceptance criterion this ticket could not be allowed to land
    // without, and the reason it was gated behind issue #25: /login requiring
    // an Administrator was the only thing standing between a second User and
    // everybody's library, so a session for one User must render nothing
    // belonging to the other.
    [Fact]
    public async Task TwoUsersPortalDashboards_AreDisjoint()
    {
        await SeedTwoReadersAsync();

        // Both sections, because each identifies a row by something the other
        // does not show: a Device by its name, a Book only by its Document's
        // hash. A test over one section would leave the other unproven.
        foreach (var route in new[] { AppRoutes.Dashboard, AppRoutes.DashboardBooksTab })
        {
            var ownPage = await PortalPageAsync("reader-a", route);
            var otherPage = await PortalPageAsync("reader-b", route);

            // Each User's own row is on their own page - so neither of the
            // two negative assertions below can pass on an empty dashboard.
            Assert.Contains("hash-a", ownPage);
            Assert.Contains("hash-b", otherPage);

            // And the other User's is absent, Device name and Document hash
            // alike. The hash is worth naming separately: it reaches the page
            // as the row's filter index rather than as a column (issue #18),
            // so a leak there renders invisibly.
            Assert.DoesNotContain("Device B", ownPage);
            Assert.DoesNotContain("hash-b", ownPage);

            Assert.DoesNotContain("Device A", otherPage);
            Assert.DoesNotContain("hash-a", otherPage);
        }
    }

    // The one precondition this ticket does not drop. IsAdministrator went
    // because it was about capability; IsActive stays because it is about
    // whether the account grants anything at all - and it is now the only
    // flag on the sign-in path, so nothing else would catch its loss.
    [Fact]
    public async Task InactiveUser_StillCannotLogIn()
    {
        await CreateSyncingUserAsync("reader", PasswordFor("reader"));

        var deactivated = await PutAsync<object?>(
            "/manage/users/active?username=reader",
            null,
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);
        deactivated.EnsureSuccessStatusCode();

        using var client = Factory.NoRedirectClient();

        var response = await DashboardLogin.LogInAsync(client, "reader", PasswordFor("reader"));

        // The form comes back with its error rather than a redirect, and
        // nothing is issued to carry a session.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains(Constants.LoginErrorText, await response.Content.ReadAsStringAsync());
    }

    // ADR 0006's gate, now reached by the account it was always meant for: a
    // User an Administrator has just created, holding the Provisional
    // Password that Administrator chose. Until this ticket only the seeded
    // admin could get far enough to be gated
    // (ProvisionalPasswordPortalGateTests), which left the common case
    // untested.
    //
    // The sign-in itself succeeds, deliberately. Refusing it would leave such
    // a User with no way into the one page that lets them fix it.
    [Fact]
    public async Task NewUserWithAProvisionalPassword_LogsInButReachesOnlyChangePassword()
    {
        // Not CreateSyncingUserAsync: that helper takes a User past the forced
        // change, which is exactly what is under test here.
        var created = await PostAsync(
            "/manage/users",
            new UserCreateRequest { username = "reader", password = PasswordFor("reader") },
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);
        created.EnsureSuccessStatusCode();

        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));

        using var client = Factory.NoRedirectClient();

        var login = await DashboardLogin.LogInAsync(client, "reader", PasswordFor("reader"));

        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        DashboardLogin.AssertIssuedASessionCookie(login);

        // Signed in, and still no library: the gate turns the dashboard into
        // the change-password page.
        var dashboard = await client.GetAsync(AppRoutes.Dashboard);

        Assert.Equal(HttpStatusCode.Found, dashboard.StatusCode);
        Assert.Equal(AppRoutes.ChangePassword, dashboard.Headers.Location!.OriginalString);

        // And that page is reachable, so the redirect is not a loop.
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(AppRoutes.ChangePassword)).StatusCode);
    }

    // Disjoint listings are only half of "sees none of another User's data".
    // The other half is the drill-ins, which take an id straight from the URL
    // - so a User who guesses the neighbouring integer must land on the
    // same "not found" page a nonexistent id gives, with none of the Device's
    // name, the Document's hash or the progress behind it.
    [Fact]
    public async Task NonAdministrator_GuessingAnotherUsersIds_SeesNotFound()
    {
        await SeedTwoReadersAsync();
        var (otherDeviceId, otherBookId) = await IdsOwnedByAsync("reader-b");

        var devicePage = await PortalPageAsync("reader-a", AppRoutes.DeviceDetail(otherDeviceId));
        var bookPage = await PortalPageAsync("reader-a", AppRoutes.BookDetail(otherBookId));

        Assert.Contains(Constants.DeviceNotFoundText, devicePage);
        Assert.DoesNotContain("Device B", devicePage);
        Assert.DoesNotContain("hash-b", devicePage);

        Assert.Contains(Constants.BookNotFoundText, bookPage);
        Assert.DoesNotContain("hash-b", bookPage);
    }

    // Administrator stops being a precondition for logging in without
    // becoming any easier to acquire.
    //
    // The request carries both credentials a non-Administrator can now
    // present at once: the portal cookie this ticket lets them obtain, and
    // the x-auth-user header /manage/* actually authenticates from. Both,
    // deliberately - a cookie alone would be refused for having no header at
    // all, which is the same 401 an anonymous caller gets and would prove
    // nothing. Authenticated as themselves and holding a session, they are
    // still refused, and the refusal can only be about capability.
    [Theory]
    [InlineData("/manage/users")]
    [InlineData("/manage/users/documents?username=reader-b")]
    public async Task AnAuthenticatedNonAdministrator_IsRefusedAdministratorSurfaces(string route)
    {
        await SeedTwoReadersAsync();

        using var client = await Factory.SignedInClientAsync("reader-a", PasswordFor("reader-a"));

        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.SetCredentials("reader-a", PasswordFor("reader-a"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("hash-b", await response.Content.ReadAsStringAsync());
    }

    // The same credentials reaching the surface they do grant: proof the 401
    // above is about Administrator and not about the header being rejected,
    // which would make the pair pass for the wrong reason.
    [Fact]
    public async Task TheSameCredentials_StillReachTheirOwnDocuments()
    {
        await SeedTwoReadersAsync();

        var response = await GetAsync(
            "/manage/users/documents?username=reader-a", "reader-a", PasswordFor("reader-a"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("hash-a", await response.Content.ReadAsStringAsync());
    }

    // Reader-b's own ids, read as reader-b, purely to arrange the URLs
    // reader-a then guesses. The seam under test is still the HTTP request
    // above - this is the part of the arrangement no endpoint can supply,
    // precisely because the ids are not disclosed to anybody else.
    private async Task<(int DeviceId, int BookId)> IdsOwnedByAsync(string username)
    {
        using var scope = Factory.CreateScopeForUser(username);
        var db = scope.ServiceProvider.GetRequiredService<KosyncDbContext>();

        return (await db.Devices.Select(d => d.Id).SingleAsync(),
            await db.Books.Select(b => b.Id).SingleAsync());
    }

    // Two Users, one Device and one Book each, with nothing shared but the
    // server. "reader-a" is always the one looking.
    private async Task SeedTwoReadersAsync()
    {
        foreach (var username in new[] { "reader-a", "reader-b" })
        {
            await CreateSyncingUserAsync(username, PasswordFor(username));
        }

        await PushAsync("reader-a", "hash-a", "device-a", "Device A");
        await PushAsync("reader-b", "hash-b", "device-b", "Device B");
    }

    // One home for the convention, rather than the seeding spelling a
    // password out and the sign-in helper deriving the same string again -
    // two forms of one fact, with nothing keeping them in step.
    private static string PasswordFor(string username) => $"{username}-pw";

    private Task<HttpResponseMessage> PushAsync(
        string username, string documentHash, string deviceId, string deviceName)
    {
        return PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.42m,
            device = deviceName,
            device_id = deviceId,
        }, username, PasswordFor(username));
    }

    // The rendered page, fetched over a cookie session belonging to the named
    // User - the whole path this ticket opens, rather than a scope reached
    // into directly.
    private async Task<string> PortalPageAsync(string username, string route)
    {
        using var client = await Factory.SignedInClientAsync(username, $"{username}-pw");

        var response = await client.GetAsync(route);

        // A redirect here means the gate turned them away, and the assertions
        // that follow would read an empty body as a clean dashboard.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
