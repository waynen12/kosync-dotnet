namespace Kosync.Models;

// The optional `metadata` object KOReader ≥ 2026.07 adds to a progress push
// when "Send document metadata" is enabled (ADR 0007). Lower-cased to match
// the wire names, like the rest of the KOReader-facing models.
//
// Every field is optional because the whole object is: a client that has the
// setting switched off omits `metadata` entirely rather than sending nulls,
// and nothing stops a future client sending only some of it.
public class DocumentMetadataRequest
{
    public string? filename { get; set; }

    public string? title { get; set; }

    // A single string, not a list - KOReader joins multiple authors itself
    // and we store whatever it sent, verbatim (ADR 0007).
    public string? authors { get; set; }
}
