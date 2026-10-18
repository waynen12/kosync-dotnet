using System.Text.RegularExpressions;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #15: the hub design-system foundation in wwwroot/css/app.css.
//
// These assert on the stylesheet as a text artifact rather than on rendered
// markup, because the things they protect are stylesheet-level decisions that
// nothing else can catch:
//
//   - BookDetail and DeviceDetail were never prototyped. They render 21
//     `.ks-*` classes whose rules read `var(--ks-*)`, so deleting the
//     `--ks-*` token block leaves both pages unstyled (docs/ROADMAP.md,
//     "The detail pages were never prototyped"). A render test still passes
//     when that happens - the classes are there, the variables are not.
//   - `margin: 0 auto` on a flex item's cross axis makes it size to content
//     instead of stretching, which centred the dashboard in a narrow column
//     at every width. The fix is one `width: 100%` per container, and it
//     only regresses by someone adding a container without it.
public class DesignSystemTests
{
    [Fact]
    public void AppCss_DefinesTheHubTokenBlock()
    {
        var css = StaticAssets.ReadAppCss();

        // Literals on purpose: docs/ROADMAP.md ("Design language") names
        // #F57C00 and --radius 0.625rem as the parity values, so the spec is
        // the independent source of truth these are checked against. Deriving
        // them from the stylesheet would make the test agree by construction.
        Assert.Contains("--hub-brand: #f57c00", css);
        Assert.Contains("--hub-gradient:", css);
        Assert.Contains("--hub-radius: 0.625rem", css);

        // The ramp has to be defined for both themes - kosync switches on
        // [data-theme="dark"], not ai-hub's .dark class.
        Assert.Matches(@":root\s*\{[^}]*--hub-bg:", css);
        Assert.Matches(@":root\[data-theme=""dark""\]\s*\{[^}]*--hub-bg:", css);
    }

    [Fact]
    public void AppCss_StillDefinesEveryKsTokenItReferences()
    {
        var css = StaticAssets.ReadAppCss();

        var referenced = TokenNames(css, @"var\((--ks-[a-z0-9-]+)");
        var defined = TokenNames(css, @"(--ks-[a-z0-9-]+)\s*:");

        Assert.NotEmpty(referenced);
        Assert.Empty(referenced.Except(defined));
    }

    [Fact]
    public void AppCss_ResolvesEveryKsTokenThroughAHubToken()
    {
        var css = StaticAssets.ReadAppCss();

        // Aliases, not a second palette: a `--ks-*` definition whose value
        // carries no `--hub-*` reference means the old palette survived the
        // fold-in and the detail pages are off-brand.
        var standalone = Regex.Matches(css, @"(--ks-[a-z0-9-]+)\s*:\s*([^;]+);")
            .Where(match => !match.Groups[2].Value.Contains("--hub-"))
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.Empty(standalone);
    }

    [Theory]
    [InlineData(1024)] // the 768px tablet target
    [InlineData(640)] // the 390px phone target
    public void AppCss_NarrowsTheGutterAtEachBreakpoint(int maxWidth)
    {
        var css = WithoutComments(StaticAssets.ReadAppCss());

        // Asserting the breakpoint does something, not merely that an @media
        // block exists: every page-level horizontal padding reads
        // --hub-gutter, so re-declaring it is what re-gutters the app.
        var breakpoint = Regex.Match(css, $@"@media\s*\(\s*max-width:\s*{maxWidth}px\s*\)\s*\{{(.*?)\n\}}",
            RegexOptions.Singleline);

        Assert.True(breakpoint.Success, $"app.css has no (max-width: {maxWidth}px) breakpoint.");
        Assert.Contains("--hub-gutter:", breakpoint.Groups[1].Value);
    }

    [Fact]
    public void AppCss_GivesEveryAutoMarginContainerAFullWidth()
    {
        // Comments are stripped first: section 5's prose quotes
        // `margin: 0 auto` to explain the rule, and a naive scan folds that
        // text into the next selector.
        var css = WithoutComments(StaticAssets.ReadAppCss());

        var offenders = Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}")
            .Where(rule => Regex.IsMatch(rule.Groups[2].Value, @"margin:\s*0\s+auto"))
            .Where(rule => !Regex.IsMatch(rule.Groups[2].Value, @"width:\s*100%"))
            .Select(rule => rule.Groups[1].Value.Trim())
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HubIcon_LivesOutsideTheThrowawayPrototypeFolder()
    {
        // The folder is the namespace, so referencing the type by its Shared
        // namespace is itself the compile-time proof; the assertion states
        // the requirement issue #15 actually makes - that HubIcon is not
        // under Components/Prototype/, which issue #19 deletes wholesale.
        var location = typeof(Components.Shared.HubIcon).Namespace;

        Assert.DoesNotContain("Prototype", location);
    }

    [Fact]
    public void HubIcon_RendersEveryIconNameItPublishes()
    {
        // Icon names are a constant vocabulary rather than call-site string
        // literals (CLAUDE.md), so an unrenderable name is a build error at
        // the call site instead of a silently empty <svg> in the page.
        var published = typeof(Constants.Icons)
            .GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(published);
        Assert.All(published, name => Assert.NotEmpty(Components.Shared.HubIconPaths.For(name)));
    }

    private static HashSet<string> TokenNames(string css, string pattern) =>
        Regex.Matches(css, pattern).Select(match => match.Groups[1].Value).ToHashSet();

    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
}
