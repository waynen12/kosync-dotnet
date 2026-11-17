namespace Kosync.Tests.TestSupport;

// Some contracts are facts about the repository rather than about a running
// app: a stylesheet's text, a script's text, or - for issue #19 - a folder
// that has to stay deleted. KosyncWebApplicationFactory points the content
// root at a private temp directory, so none of it is reachable over HTTP.
// This walks up from the test binary to the repository root instead.
public static class RepositoryFiles
{
    public static bool Exists(params string[] segments) => File.Exists(PathTo(segments));

    public static bool DirectoryExists(params string[] segments) => Directory.Exists(PathTo(segments));

    public static string ReadAllText(params string[] segments) => File.ReadAllText(PathTo(segments));

    private static string PathTo(params string[] segments) => Path.Combine([RepositoryRoot, .. segments]);

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ProjectFileName)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate {ProjectFileName} above {AppContext.BaseDirectory}.");
        }
    }

    private const string ProjectFileName = "Kosync.csproj";
}
