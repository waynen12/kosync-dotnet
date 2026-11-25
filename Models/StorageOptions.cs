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

    // Bound as a string and parsed below, like SyncTraceOptions.MaxBodyCharacters:
    // binding straight to an `int` throws on a mistyped value, and a typo in
    // a retention setting must not be a worse outage than the disk it is
    // there to protect (issue #50).
    public string? MaxBackupsRetained { get; set; }

    // What the post-backup prune in DatabaseBackup keeps. Not a number at
    // all falls back to the shipped default; anything below the minimum
    // means the same as the minimum, so a configured 0 cannot prune away
    // the backup that was just taken.
    public int ResolvedMaxBackupsRetained
    {
        get
        {
            if (!int.TryParse(MaxBackupsRetained?.Trim(), out int configured))
            {
                return Constants.Storage.DefaultMaxBackupsRetained;
            }

            return Math.Max(Constants.Storage.MinimumMaxBackupsRetained, configured);
        }
    }
}
