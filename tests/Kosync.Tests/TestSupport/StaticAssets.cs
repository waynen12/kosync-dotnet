namespace Kosync.Tests.TestSupport;

// KosyncWebApplicationFactory deliberately points the content root at a
// private temp directory, so UseStaticFiles() cannot serve wwwroot in tests.
// Stylesheet-contract tests therefore read the file from the repository
// instead of over HTTP.
public static class StaticAssets
{
    private const string ProjectFileName = "Kosync.csproj";

    public static string ReadAppCss() => File.ReadAllText(Path.Combine(WwwRoot, "css", "app.css"));

    private static string WwwRoot => Path.Combine(RepositoryRoot, "wwwroot");

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
}
