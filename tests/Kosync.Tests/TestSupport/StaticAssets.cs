using System.Text.RegularExpressions;

namespace Kosync.Tests.TestSupport;

// KosyncWebApplicationFactory deliberately points the content root at a
// private temp directory, so UseStaticFiles() cannot serve wwwroot in tests.
// Stylesheet-contract tests therefore read the file from the repository
// instead of over HTTP - see RepositoryFiles for the walk up to it.
public static class StaticAssets
{
    public static string ReadAppCss() => RepositoryFiles.ReadAllText(WwwRoot, "css", "app.css");

    public static string ReadThemeJs() => RepositoryFiles.ReadAllText(WwwRoot, "js", "theme.js");

    // theme.js is half prose - the enhancedload hook and the toggle-contents
    // rule are both long comments naming the very things a scan looks for.
    public static string ReadThemeJsWithoutComments() =>
        Regex.Replace(ReadThemeJs(), @"//.*$", "", RegexOptions.Multiline);

    // Same reasoning for any other script under test, and both comment forms
    // this time: the scripts here explain themselves in /* */ headers.
    public static string ReadJsWithoutComments(string fileName)
    {
        var js = RepositoryFiles.ReadAllText(WwwRoot, "js", fileName);

        return Regex.Replace(
            Regex.Replace(js, @"/\*.*?\*/", "", RegexOptions.Singleline),
            @"//.*$", "", RegexOptions.Multiline);
    }

    // Every scan of the stylesheet wants this: app.css's section prose names
    // the selectors and tokens it is explaining, and a naive scan reads that
    // prose as a rule.
    public static string ReadAppCssWithoutComments() =>
        Regex.Replace(ReadAppCss(), @"/\*.*?\*/", "", RegexOptions.Singleline);

    private const string WwwRoot = "wwwroot";
}
