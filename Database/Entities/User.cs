using System.ComponentModel.DataAnnotations.Schema;

namespace Kosync.Database.Entities;

public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = default!;

    public string PasswordHash { get; set; } = default!;

    // Whose choice PasswordHash was. Provisional by default: every route
    // that creates a User hands over a password somebody else picked, except
    // the two that don't and say so (ADR 0006).
    public PasswordState PasswordState { get; set; } = PasswordState.Provisional;

    // "This account does nothing until its password is replaced." Derived
    // rather than stored, so the answer cannot drift out of step with the
    // state it is derived from.
    //
    // Written as "anything that is not the User's own choice" rather than as
    // a list of blocking states: a PasswordState added later - the expiry
    // ADR 0006 anticipates - therefore blocks until somebody deliberately
    // exempts it here, instead of being allowed through by an enumeration
    // that nobody remembered to extend.
    [NotMapped]
    public bool MustChangePassword => PasswordState is not PasswordState.UserChosen;

    public bool IsActive { get; set; } = true;

    public bool IsAdministrator { get; set; } = false;

    public List<Book> Books { get; set; } = new();

    public List<Document> Documents { get; set; } = new();

    public List<Device> Devices { get; set; } = new();
}
