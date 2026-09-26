namespace Kosync.Database.Legacy;

public class LegacyUser
{
    public int Id { get; set; }

    public string Username { get; set; } = default!;

    public string PasswordHash { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public bool IsAdministrator { get; set; } = false;

    public List<LegacyDocument> Documents { get; set; } = new();
}

public class LegacyDocument
{
    public string DocumentHash { get; set; } = default!;

    public string Progress { get; set; } = default!;

    public decimal Percentage { get; set; } = default!;

    public string Device { get; set; } = default!;

    public string DeviceId { get; set; } = default!;

    public DateTime Timestamp { get; set; } = default!;
}
