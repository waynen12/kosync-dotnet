namespace Kosync.Models;

// Bound from the "Storage" configuration section, so where the database
// lives on disk is a deployment decision (Storage__DataDirectory in a
// container) rather than the two hardcoded spellings - one in Program.cs,
// one in the design-time KosyncDbContextFactory - that this replaces
// (issue #47). See Constants.Storage.DefaultDataDirectory.
//
// Deliberately *not* also written into the shipped appsettings.json. A value
// there would win over this initializer in production while the tests, whose
// content root has no appsettings.json, went on exercising the constant - so
// editing the JSON would change where every install's database lives with
// the suite still green. One default, in one place, is the whole point
// (see PortalOptions, which this follows exactly).
public class StorageOptions
{
    public string DataDirectory { get; set; } = Constants.Storage.DefaultDataDirectory;

    // Storage__DataDirectory= in a compose file binds an empty string, not a
    // null, so the property initializer's default never gets a look in. A
    // blank value falls back here rather than resolving to the content root
    // itself, which would move an existing install's database out of its
    // "data" subdirectory into a directory shared with the application's own
    // files.
    public string ResolvedDataDirectory =>
        string.IsNullOrWhiteSpace(DataDirectory) ? Constants.Storage.DefaultDataDirectory : DataDirectory;
}
