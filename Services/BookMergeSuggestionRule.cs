namespace Kosync.Services;

// Which of a User's other Books look like the same Book as this one
// (ADR 0007, issue #32). A *suggestion* only: nothing here merges anything,
// because MergeBooksAsync is transactional but not undoable, so a false
// positive is manual cleanup rather than an undo.
//
// The rule is deliberately the narrowest one that could be useful - an exact
// match, case-insensitive and trimmed, on both title and author, among Books
// that have metadata at all. Title-only matching pairs up books in a series
// and anything called "Collected Stories"; fuzzy matching is its own project
// and should be decided on real data gathered from this exact rule. The
// roadmap deferred automatic matching precisely because false positives were
// too expensive, so near-zero false positives is what earns this the right to
// exist.
public static class BookMergeSuggestionRule
{
    public static List<int> Suggest(Book book, IEnumerable<Book> otherBooks)
    {
        if (Identity(book) is not BookIdentity identity)
        {
            return [];
        }

        return otherBooks
            .Where(other => other.Id != book.Id && IsTheSameBook(identity, other))
            .Select(other => other.Id)
            .OrderBy(id => id)
            .ToList();
    }

    // What has to be equal for two Books to be the same Book. Both fields
    // required: a Book with a title and no author is a title-only pairing
    // waiting to happen, and a Book with neither has nothing to compare at
    // all - its `Book #N` label is not a title it has (BookMetadataRule).
    private sealed record BookIdentity(int UserId, string Title, string Author);

    private static BookIdentity? Identity(Book book) =>
        BookMetadataRule.ResolveTitleOrNull(book) is string title
        && BookMetadataRule.ResolveAuthor(book) is string author
            ? new BookIdentity(book.UserId, title, author)
            : null;

    // What the rule is willing to call one Book wearing two ids.
    //
    // The UserId comparison is not this rule doing its own tenant scoping -
    // ADR 0004 puts that in the global query filters, and this rule is handed
    // whatever they let through. It is here because a suggestion that crossed
    // Users would propose moving someone else's Documents, which is the one
    // mistake a reader cannot undo, so the rule declines to form one even
    // from Books it should never have been given.
    private static bool IsTheSameBook(BookIdentity identity, Book otherBook) =>
        Identity(otherBook) is BookIdentity other
        && other.UserId == identity.UserId
        && Equivalent(other.Title, identity.Title)
        && Equivalent(other.Author, identity.Author);

    // Case-insensitive; the trimming already happened in MetadataText, which
    // treats surrounding whitespace as an artefact of a file's metadata or a
    // text box rather than part of the title.
    private static bool Equivalent(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
