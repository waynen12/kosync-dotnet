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

    public List<Document> Documents { get; set; } = new();
}
