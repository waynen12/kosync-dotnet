using System.Text.RegularExpressions;

namespace Kosync.Tests.TestSupport;

// The fold-in moved rules out of wwwroot/css/prototype.css, which only
// PrototypeSwitcher ever linked, into app.css, which every page loads. A
// screen that asks for a `hub-` class app.css does not define is therefore
// unstyled - and no markup assertion can see that, because the class is in
// the markup either way. These two halves are what a fold-in test compares.
//
// Still the guard now that issue #19 has deleted prototype.css: these are
// the assertions that would have caught a rule left behind in it.
public static class HubClasses
{
    public static HashSet<string> RenderedIn(string html) =>
        Regex.Matches(html, @"class=""([^""]*)""")
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(name => name.StartsWith(Prefix))
            .ToHashSet();

    public static HashSet<string> DefinedInAppCss() =>
        // The trailing lookahead matters: without it `.hub-login-card` also
        // reads as a definition of `.hub-login`, and a missing rule passes.
        Regex.Matches(StaticAssets.ReadAppCssWithoutComments(), $@"\.({Prefix}[a-z0-9-]+)(?![\w-])")
            .Select(match => match.Groups[1].Value)
            .ToHashSet();

    private const string Prefix = "hub-";
}
