namespace Kosync.Services;

// What a progress push is allowed to do to a Document's Observed Metadata
// (ADR 0007, issue #29). One place rather than inline in the sync endpoint,
// because the one rule that matters here is a negative - an absent
// observation must never erase a stored one - and a negative is easier to
// keep true where it is named.
public static class ObservedMetadataRule
{
    public static void Record(Document document, DocumentMetadataRequest? observed, DateTime observedAt)
    {
        if (observed is null) { return; }

        // A field KOReader could not fill is as good as unsent: it reports
        // that this device found nothing in the file, not that what another
        // device found was wrong (MetadataText).
        var filename = MetadataText.TrimmedOrNull(observed.filename);
        var title = MetadataText.TrimmedOrNull(observed.title);
        var authors = MetadataText.TrimmedOrNull(observed.authors);

        // A `metadata` object that turned out to carry nothing is no more an
        // observation than an absent one, so it does not become the
        // Document's most recent either.
        if (filename is null && title is null && authors is null) { return; }

        document.ObservedFilename = filename ?? document.ObservedFilename;
        document.ObservedTitle = title ?? document.ObservedTitle;
        document.ObservedAuthors = authors ?? document.ObservedAuthors;
        document.MetadataObservedAt = observedAt;
    }
}
