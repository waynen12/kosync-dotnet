namespace Kosync.Database.Entities;

// One Progress Reset, recorded where it happened (GLOSSARY, issue #40):
// the reader deliberately cleared this Document's current-progress pointer
// so they could restart. Not a SyncEvent - GLOSSARY.md defines SyncEvent as
// a push from a Device, and this isn't one - so it is its own record rather
// than a flagged row in SyncEvents.
//
// A row per occurrence, replacing the single Document.LastResetAt timestamp
// it grew out of. That timestamp only ever remembered the latest reset, so a
// Document reset twice had lost the first one: the history could render only
// one marker, and ProgressRegressionRule - which clears its ceiling per
// marker - judged the push that followed the forgotten reset against a
// ceiling that reset had cleared, falsely flagging it (issue #40).
public class ProgressReset
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = default!;

    public DateTime ResetAt { get; set; }
}
