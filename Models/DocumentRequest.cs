namespace Kosync.Models;

public class DocumentRequest
{
    public string document { get; set; } = default!;

    public string progress { get; set; } = default!;

    public decimal percentage { get; set; } = default!;

    public string device { get; set; } = default!;

    public string device_id { get; set; } = default!;

    // Null whenever the client did not send a `metadata` object at all -
    // which is every stock KOReader older than 2026.07, and every newer one
    // with the opt-in setting off. Absent means "this client sent none", not
    // "this file has none", so it never clears what is already stored.
    public DocumentMetadataRequest? metadata { get; set; }
}