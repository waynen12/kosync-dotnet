using Kosync.Database;
using Kosync.Database.Entities;
using Kosync.Services;
using Kosync.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kosync.Tests;

// ADMIN_PASSWORD is seed-only (ADR 0006): it sets the admin's password when
// the account is created and never again. It used to be reapplied on every
// startup, which would have silently reverted any password change on the
// next restart.
//
// A restart is exactly "KosyncDbInitializer.Initialize runs again", so these
// tests call it twice against one in-memory database rather than standing up
// the whole app twice.
public class AdminPasswordSeedingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dataDirectory;

    // Both are read straight from the process environment, so an ambient
    // value in the shell or on CI would otherwise decide what these tests
    // boot with.
    private readonly EnvironmentVariableScope _noAmbientAdminPassword;
    private readonly EnvironmentVariableScope _noAmbientReset;

    public AdminPasswordSeedingTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dataDirectory = Directory.CreateTempSubdirectory("kosync-seeding-tests-").FullName;

        _noAmbientAdminPassword = AdminPasswordOf(null);
        _noAmbientReset = AdminPasswordResetOf(null);
    }

    [Fact]
    public void FirstBoot_SeedsTheAdminWithAdminPassword()
    {
        using var _ = AdminPasswordOf("seeded-password");

        Boot();

        Assert.True(AdminPasswordIs("seeded-password"));
    }

    [Fact]
    public void FirstBoot_WithNoAdminPassword_FallsBackToTheDefault()
    {
        Boot();

        Assert.True(AdminPasswordIs(Constants.Admin.DefaultPassword));
    }

    // The installer chose it, not the admin - so it is Provisional, and the
    // account does nothing until it is replaced.
    [Fact]
    public void FirstBoot_SeedsTheAdminWithAProvisionalPassword()
    {
        Boot();

        Assert.Equal(PasswordState.Provisional, AdminPasswordState());
        Assert.True(TheAdmin().MustChangePassword);
    }

    [Fact]
    public void Restart_DoesNotReapplyAdminPassword_SoAChangedPasswordSurvives()
    {
        using var _ = AdminPasswordOf("seeded-password");

        Boot();
        TheAdminChangesTheirPasswordTo("the-users-own-password");

        // ADMIN_PASSWORD is still set, the way it stays set in a compose file.
        Boot();

        Assert.True(AdminPasswordIs("the-users-own-password"));
        Assert.False(AdminPasswordIs("seeded-password"));
    }

    // Recovery from a genuinely lost admin password. It deliberately does not
    // return the admin to a working credential: the recovery password is
    // Provisional, so all it can do is replace itself (ADR 0006).
    [Fact]
    public void AdminPasswordReset_ReturnsTheAdminToAProvisionalPassword()
    {
        using var _ = AdminPasswordOf("seeded-password");

        Boot();
        TheAdminChangesTheirPasswordTo("since-forgotten");

        using var reset = AdminPasswordResetOf("recover-once");
        Boot();

        Assert.True(AdminPasswordIs("seeded-password"));
        Assert.Equal(PasswordState.Provisional, AdminPasswordState());
    }

    // The variable stays set in the compose file that triggered it, so every
    // later restart would otherwise reset the password again - which is the
    // very behaviour ADR 0006 removed from ADMIN_PASSWORD.
    [Fact]
    public void AdminPasswordReset_IsOneShot_SoTheNextRestartLeavesThePasswordAlone()
    {
        using var _ = AdminPasswordOf("seeded-password");
        using var reset = AdminPasswordResetOf("recover-once");

        Boot();
        TheAdminChangesTheirPasswordTo("chosen-after-recovery");

        Boot();

        Assert.True(AdminPasswordIs("chosen-after-recovery"));
        Assert.Equal(PasswordState.UserChosen, AdminPasswordState());
    }

    // How an operator who has lost the password twice recovers twice: the
    // variable is honoured once per distinct value.
    [Fact]
    public void AdminPasswordReset_WithANewValue_RecoversAgain()
    {
        using var _ = AdminPasswordOf("seeded-password");

        using (AdminPasswordResetOf("recover-once"))
        {
            Boot();
        }

        TheAdminChangesTheirPasswordTo("since-forgotten-again");

        using var secondReset = AdminPasswordResetOf("recover-twice");
        Boot();

        Assert.True(AdminPasswordIs("seeded-password"));
        Assert.Equal(PasswordState.Provisional, AdminPasswordState());
    }

    private static EnvironmentVariableScope AdminPasswordOf(string? password)
    {
        return new EnvironmentVariableScope(Constants.EnvironmentVariables.AdminPassword, password);
    }

    private static EnvironmentVariableScope AdminPasswordResetOf(string? value)
    {
        return new EnvironmentVariableScope(Constants.EnvironmentVariables.AdminPasswordReset, value);
    }

    // One startup pass, with the same unbypassed TenantContext the container
    // hands Program - Initialize is the thing that declares the bypass.
    private void Boot()
    {
        var tenant = new TenantContext();
        using var db = new KosyncDbContext(Options, tenant);

        KosyncDbInitializer.Initialize(db, tenant, _dataDirectory, Hasher, NullLogger.Instance);
    }

    // What completing the forced change does: a new credential, now the
    // User's own choice.
    private void TheAdminChangesTheirPasswordTo(string password)
    {
        using var db = TestDb.CreateSystemContext(Options);

        var admin = db.Users.Single(u => u.Username == Constants.Admin.Username);
        admin.PasswordHash = Hasher.HashPlainText(password);
        admin.PasswordState = PasswordState.UserChosen;

        db.SaveChanges();
    }

    private bool AdminPasswordIs(string password)
    {
        return Hasher.VerifyPlainText(password, TheAdmin().PasswordHash);
    }

    private PasswordState AdminPasswordState() => TheAdmin().PasswordState;

    private User TheAdmin()
    {
        using var db = TestDb.CreateSystemContext(Options);

        return db.Users.Single(u => u.Username == Constants.Admin.Username);
    }

    // A fresh hasher each time, so a verified-credential cache entry can never
    // stand in for the stored hash these tests are asserting on.
    private static PasswordHasher Hasher => TestPasswords.CreateHasher();

    private DbContextOptions<KosyncDbContext> Options =>
        new DbContextOptionsBuilder<KosyncDbContext>().UseSqlite(_connection).Options;

    public void Dispose()
    {
        _noAmbientReset.Dispose();
        _noAmbientAdminPassword.Dispose();

        _connection.Dispose();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
