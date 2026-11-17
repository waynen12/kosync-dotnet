namespace Kosync.Services;

// What a Document is called on screen (ADR 0007, issue #31): the filename a
// Device observed for it, or a truncated hash where no Device ever reported
// one. A Document *is* a file, so its filename is the most honest name for
// it - and unlike a Book, a Document has no curated tier and no `#N`
// fallback, because its hash is always there to name it with.
//
// Detail pages only. The dashboard's table shows a Document *count* and
// labels no individual Document, so there is nothing there for a filename to
// replace (docs/ROADMAP.md, "The dashboard stops listing documents").
public static class DocumentLabelRule
{
    public static string Resolve(Document document) =>
        MetadataText.TrimmedOrNull(document.ObservedFilename)
        ?? Truncate(document.DocumentHash);

    // A label, not an identifier: the full hash is rendered beside it on both
    // detail pages, so this one only has to be recognisable - enough leading
    // characters to match against the hash KOReader showed you, and a suffix
    // so nobody reads it as a hash they could search for.
    private static string Truncate(string documentHash) =>
        documentHash.Length <= Constants.DocumentLabel.TruncatedHashLength
            ? documentHash
            : documentHash[..Constants.DocumentLabel.TruncatedHashLength]
                + Constants.DocumentLabel.TruncationSuffix;
}
