namespace Kosync.Services;

// One answer to "what is a blank title worth?", shared by everything that
// stores or reads book metadata: nothing. A field nobody filled in is not a
// value, so it never becomes an Observed title, never becomes a Curated one,
// and never beats a value that is actually there.
//
// Trimming goes with it because the surrounding whitespace is an artefact -
// of a file's own metadata, or of a text box - and not part of the title.
public static class MetadataText
{
    public static string? TrimmedOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
