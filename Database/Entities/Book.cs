namespace Kosync.Database.Entities;

public class Book
{
    public int Id { get; set; }

    // Every Book belongs to exactly one User (GLOSSARY "User"). Ownership
    // used to be implicit via the Book's Documents, which left MergeBooksAsync
    // free to merge two Users' Books and physically move one User's Documents
    // into the other's Book - and a global query filter needs a column of its
    // own to filter on (ADR 0004).
    public int UserId { get; set; }
    public User User { get; set; } = default!;

    // Curated Metadata (GLOSSARY, ADR 0007): the title and author this
    // Book's owning User set by hand. Null means nobody has, which is the
    // normal case - a Book's title is otherwise derived from what its
    // Documents observed, or falls back to `Book #N`.
    //
    // These live on the Book rather than the Document because a person
    // typing a title is making a statement about the Book, not a report
    // about one of its files - which is also why a progress push must never
    // write here.
    public string? CuratedTitle { get; set; }

    public string? CuratedAuthor { get; set; }

    public List<Document> Documents { get; set; } = new();
}
