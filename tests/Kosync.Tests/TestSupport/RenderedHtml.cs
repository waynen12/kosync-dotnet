using System.Text.RegularExpressions;

namespace Kosync.Tests.TestSupport;

public static class RenderedHtml
{
    // How many times a flag or label appears on a page. A count rather than
    // a Contains, because the interesting failures for an inline flag are
    // "shown where it shouldn't be" and "shown on everything" - and both of
    // those pass a Contains (issue #38).
    public static int Occurrences(string html, string text) =>
        html.Split(text).Length - 1;

    // The drill-ins' expanded rows, with the collapsed rows above them left
    // out (issue #39). A Document's SyncEvent history is in here; the flag
    // rolled up from that history onto the row is not - so "the history
    // flags exactly one push" is a count against this, and bumping the
    // whole-page count instead would have quietly folded the two together.
    public static string RowDrawers(string html) =>
        string.Concat(DrawerRows(html).Select(match => match.Value));

    // The other half: everything that is not a drawer. Where a rolled-up
    // flag, a Split Book badge or a page header lives.
    public static string CollapsedRows(string html) =>
        DrawerRows(html).Aggregate(html, (rest, match) => rest.Replace(match.Value, ""));

    // A drawer is `<tr hidden ...>` to its first `</tr>`: these tables nest
    // no rows inside a row, so the first close is the right one.
    private static IEnumerable<Match> DrawerRows(string html) =>
        Regex.Matches(html, @"<tr[^>]*\bhidden\b.*?</tr>", RegexOptions.Singleline);
}
