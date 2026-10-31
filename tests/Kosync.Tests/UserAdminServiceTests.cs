using Kosync.Database.Entities;
using Kosync.Models;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Kosync.Tests;

// Covers issue #28: UserAdminService, the one place Users are administered
// from. Both the portal's Users page and /manage/* call these methods, so
// this is where the guards ADR 0006 describes are asserted - the controller
// tests then only have to show that the endpoints reach them.
//
// The service is resolved from a scope with *no* tenant set, which is the
// arrangement the real page runs in (it reads no library of its own). A
// per-User count that forgot to say it crosses Users therefore comes back 0
// here rather than passing on a bypass the page does not have.
public class UserAdminServiceTests : IntegrationTestBase
{
    [Fact]
    public async Task Users_AreListedWithTheirRoleAndPasswordState()
    {
        await CreateSyncingUserAsync("reader", "reader-password");

        var users = await Listed();

        var admin = users.Single(a => a.Username == Constants.Admin.Username);
        Assert.True(admin.IsAdministrator);
        Assert.True(admin.IsActive);

        var reader = users.Single(a => a.Username == "reader");
        Assert.False(reader.IsAdministrator);
        Assert.True(reader.IsActive);
        Assert.False(reader.HasProvisionalPassword);
    }

    // The counts are the whole of what this page shows about a library, and
    // they are a cross-User read by design (ADR 0004) - so they have to come
    // back from a scope that has no tenant, which is every scope this page
    // runs in.
    [Fact]
    public async Task Users_AreCountedAgainstTheirLibraryWithoutATenantBeingSet()
    {
        await CreateSyncingUserAsync("reader", "reader-password");
        await PushProgressAsync("reader", "reader-password", "hash1", "device-1");
        await PushProgressAsync("reader", "reader-password", "hash2", "device-2");

        var reader = (await Listed()).Single(a => a.Username == "reader");

        Assert.Equal(2, reader.DeviceCount);
        Assert.Equal(2, reader.BookCount);
        Assert.Equal(2, reader.DocumentCount);
        Assert.NotNull(reader.LastSyncedAt);
    }

    [Fact]
    public async Task Users_WhoHaveNotSyncedShowAsHavingNothing()
    {
        await Create("reader");

        var reader = (await Listed()).Single(a => a.Username == "reader");

        Assert.Equal(0, reader.DeviceCount);
        Assert.Equal(0, reader.BookCount);
        Assert.Null(reader.LastSyncedAt);
    }

    // An Administrator typed this password and is about to hand it over, so
    // it grants the new User nothing until they replace it (ADR 0006).
    [Fact]
    public async Task Create_GivesTheNewUserAProvisionalPassword()
    {
        var result = await Create("reader");

        Assert.True(result.Succeeded);
        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
        Assert.True((await Listed()).Single(a => a.Username == "reader").HasProvisionalPassword);
    }

