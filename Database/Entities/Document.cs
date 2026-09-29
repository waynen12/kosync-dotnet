namespace Kosync.Database.Entities;

public class Document
{
    public int Id { get; set; }

    public string DocumentHash { get; set; } = default!;

    public int UserId { get; set; }
    public User User { get; set; } = default!;

    public int BookId { get; set; }
    public Book Book { get; set; } = default!;

    public List<SyncEvent> SyncEvents { get; set; } = new();

    // Set by the "delete progress" dashboard action (issue #9): when this
    // Document last had its current-progress pointer deliberately cleared
    // so the reader could restart. Not a SyncEvent - CONTEXT.md defines
    // SyncEvent as a push from a Device, and this isn't one - but it still
    // needs to be visible in the Document's history as the distinct event
    // it is.
    public DateTime? LastResetAt { get; set; }
}
