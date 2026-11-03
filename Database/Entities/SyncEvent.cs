namespace Kosync.Database.Entities;

public class SyncEvent
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public int DeviceId { get; set; }
    public Device Device { get; set; } = default!;

    public string Progress { get; set; } = default!;

    public decimal Percentage { get; set; }

    public DateTime Timestamp { get; set; }

    // The Document's current-progress pointer, and nothing more: exactly one
    // SyncEvent per Document holds it. Set when this push was at least as far
    // along as the then-current progress, and cleared again the moment a
    // later push supersedes it.
    //
    // So false does *not* mean this push was a Progress Regression - most of
    // a healthy Document's history is false (GLOSSARY.md, issue #38). Whether
    // a push was blocked when it arrived is ProgressRegressionRule's answer,
    // replayed over the history; nothing stores it.
    public bool IsCurrent { get; set; }
}
