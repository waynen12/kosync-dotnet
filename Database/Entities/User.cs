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
    // rather than stored, so that a PasswordState added later cannot be
    // introduced without deciding here whether it blocks.
    [NotMapped]
    public bool MustChangePassword => PasswordState is not PasswordState.UserChosen;

    public bool IsActive { get; set; } = true;

    public bool IsAdministrator { get; set; } = false;

    public List<Book> Books { get; set; } = new();

    public List<Document> Documents { get; set; } = new();

    public List<Device> Devices { get; set; } = new();
}
