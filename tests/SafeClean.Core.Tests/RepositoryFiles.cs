namespace SafeClean.Core.Tests;

/// <summary>Locates the repository's source files so guardrail tests can inspect them (read-only).</summary>
internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string SourceDirectory => Path.Combine(Root, "src");

    /// <summary>All C# and XAML files under src/, excluding build output.</summary>
    public static IReadOnlyList<string> SourceFiles(string? project = null)
    {
        var directory = project is null ? SourceDirectory : Path.Combine(SourceDirectory, project);
        return Directory
            .EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(f))
            .ToList();
    }

    public static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static bool IsBuildOutput(string path)
    {
        var parts = Path.GetRelativePath(SourceDirectory, path).Split(Path.DirectorySeparatorChar);
        return parts.Contains("bin", StringComparer.OrdinalIgnoreCase) || parts.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SafeClean.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find SafeClean.sln above the test output folder.");
    }
}
