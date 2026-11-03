namespace Kosync.Tests.TestSupport;

public static class RenderedHtml
{
    // How many times a flag or label appears on a page. A count rather than
    // a Contains, because the interesting failures for an inline flag are
    // "shown where it shouldn't be" and "shown on everything" - and both of
    // those pass a Contains (issue #38).
    public static int Occurrences(string html, string text) =>
        html.Split(text).Length - 1;
}
