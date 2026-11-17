using System.Text.RegularExpressions;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #15: the hub design-system foundation in wwwroot/css/app.css.
//
// These assert on the stylesheet as a text artifact rather than on rendered
// markup, because the things they protect are stylesheet-level decisions that
// nothing else can catch:
//
//   - Every rule in the file reads the hub tokens. Three tests here used to
//     protect the opposite: a block of pre-hub aliases that kept BookDetail
//     and DeviceDetail alive, unprototyped, through the fold-in. Issue #39
//     rebuilt both pages on the primitives, so the aliases went and those
//     three tests went with them - they existed to guard the survivor. What
//     replaces them is below: the prefix is gone, and gone for good, and
//     the rules the two pages now depend on are present.
//   - A page whose rules have been deleted still renders its classes, so a
//     view test cannot see this - which is the whole reason the aliases were
//     kept rather than risked in the first place.
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

    // What the three retired alias tests become. The pre-hub prefix is not
    // merely unused - it is absent, comments included, which is issue #39's
    // stated finish line (`grep -c -- '--ks-\|\.ks-'` is 0). Asserted on the
    // raw file rather than the comment-stripped one for exactly that reason:
    // prose explaining the prefix would keep teaching it.
    [Fact]
    public void AppCss_CarriesNoPreHubTokensOrRulesAtAll()
    {
        var css = StaticAssets.ReadAppCss();

        Assert.DoesNotMatch(@"--ks-", css);
        Assert.DoesNotMatch(@"\.ks-", css);
    }

    // And every token the file does read is defined in it. The aliases were
    // the one family that could go missing while the rules reading them
    // stayed - but the general rule is what outlives them, and it catches a
    // `var(--hub-...)` typo as well as a deleted block.
    [Fact]
    public void AppCss_DefinesEveryHubTokenItReferences()
    {
        var css = StaticAssets.ReadAppCss();

        var referenced = TokenNames(css, @"var\((--hub-[a-z0-9-]+)");
        var defined = TokenNames(css, @"(--hub-[a-z0-9-]+)\s*:");

        Assert.NotEmpty(referenced);
        Assert.Empty(referenced.Except(defined));
    }

    // The detail pages' own rules, which replaced the deleted ones. A view
    // test cannot catch their absence: BookDetail would render every class
    // below and simply look wrong, which is the failure mode the aliases
    // were kept to avoid and this is the guard that replaces them.
    [Theory]
    [InlineData("hub-detail")] // the page container, replacing its own
    [InlineData("hub-back")] // the way back to the list
    [InlineData("hub-hash")] // a Document's full hash, in the drawer
    [InlineData("hub-doc-key")] // D1/D2/D3, keyed by position not colour
    [InlineData("hub-doc-label")] // the observed filename, truncating
    [InlineData("hub-aside")] // the hint, both warnings and the errors
    [InlineData("hub-confirm")] // the two-press guard on reset and merge
    [InlineData("hub-drawer")] // the metadata and merge disclosures
    [InlineData("hub-row-toggle")] // the row expander
    [InlineData("hub-row-drawer")] // what it expands into
    [InlineData("hub-event")] // one SyncEvent in that drawer
    public void AppCss_StylesTheRulesTheDetailPagesRender(string className)
    {
        var css = StaticAssets.ReadAppCssWithoutComments();

        // The lookahead is what makes a failure mean the right thing:
        // without it `.hub-row-drawer-cell` reads as a definition of
        // `.hub-row-drawer`, and `.hub-aside-danger` as one of `.hub-aside`.
        Assert.Matches($@"\.{Regex.Escape(className)}(?![\w-])", css);
    }

    [Theory]
    [InlineData(1024)] // the 768px tablet target
    [InlineData(640)] // the 390px phone target
    public void AppCss_NarrowsTheGutterAtEachBreakpoint(int maxWidth)
    {
        var css = StaticAssets.ReadAppCssWithoutComments();

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
        // Comments are stripped first: section 4's prose quotes
        // `margin: 0 auto` to explain the rule, and a naive scan folds that
        // text into the next selector.
        var css = StaticAssets.ReadAppCssWithoutComments();

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
        // the requirement issue #15 actually makes - that HubIcon was not
        // under Components/Prototype/, which issue #19 deleted wholesale.
        // PrototypeRemovalTests now guards the folder's absence outright;
        // this one is still the record of where HubIcon had to end up.
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
}
