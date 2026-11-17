using System.Net;
using System.Text.RegularExpressions;
using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #28's page: the Users administration screen, folded in from
// the prototype's UsersVariantB (a dense table plus a slide-over detail
// panel).
//
// Everything here goes over HTTP the way a browser does, because the page is
// Static SSR and its actions are form posts (ADR 0003) - so the rendered
// markup and the posts back to it *are* the page's public surface. The guards
// themselves are proven one layer down in UserAdminServiceTests; what these
// assert is that the page reaches them, shows them, and keeps its
// interactions off the server.
public class UsersPageTests : IntegrationTestBase
{
    [Fact]
    public async Task Page_IsNotThereForANonAdministrator()
    {
        await CreateSyncingUserAsync("reader", "reader-pw");

        using var client = await Factory.SignedInClientAsync("reader", "reader-pw");

        var html = await (await client.GetAsync(AppRoutes.Users)).Content.ReadAsStringAsync();

        // Not merely the absence of the actions: a non-Administrator must not
        // learn who else holds an account on this server.
        Assert.DoesNotContain(Constants.Users.PageTitle, html);
        Assert.DoesNotContain(Constants.Admin.Username, html);
        Assert.DoesNotContain($@"id=""{Constants.Users.TableId}""", html);
    }

    [Fact]
    public async Task Page_RedirectsAVisitorWithNoSessionToLogin()
    {
        using var client = Factory.NoRedirectClient();

        var response = await client.GetAsync(AppRoutes.Users);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith(AppRoutes.Login, response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Page_ListsEveryUserWithTheirRoleStatusAndLibrarySize()
    {
        await CreateSyncingUserAsync("reader", "reader-pw");
        await PushProgressAsync("reader", "reader-pw", "hash1", "device-1");

        var html = await PageAsync();

        Assert.Contains(Constants.Users.PageTitle, html);

        foreach (var column in new[]
        {
            Constants.Users.ColumnUsername,
            Constants.Users.ColumnRole,
            Constants.Users.ColumnStatus,
            Constants.Users.ColumnDevices,
            Constants.Users.ColumnBooks,
            Constants.Dashboard.ColumnLastSynced,
        })
        {
            Assert.Contains(column, html);
        }

        Assert.Contains("reader", html);
        Assert.Contains(Constants.Users.RoleAdministrator, html);
        Assert.Contains(Constants.Users.RoleUser, html);
        Assert.Contains(Constants.Users.StatusActive, html);
    }

    // A Provisional Password is the thing an Administrator most needs to see
    // on this page: that account exists and does nothing (ADR 0006), which no
    // other column would reveal.
    [Fact]
    public async Task Page_FlagsAUserWhoStillHoldsAProvisionalPassword()
    {
        await CreateAsync("reader");

        var html = await PageAsync();

        Assert.Contains(Constants.Users.PasswordProvisional, html);
    }

    // The create form, which is what makes this page more than a listing.
    // Posted the way a browser does, because a Static SSR named form has no
    // other interface.
    [Fact]
    public async Task Create_AddsAUserHoldingAProvisionalPassword()
    {
        var html = await PostCreateAsync(username: "reader", password: "handed-over");

        Assert.Contains(Constants.UserAdmin.UserCreated, html);
        Assert.Contains("reader", html);
        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
    }

    [Fact]
    public async Task Create_CanMakeTheNewUserAnAdministrator()
    {
        var html = await PostCreateAsync("second-admin", "handed-over", isAdministrator: true);

        Assert.Contains(Constants.UserAdmin.UserCreated, html);
        Assert.True(Factory.IsAdministrator("second-admin"));
    }

    [Fact]
    public async Task Create_ShowsTheRefusalForAUsernameAlreadyTaken()
    {
        await CreateAsync("reader");

        var html = await PostCreateAsync("reader", "handed-over");

        Assert.Contains(Constants.UserAdmin.UserAlreadyExists, html);
    }

    [Fact]
    public async Task ResetPassword_HandsTheUserAProvisionalPasswordAgain()
    {
        await CreateSyncingUserAsync("reader", "reader-pw");

        var html = await PostActionAsync("reader", "ResetPassword", newPassword: "handed-over");

        Assert.Contains(Constants.UserAdmin.PasswordChanged, html);
        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
    }

    [Fact]
    public async Task Deactivate_MarksTheUserInactiveAndSaysSo()
    {
        await CreateAsync("reader");

        var html = await PostActionAsync("reader", "Deactivate");

        Assert.Contains(Constants.UserAdmin.MarkedInactive, html);
        Assert.Contains(Constants.Users.StatusInactive, html);

        // ...and back, because the button the page renders for an inactive
        // User is the other one.
        var reactivated = await PostActionAsync("reader", "Reactivate");
        Assert.Contains(Constants.UserAdmin.MarkedActive, reactivated);
    }

    [Fact]
    public async Task Promote_AndThenDemote_MovesTheCapabilityAcrossAndBack()
    {
        await CreateAsync("reader");

        var promoted = await PostActionAsync("reader", "Promote");
        Assert.Contains(Constants.UserAdmin.Promoted, promoted);

        var demoted = await PostActionAsync("reader", "Demote");
        Assert.Contains(Constants.UserAdmin.Demoted, demoted);
    }

    [Fact]
    public async Task Delete_RemovesTheUser()
    {
        await CreateAsync("reader");

        var html = await PostActionAsync("reader", "Delete");

        Assert.Contains(Constants.UserAdmin.Deleted, html);
        Assert.DoesNotContain(">reader<", html);
    }

    // The guards, through the page rather than through the service: a post
    // that asks for one of them gets the refusal and nothing happens. These
    // are reachable even though the page disables the buttons - a disabled
    // attribute is a courtesy, and the post can be made without it.
    [Fact]
    public async Task Delete_YourOwnAccount_IsRefusedWithAnotherAdministratorInPlace()
    {
        await CreateAsync("second-admin");
        Factory.MakeAdministrator("second-admin");

        var html = await PostActionAsync(Constants.Admin.Username, "Delete");

        Assert.Contains(Constants.UserAdmin.SelfDeletionRefused, html);
        Assert.Contains(Constants.Admin.Username, html);
    }

    [Fact]
    public async Task Demote_YourOwnCapability_IsRefusedWithAnotherAdministratorInPlace()
    {
        await CreateAsync("second-admin");
        Factory.MakeAdministrator("second-admin");

        var html = await PostActionAsync(Constants.Admin.Username, "Demote");

        Assert.Contains(Constants.UserAdmin.SelfDemotionRefused, html);
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("Demote")]
    [InlineData("Deactivate")]
    public async Task EveryWayOfRemovingTheLastAdministrator_IsRefused(string action)
    {
        var html = await PostActionAsync(Constants.Admin.Username, action);

        Assert.Contains(Constants.UserAdmin.LastAdministratorRefused, html);
    }

    // The panel says why before the click, too - a greyed-out button with no
    // explanation is the thing this avoids.
    [Fact]
    public async Task Panel_NamesTheGuardThatWouldRefuseAndDisablesTheAction()
    {
        var html = await PageAsync();

        Assert.Contains(Constants.Users.LastAdministratorGuardReason, html);
        Assert.Matches(@"disabled[^>]*value=""Delete""|value=""Delete""[^>]*disabled", html);
    }

    [Fact]
    public async Task Page_MarksTheSignedInAdministratorsOwnRow()
    {
        var html = await PageAsync();

        Assert.Contains(Constants.Users.SelfSuffix, html);
    }

    // ADR 0003, and the acceptance criterion the ticket calls out: the two
    // interactions that are not writes must not reach the server at all.
    // Filtering is the dashboard's own client-side filter over a per-row
    // index, and opening the slide-over is a radio plus a CSS rule.
    [Fact]
    public async Task Interactions_FilterAndOpenThePanelWithoutReachingTheServer()
    {
        await CreateAsync("reader");

        var html = await PageAsync();

        Assert.Contains($@"data-ks-filter=""#{Constants.Users.TableId}""", html);
        Assert.Contains($@"id=""{Constants.Users.TableId}""", html);
        Assert.Contains("data-ks-text=", html);
        Assert.Contains(Constants.Users.NoMatchesText, html);

        // The panel's disclosure: the row links to the panel's own id, and
        // the backdrop and close button link away from it. A fragment is
        // changed by the browser, so neither direction is a request.
        var panelId = string.Format(Constants.Users.PanelIdFormat, Factory.UserIdOf("reader"));

        Assert.Contains($@"id=""{panelId}""", html);
        Assert.Contains($@"href=""{AppRoutes.UsersPanel(panelId)}""", html);
        Assert.Contains($@"href=""{AppRoutes.UsersPanelClosed}""", html);

        // Both hrefs carry the page's own path, and that is the point rather
        // than tidiness: App.razor sets <base href="/">, so a fragment-only
        // "#users-panel-3" resolves to "/#users-panel-3" - a different
        // document, which makes the click a navigation and leaves the panel
        // shut. Nor is closing a bare "#", which Chromium answered with a
        // background fetch of the page rather than as a fragment change.
        // Both found by opening the page in a browser; neither is visible
        // from here beyond these two assertions.
        Assert.DoesNotContain($@"href=""#{panelId}""", html);
        Assert.DoesNotContain(@"href=""#""", html);

        // And every one of those links opts out of enhanced navigation,
        // which is the other half of the same lesson: Blazor would otherwise
        // handle the click by pushing history itself, and a pushState does
        // not recompute `:target` - the URL would change and the panel would
        // stay shut.
        var fragmentLinks = Regex.Matches(html, @"<a[^>]*href=""[^""]*#users-panel[^""]*""[^>]*>");

        Assert.NotEmpty(fragmentLinks);
        Assert.All(fragmentLinks, link => Assert.Contains(@"data-enhance-nav=""false""", link.Value));

        // And no circuit behind any of it. An @@onclick anywhere on this page
        // would need one, which is what makes this the regression test for
        // the whole claim rather than for the markup above.
        Assert.DoesNotContain("Blazor:{\"type\":\"server\"", html);
    }

    // A badge's class is in the markup whether or not app.css defines a rule
    // for it, so the fold-in's other half needs its own test (HubClasses).
    [Fact]
    public async Task Page_RendersOnlyClassesThatAppCssDefines()
    {
        await CreateAsync("reader");
        await PostActionAsync("reader", "Deactivate");

        var rendered = HubClasses.RenderedIn(await PageAsync());

        Assert.NotEmpty(rendered);
        Assert.Empty(rendered.Except(HubClasses.DefinedInAppCss()));
    }

    // Reaching the page, which is the other half of "Administrator-only":
    // the link is rendered for an Administrator and for nobody else.
    [Fact]
    public async Task Sidebar_LinksToThePageForAnAdministratorOnly()
    {
        await CreateSyncingUserAsync("reader", "reader-pw");

        using var administrator = await Factory.SignedInClientAsync();
        var administratorDashboard = await (await administrator.GetAsync(AppRoutes.Dashboard)).Content.ReadAsStringAsync();

        using var reader = await Factory.SignedInClientAsync("reader", "reader-pw");
        var readerDashboard = await (await reader.GetAsync(AppRoutes.Dashboard)).Content.ReadAsStringAsync();

        Assert.Contains($@"href=""{AppRoutes.Users}""", administratorDashboard);
        Assert.Contains(Constants.Users.NavGroupAdministration, administratorDashboard);

        Assert.DoesNotContain($@"href=""{AppRoutes.Users}""", readerDashboard);
        Assert.DoesNotContain(Constants.Users.NavGroupAdministration, readerDashboard);
    }

    private async Task<string> PostCreateAsync(string username, string password, bool isAdministrator = false)
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await PortalForms.SubmitNamedAsync(client, AppRoutes.Users, Constants.Users.CreateFormName,
            new Dictionary<string, string>
            {
                ["CreateInput.Username"] = username,
                ["CreateInput.Password"] = password,
                ["CreateInput.IsAdministrator"] = isAdministrator ? "true" : "false",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    // One of a User's panel actions, posted to that User's own form - which
    // is how the page knows who the action is for, without trusting a field.
    private async Task<string> PostActionAsync(string username, string action, string? newPassword = null)
    {
        using var client = await Factory.SignedInClientAsync();

        var formName = string.Format(Constants.Users.ActionFormNameFormat, Factory.UserIdOf(username));

        var response = await PortalForms.SubmitNamedAsync(client, AppRoutes.Users, formName,
            new Dictionary<string, string>
            {
                ["ActionInput.Action"] = action,
                ["ActionInput.NewPassword"] = newPassword ?? string.Empty,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> PageAsync()
    {
        using var client = await Factory.SignedInClientAsync();

        var response = await client.GetAsync(AppRoutes.Users);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }

    private Task<HttpResponseMessage> CreateAsync(string username, string password = "handed-over") =>
        PostAsync(
            "/manage/users",
            new UserCreateRequest { username = username, password = password },
            Constants.Admin.Username,
            Constants.Admin.DefaultPassword);

    private Task PushProgressAsync(string username, string password, string documentHash, string deviceId) =>
        PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.42m,
            device = deviceId,
            device_id = deviceId,
        }, username, password);
}
