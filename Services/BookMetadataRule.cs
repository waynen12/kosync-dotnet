namespace Kosync.Services;

// How a Book's displayed title and author resolve (ADR 0007, issue #30):
// Curated Metadata, then the most recent Observed Metadata across the Book's
// Documents, then `Book #N`. One place rather than inline in each view,
// because the precedence is the rule - a page that re-derives it is a page
// that can disagree with the others.
//
// Takes a Book with its Documents loaded: the observed tier reads them.
public static class BookMetadataRule
{
    public static string ResolveTitle(Book book) =>
        ResolveTitleOrNull(book)
        ?? string.Format(Constants.BookFallbackNameFormat, book.Id);

    // The same resolution without the `Book #N` fallback, for a caller that
    // has to tell a title apart from the absence of one. `Book #N` is a label
    // for a Book that knows nothing about itself, not a title it has - and
    // BookMergeSuggestionRule has to not match on it.
    public static string? ResolveTitleOrNull(Book book) =>
        MetadataText.TrimmedOrNull(book.CuratedTitle)
        ?? MostRecentObservation(book, d => d.ObservedTitle);

    public static string? ResolveAuthor(Book book) =>
        MetadataText.TrimmedOrNull(book.CuratedAuthor)
        ?? MostRecentObservation(book, d => d.ObservedAuthors);

    // Whether this Book should carry the "enable Send document metadata"
    // hint (ADR 0007, issue #31). Two conditions, and each one is the hint's
    // own wording held to: it says "no title yet", so a Book with a curated
    // title has nothing to be hinted about - a curated *author* does not
    // count, because it leaves the Book still called `Book #N`. And it says
    // to enable a setting, so it is wrong the moment any Document observed
    // anything: that reader's devices are already sending metadata, and a
    // file that carried no title is not a setting they can switch on.
    //
    // MetadataObservedAt is the test for "observed anything", because
    // ObservedMetadataRule only stamps it where a push actually carried a
    // value - so a Book that saw nothing but empty `metadata` objects still
    // gets the hint, which is right: that reader's devices are sending
    // nothing either.
    public static bool NeedsMetadataHint(Book book) =>
        MetadataText.TrimmedOrNull(book.CuratedTitle) is null
        && book.Documents.All(d => d.MetadataObservedAt is null);

    // The most recent observation wins, deliberately *not* the
    // furthest-along Document that decides the Book's progress: progress is
    // a quantity where further is better, while there is no sense in which a
    // title from a further-read file is more correct (ADR 0007).
    //
    // Resolved per field, so a Book whose newest observation carried a title
    // but no author still shows an author an older Document reported - the
    // alternative is hiding a fact nothing has contradicted.
    //
    // Id breaks a tie on the timestamp: two Documents observed in the same
    // instant have no "more recent" between them, and a Book's title must
    // not depend on which row the database hands back first.
    private static string? MostRecentObservation(Book book, Func<Document, string?> field) =>
        book.Documents
            .Where(d => d.MetadataObservedAt is not null)
            .OrderByDescending(d => d.MetadataObservedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => MetadataText.TrimmedOrNull(field(d)))
            .FirstOrDefault(observation => observation is not null);
}
