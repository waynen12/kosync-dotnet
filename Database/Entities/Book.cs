namespace Kosync.Database.Entities;

public class Book
{
    public int Id { get; set; }

    public List<Document> Documents { get; set; } = new();
}
