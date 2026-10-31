using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// Covers issue #19: the UI fold-in's last ticket, which deletes the
// throwaway prototype now that every surviving variant has been folded in.
//
// The prototype was a parallel app - its own folder, its own namespace, its
// own stylesheet and script, and a switcher page that linked them. None of
// it is referenced any more, so nothing else in the suite can notice it
// coming back; a reinstated variant would simply sit there, compiling and
// unread. These assertions are what notices.
//
// The variants themselves remain recoverable from this branch's history
// (docs/ROADMAP.md, "When folding in"), which is where the design record
// was always meant to live.
public class PrototypeRemovalTests
{
    [Fact]
    public void SourceTree_NoLongerCarriesThePrototypeFolderOrItsAssets()
    {
        Assert.False(
            RepositoryFiles.DirectoryExists("Components", "Prototype"),
            "Components/Prototype/ is the throwaway prototype and must stay deleted (issue #19).");

        // Only PrototypeSwitcher ever linked these two, so they went unread
        // the moment it did - and a stylesheet nothing links is the one place
        // a stale copy of a folded-in rule can hide.
        Assert.False(RepositoryFiles.Exists("wwwroot", "css", "prototype.css"));
        Assert.False(RepositoryFiles.Exists("wwwroot", "js", "prototype.js"));
    }

    [Fact]
    public void Assembly_CarriesNoTypesInThePrototypeNamespace()
    {
        // The folder is the namespace, so this is the compiled half of the
        // assertion above: it also fails if the components were moved rather
        // than deleted, or if a razor file was deleted and its code-behind
        // left behind.
        var prototypeTypes = typeof(Constants).Assembly
            .GetTypes()
            .Where(type => type.Namespace?.Contains("Prototype") == true)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(prototypeTypes);
    }
}
