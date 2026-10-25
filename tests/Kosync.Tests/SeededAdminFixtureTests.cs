using Kosync.Database.Entities;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// The contract roughly two dozen tests across this suite lean on without
// saying so: they log in as admin/admin and expect to get somewhere.
//
// On a real install that admin holds a Provisional Password and the account
// does nothing until it is replaced (ADR 0006). Leaving the fixture that way
// would mean every one of those tests failing for a reason unrelated to what
// it is testing, so the fixture's admin has already replaced theirs - and a
// test that wants the other case asks for it.
public class SeededAdminFixtureTests : IntegrationTestBase
{
    [Fact]
    public void TheFixtureAdmin_HasAlreadyReplacedTheirPassword()
    {
        Assert.Equal(PasswordState.UserChosen, StoredPasswordStateOf(Constants.Admin.Username));
    }

    // What a test exercising the forced change opts into. It is also what the
    // app really seeds, so this is the case to reach for when the behaviour
    // under test is about a Provisional Password rather than about something
    // else that needs a working login.
    [Fact]
    public void AFixtureAskedForAProvisionalAdmin_LeavesTheSeededPasswordProvisional()
    {
        using var factory = new KosyncWebApplicationFactory
        {
            AdminPasswordState = PasswordState.Provisional,
        };
        using var client = factory.CreateClient();

        Assert.Equal(PasswordState.Provisional, factory.StoredPasswordStateOf(Constants.Admin.Username));
    }
}
