namespace Kosync.Components.Prototype;

// THROWAWAY PROTOTYPE - synthetic density, in memory only.
//
// The real database has two Devices and two Books, which is not enough to
// tell a card grid from a table: at n=2 every layout looks fine. ?demo=1
// appends these on top of the real rows so the variants can be judged at a
// plausible library size. Nothing is written to the database - this is a
// list concat, which is also why the names are obviously made up.
//
// Books carry real titles and authors because the layout decision is being
// made for the post-Phase-3 world (ADR 0007: observed/curated metadata).
// Until that ships the dashboard renders "Book #N", which would make a card
// grid look artificially bad here - 30 identical-length labels is not the
// comparison worth having.
// Delete with Components/Prototype/.
public static class PrototypeDemoData
{
    public const string DemoQueryParam = "demo";
    public const string DemoOn = "1";
    public const string DemoBadgeText = "demo density";

    private static readonly (string Name, int Docs, decimal Pct, int MinutesAgo, bool Flagged)[] DeviceSeed =
    [
        ("Kobo Libra Colour", 7, 0.62m, 6, false),
        ("Kindle Paperwhite", 4, 0.91m, 95, true),
        ("Boox Page", 2, 0.14m, 1540, false),
        ("PocketBook Verse", 5, 0.48m, 32, false),
        ("KOReader (phone)", 9, 0.77m, 3, true),
        ("Kobo Clara HD", 1, 0.05m, 27400, false),
        ("Onyx Nova Air", 3, 0.33m, 740, false),
        ("Kobo Sage", 6, 0.58m, 210, false),
        ("Kindle Oasis", 2, 0.24m, 4300, true),
        ("reMarkable 2", 1, 0.12m, 11500, false),
        ("Tolino Vision 6", 4, 0.69m, 88, false),
    ];

    private static readonly (string Title, string Author, int Docs, decimal Pct, int MinutesAgo, bool Flagged)[] BookSeed =
    [
        ("The Three-Body Problem", "Cixin Liu", 2, 0.42m, 6, true),
        ("Piranesi", "Susanna Clarke", 1, 0.98m, 95, false),
        ("A Memory Called Empire", "Arkady Martine", 3, 0.17m, 1540, true),
        ("The Dispossessed", "Ursula K. Le Guin", 1, 0.55m, 32, false),
        ("Gödel, Escher, Bach", "Douglas Hofstadter", 1, 0.03m, 3, false),
        ("Children of Time", "Adrian Tchaikovsky", 2, 0.71m, 740, true),
        ("The Left Hand of Darkness", "Ursula K. Le Guin", 1, 1.00m, 27400, false),
        ("Blindsight", "Peter Watts", 1, 0.26m, 180, false),
        ("The Name of the Wind", "Patrick Rothfuss", 4, 0.88m, 12, false),
        ("Hyperion", "Dan Simmons", 1, 0.61m, 4300, false),
        ("Annihilation", "Jeff VanderMeer", 1, 0.44m, 60, false),
        ("The City & the City", "China Miéville", 2, 0.19m, 980, true),
        ("Perdido Street Station", "China Miéville", 1, 0.07m, 15600, false),
        ("Never Let Me Go", "Kazuo Ishiguro", 1, 0.93m, 440, false),
        ("Klara and the Sun", "Kazuo Ishiguro", 1, 0.36m, 25, false),
        ("The Road", "Cormac McCarthy", 1, 1.00m, 52000, false),
        ("Blood Meridian", "Cormac McCarthy", 3, 0.51m, 7, true),
        ("Infinite Jest", "David Foster Wallace", 2, 0.11m, 2900, false),
        ("Dune", "Frank Herbert", 5, 0.74m, 48, true),
        ("Dune Messiah", "Frank Herbert", 1, 0.02m, 48, false),
        ("Shantaram", "Gregory David Roberts", 1, 0.66m, 1300, false),
        ("The Goldfinch", "Donna Tartt", 1, 0.29m, 8800, false),
        ("The Secret History", "Donna Tartt", 2, 0.85m, 115, false),
        ("Project Hail Mary", "Andy Weir", 1, 0.97m, 19, false),
        ("Exhalation", "Ted Chiang", 1, 0.38m, 3400, false),
        ("Stories of Your Life and Others", "Ted Chiang", 1, 0.63m, 620, false),
        ("The Sparrow", "Mary Doria Russell", 1, 0.08m, 36000, false),
        ("Use of Weapons", "Iain M. Banks", 2, 0.47m, 270, true),
        ("The Player of Games", "Iain M. Banks", 1, 1.00m, 41000, false),
        ("Red Mars", "Kim Stanley Robinson", 1, 0.22m, 5100, false),
        ("The Ministry for the Future", "Kim Stanley Robinson", 3, 0.56m, 9, false),
        ("Cryptonomicon", "Neal Stephenson", 1, 0.13m, 16000, false),
        ("Snow Crash", "Neal Stephenson", 1, 0.79m, 155, false),
        ("Thinking, Fast and Slow", "Daniel Kahneman", 2, 0.31m, 2100, false),
    ];

    public static IEnumerable<PrototypeDashboardItem> Devices() =>
        DeviceSeed.Select((seed, index) => Build(
            PrototypeDashboardItem.DeviceKind,
            AppRoutes.DeviceDetail(900 + index),
            seed.Name,
            $"{Services.DashboardFormatting.Pluralize(seed.Docs, "document")} synced",
            seed.Name,
            seed.Docs, seed.Pct, seed.MinutesAgo, seed.Flagged));

    public static IEnumerable<PrototypeDashboardItem> Books() =>
        BookSeed.Select((seed, index) => Build(
            PrototypeDashboardItem.BookKind,
            AppRoutes.BookDetail(900 + index),
            seed.Title,
            // Author leads the meta line, the way a curated Book will read
            // once ADR 0007's metadata lands.
            $"{seed.Author} · {Services.DashboardFormatting.Pluralize(seed.Docs, "document")} · {Services.DashboardFormatting.Percentage(seed.Pct)} {PrototypeConstants.ProgressSuffix}",
            $"{seed.Title} {seed.Author}",
            seed.Docs, seed.Pct, seed.MinutesAgo, seed.Flagged));

    private static PrototypeDashboardItem Build(
        string kind, string href, string title, string meta, string searchSeed,
        int docCount, decimal pct, int minutesAgo, bool flagged)
    {
        var docs = Enumerable.Range(0, docCount)
            .Select(i => new PrototypeDocRow(
                // Looks like the real md5 Document hashes the dashboard shows.
                Hash: Convert.ToHexString(
                    System.Security.Cryptography.MD5.HashData(
                        System.Text.Encoding.UTF8.GetBytes($"{title}-{i}"))).ToLowerInvariant(),
                Percentage: Math.Max(0m, pct - (i * 0.09m)),
                IsSplit: flagged && i > 0))
            .ToList();

        return new PrototypeDashboardItem(
            Kind: kind,
            Href: href,
            Title: title,
            Meta: meta,
            Percentage: pct,
            LastSyncedAt: DateTime.UtcNow.AddMinutes(-minutesAgo),
            IsFlagged: flagged,
            Documents: docs,
            SearchText: $"{searchSeed} {string.Join(' ', docs.Select(d => d.Hash))}");
    }
}