    [Fact]
    public async Task Create_StoresThePasswordAsACredentialTheNewUserCanAuthenticateWith()
    {
        await Create("reader", "handed-over");

        // Past the forced change, because the Provisional Password refuses
        // sync (ADR 0006) - what is asserted here is that the stored
        // credential is the one that was typed in, not what blocks it.
        Factory.SetPasswordState("reader", PasswordState.UserChosen);

        var response = await GetAsync("/users/auth", "reader", "handed-over");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_RefusesAUsernameThatIsAlreadyTaken()
    {
        await Create("reader");

        var result = await Create("reader");

        Assert.Equal(UserAdminOutcome.UsernameTaken, result.Outcome);
        Assert.Equal(Constants.UserAdmin.UserAlreadyExists, result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_RefusesABlankUsername(string? username)
    {
        var result = await WithService(service =>
            service.CreateAsync(AdminId, username, "handed-over", isAdministrator: false));

        Assert.Equal(UserAdminOutcome.UsernameRequired, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Create_RefusesABlankPassword(string? password)
    {
        var result = await WithService(service =>
            service.CreateAsync(AdminId, "reader", password, isAdministrator: false));

        Assert.Equal(UserAdminOutcome.PasswordRequired, result.Outcome);
        Assert.DoesNotContain(await Listed(), a => a.Username == "reader");
    }

    // The only way back to a User is an exact match on the username, so a
    // name stored with the space somebody typed in front of it is a User
    // nobody can find by reading the screen.
    [Fact]
    public async Task Create_TrimsTheUsername()
    {
        await Create(" alice ");

        Assert.Contains(await Listed(), u => u.Username == "alice");
    }

    // ADR 0006: an Administrator can create another Administrator directly,
    // rather than creating a User and then promoting them.
    [Fact]
    public async Task Create_CanMakeTheNewUserAnAdministrator()
    {
        await WithService(service =>
            service.CreateAsync(AdminId, "second-admin", "handed-over", isAdministrator: true));

        Assert.True((await Listed()).Single(a => a.Username == "second-admin").IsAdministrator);
    }

    // A password an Administrator sets for somebody else is exactly what a
    // Provisional Password is, however the account came to exist - so a
    // reset raises that state again even for a User who had already replaced
    // their initial password (ADR 0006).
    [Fact]
    public async Task ResetPassword_HandsAnotherUserAProvisionalPassword()
    {
        await CreateSyncingUserAsync("reader", "reader-password");

        var result = await WithService(service =>
            service.ResetPasswordAsync(AdminId, "reader", "handed-over"));

        Assert.True(result.Succeeded);
        Assert.Equal(PasswordState.Provisional, Factory.StoredPasswordStateOf("reader"));
    }

    // The distinction that replaced the blanket "cannot update admin user"
    // refusal: a password you set for yourself is your own choice, and
    // nothing forces you to replace it (ADR 0006).
    [Fact]
    public async Task ResetPassword_SettingYourOwnMakesItYourOwnChoice()
    {
        var result = await WithService(service =>
            service.ResetPasswordAsync(AdminId, Constants.Admin.Username, "my-own-password"));

        Assert.True(result.Succeeded);
        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf(Constants.Admin.Username));
    }

    [Fact]
    public async Task ResetPassword_RefusesABlankPassword()
    {
        await Create("reader");

        var result = await WithService(service => service.ResetPasswordAsync(AdminId, "reader", "   "));

        Assert.Equal(UserAdminOutcome.PasswordRequired, result.Outcome);
    }

    // PasswordPolicy binds where a User picks their own password (ADR 0006),
    // and setting your own through this service is picking your own.
    [Fact]
    public async Task ResetPassword_AppliesThePasswordPolicyToYourOwnPassword()
    {
        var result = await WithService(service =>
            service.ResetPasswordAsync(AdminId, Constants.Admin.Username, "short"));

        Assert.Equal(UserAdminOutcome.PasswordRejected, result.Outcome);
        Assert.Equal(PasswordState.UserChosen, Factory.StoredPasswordStateOf(Constants.Admin.Username));
    }

    // ...and deliberately does not bind the Provisional Password an
    // Administrator hands over, which exists to be replaced.
    [Fact]
    public async Task ResetPassword_DoesNotApplyThePolicyToAPasswordHandedOver()
    {
        await Create("reader");

        var result = await WithService(service => service.ResetPasswordAsync(AdminId, "reader", "short"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ResetPassword_RefusesAUserWhoDoesNotExist()
    {
        var result = await WithService(service => service.ResetPasswordAsync(AdminId, "ghost", "handed-over"));

        Assert.Equal(UserAdminOutcome.UserNotFound, result.Outcome);
    }

    [Fact]
    public async Task Promote_GrantsTheAdministratorCapability()
    {
        await Create("reader");

        var result = await SetAdministrator("reader", isAdministrator: true);

        Assert.True(result.Succeeded);
        Assert.True((await Listed()).Single(a => a.Username == "reader").IsAdministrator);
    }

    [Fact]
    public async Task Demote_RevokesTheAdministratorCapability()
    {
        await CreateAdministrator("second-admin");

        var result = await SetAdministrator("second-admin", isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.False((await Listed()).Single(a => a.Username == "second-admin").IsAdministrator);
    }

    // The first of the two guards this ticket is really about. Another
    // Administrator exists, so the server would not be left without one -
    // and it is still refused, because an Administrator who demotes
    // themselves cannot undo it and cannot see who could.
    [Fact]
    public async Task Demote_RefusesYourOwnCapabilityEvenWithAnotherAdministratorInPlace()
    {
        await CreateAdministrator("second-admin");

        var result = await SetAdministrator(Constants.Admin.Username, isAdministrator: false);

        Assert.Equal(UserAdminOutcome.SelfDemotionRefused, result.Outcome);
        Assert.True((await Listed()).Single(a => a.Username == Constants.Admin.Username).IsAdministrator);
    }

    // The second guard, and the reason it is checked ahead of the self-rules:
    // on a server with one Administrator every way of removing them is
    // somebody acting on themselves, so "ask another Administrator" would be
    // advice to ask nobody. The refusal has to name the real obstacle.
    [Fact]
    public async Task Demote_RefusesTheLastAdministrator()
    {
        var result = await SetAdministrator(Constants.Admin.Username, isAdministrator: false);

        Assert.Equal(UserAdminOutcome.LastAdministratorRefused, result.Outcome);
    }

    [Fact]
    public async Task Demote_RefusesTheLastActiveAdministratorEvenWithAnInactiveOneInPlace()
    {
        await CreateAdministrator("spare-admin");
        await WithService(service => service.SetActiveAsync(AdminId, "spare-admin", isActive: false));

        var result = await SetAdministrator(Constants.Admin.Username, isAdministrator: false);

        // Not the self-rule: an inactive Administrator cannot log in, so
        // leaving only that account is leaving the server with nobody.
        Assert.Equal(UserAdminOutcome.LastAdministratorRefused, result.Outcome);
    }

    [Fact]
    public async Task Deactivate_RefusesYourOwnAccount()
    {
        await CreateAdministrator("second-admin");

        var result = await WithService(service =>
            service.SetActiveAsync(AdminId, Constants.Admin.Username, isActive: false));

        Assert.Equal(UserAdminOutcome.SelfDeactivationRefused, result.Outcome);
        Assert.True((await Listed()).Single(a => a.Username == Constants.Admin.Username).IsActive);
    }

    [Fact]
    public async Task Deactivate_RefusesTheLastAdministrator()
    {
        var result = await WithService(service =>
            service.SetActiveAsync(AdminId, Constants.Admin.Username, isActive: false));

        Assert.Equal(UserAdminOutcome.LastAdministratorRefused, result.Outcome);
    }

    [Fact]
    public async Task Deactivate_AllowsAnotherAdministratorWhileOneRemains()
    {
        await CreateAdministrator("second-admin");

        var result = await WithService(service =>
            service.SetActiveAsync(AdminId, "second-admin", isActive: false));

        Assert.True(result.Succeeded);
        Assert.False((await Listed()).Single(a => a.Username == "second-admin").IsActive);
    }

    // The shape /manage/users/active has always had: it flips the state
    // rather than setting it, and says which way it went.
    [Fact]
    public async Task ToggleActive_FlipsTheStateAndSaysWhichWayItWent()
    {
        await Create("reader");

        var deactivated = await WithService(service => service.ToggleActiveAsync(AdminId, "reader"));
        Assert.Equal(Constants.UserAdmin.MarkedInactive, deactivated.Message);

        var reactivated = await WithService(service => service.ToggleActiveAsync(AdminId, "reader"));
        Assert.Equal(Constants.UserAdmin.MarkedActive, reactivated.Message);
        Assert.True((await Listed()).Single(a => a.Username == "reader").IsActive);
    }

    [Fact]
    public async Task Delete_RemovesTheUser()
    {
        await Create("reader");

        var result = await WithService(service => service.DeleteAsync(AdminId, "reader"));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(await Listed(), a => a.Username == "reader");
    }

    // Deleting a User takes their library with them, which is what the
    // cascades in KosyncDbContext are for. Asserted through the counts of
    // the Users who remain, because a leftover Book belonging to nobody is
    // invisible from any other angle.
    [Fact]
    public async Task Delete_TakesTheUsersLibraryWithThem()
    {
        await CreateSyncingUserAsync("reader", "reader-password");
        await PushProgressAsync("reader", "reader-password", "hash1", "device-1");

        await WithService(service => service.DeleteAsync(AdminId, "reader"));

        Assert.Equal(0, Factory.CountOfEveryStoredRow<Book>());
        Assert.Equal(0, Factory.CountOfEveryStoredRow<Document>());
        Assert.Equal(0, Factory.CountOfEveryStoredRow<Device>());
        Assert.Equal(0, Factory.CountOfEveryStoredRow<SyncEvent>());
    }

    [Fact]
    public async Task Delete_RefusesYourOwnAccount()
    {
        await CreateAdministrator("second-admin");

        var result = await WithService(service => service.DeleteAsync(AdminId, Constants.Admin.Username));

        Assert.Equal(UserAdminOutcome.SelfDeletionRefused, result.Outcome);
        Assert.Contains(await Listed(), a => a.Username == Constants.Admin.Username);
    }

    [Fact]
    public async Task Delete_RefusesTheLastAdministrator()
    {
        var result = await WithService(service => service.DeleteAsync(AdminId, Constants.Admin.Username));

        Assert.Equal(UserAdminOutcome.LastAdministratorRefused, result.Outcome);
    }

    [Fact]
    public async Task Delete_RefusesAUserWhoDoesNotExist()
    {
        var result = await WithService(service => service.DeleteAsync(AdminId, "ghost"));

        Assert.Equal(UserAdminOutcome.UserNotFound, result.Outcome);
    }

    // The backstop that makes this service safe to re-expose over HTTP: both
    // callers authorize for themselves, and it refuses anyway.
    [Fact]
    public async Task EveryOperation_RefusesACallerWhoIsNotAnActiveAdministrator()
    {
        await CreateSyncingUserAsync("reader", "reader-password");
        var readerId = Factory.UserIdOf("reader");

        var outcomes = new[]
        {
            await WithService(service => service.CreateAsync(readerId, "other", "handed-over", false)),
            await WithService(service => service.ResetPasswordAsync(readerId, "reader", "chosen-password")),
            await WithService(service => service.SetActiveAsync(readerId, Constants.Admin.Username, false)),
            await WithService(service => service.ToggleActiveAsync(readerId, Constants.Admin.Username)),
            await WithService(service => service.SetAdministratorAsync(readerId, "reader", true)),
            await WithService(service => service.DeleteAsync(readerId, Constants.Admin.Username)),
        };

        Assert.All(outcomes, result =>
            Assert.Equal(UserAdminOutcome.NotAnAdministrator, result.Outcome));
    }

    [Fact]
    public async Task EveryOperation_RefusesAnAdministratorWhoseAccountIsInactive()
    {
        await CreateAdministrator("second-admin");
        var secondAdminId = Factory.UserIdOf("second-admin");
        await WithService(service => service.SetActiveAsync(AdminId, "second-admin", isActive: false));

        var result = await WithService(service =>
            service.CreateAsync(secondAdminId, "reader", "handed-over", false));

        Assert.Equal(UserAdminOutcome.NotAnAdministrator, result.Outcome);
    }

    private const string DefaultProvisionalPassword = "handed-over";

    private Task<UserAdminResult> CreateAdministrator(string username) =>
        WithService(service =>
            service.CreateAsync(AdminId, username, DefaultProvisionalPassword, isAdministrator: true));

    private Task<UserAdminResult> SetAdministrator(string username, bool isAdministrator) =>
        WithService(service => service.SetAdministratorAsync(AdminId, username, isAdministrator));

    private Task<UserAdminResult> Create(string username, string password = DefaultProvisionalPassword) =>
        WithService(service => service.CreateAsync(AdminId, username, password, isAdministrator: false));

    private async Task<IReadOnlyList<UserSummary>> Listed() =>
        await WithService(service => service.GetUsersAsync());

    private int AdminId => Factory.UserIdOf(Constants.Admin.Username);

    // One scope per call, the way a request is one scope: a test that
    // mutates and then reads must not be reading its own change out of a
    // DbContext's identity map.
    private async Task<T> WithService<T>(Func<UserAdminService, Task<T>> use)
    {
        using var scope = Factory.Services.CreateScope();

        return await use(scope.ServiceProvider.GetRequiredService<UserAdminService>());
    }

    private Task PushProgressAsync(string username, string password, string documentHash, string deviceId) =>
        PutAsync("/syncs/progress", new DocumentRequest
        {
            document = documentHash,
            progress = "p1",
            percentage = 0.5m,
            device = deviceId,
            device_id = deviceId,
        }, username, password);
}
