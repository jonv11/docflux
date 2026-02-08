namespace DocFlux.Core.Tests;

internal static class TestPathHelper
{
    public static string RepoRoot { get; } = ResolveRepoRoot();

    public static string FixturePath(params string[] segments)
    {
        return Path.Combine(
            [RepoRoot, "tests", "DocFlux.Core.Tests", "Fixtures", .. segments]);
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DocFlux.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to resolve repository root from test execution directory.");
    }
}
