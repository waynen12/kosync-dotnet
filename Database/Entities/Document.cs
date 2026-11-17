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

    // Every Progress Reset this Document has had, one record each (issue
    // #40) - the "delete progress" dashboard action from issue #9. A list
    // rather than the single LastResetAt timestamp this replaced, because a
    // Document can be reset more than once and each of those resets is a
    // distinct event its history has to show.
    public List<ProgressReset> ProgressResets { get; set; } = new();

    // Observed Metadata (GLOSSARY, ADR 0007): what a Device reported about
    // this one file on a progress push. Null means nothing has ever been
    // observed for it - a permanent case, not a transitional one, since
    // KOReader's "send document metadata" setting is opt-in and recent.
    //
    // These live on the Document rather than the Book deliberately: two
    // Documents on the same Book can honestly report different titles.
    public string? ObservedFilename { get; set; }

    public string? ObservedTitle { get; set; }

    // Stored verbatim as the single string KOReader sends, never parsed into
    // individual authors (ADR 0007).
    public string? ObservedAuthors { get; set; }

    // When the stored observation arrived. ADR 0007 resolves a Book with no
    // Curated Metadata to its most recent observation, and that recency has
    // to be written down at the time: the Document's last sync is not a
    // stand-in, because a Device goes on pushing progress for a file long
    // after it last reported anything about it.
    public DateTime? MetadataObservedAt { get; set; }
}
