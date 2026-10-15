using System.Text.RegularExpressions;
using SafeClean.Core.Models;

namespace SafeClean.Core.Tests;

/// <summary>Core holds the safety rules, so it must stay pure: no UI, no Infrastructure, no file access.</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] ForbiddenAssemblies =
    [
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Windows.Forms",
        "SafeClean.Infrastructure",
        "SafeClean", // the App assembly
    ];

    [Fact]
    public void Core_DoesNotReferenceUiOrInfrastructureAssemblies()
    {
        var referenced = typeof(CleanupMode).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.All(ForbiddenAssemblies, forbidden => Assert.DoesNotContain(forbidden, referenced));
    }

    [Fact]
    public void CoreProjectFile_HasNoProjectOrPackageReferences()
    {
        var projectFile = File.ReadAllText(Path.Combine(RepositoryFiles.SourceDirectory, "SafeClean.Core", "SafeClean.Core.csproj"));

        Assert.DoesNotContain("<ProjectReference", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<PackageReference", projectFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoreSource_DoesNotAccessTheFileSystem()
    {
        var fileAccess = new Regex(@"\b(File|Directory|FileStream|FileInfo|DirectoryInfo|DriveInfo|FileSystemInfo)\s*[\.(]");
        var files = RepositoryFiles.SourceFiles("SafeClean.Core");
        Assert.NotEmpty(files);

        var offenders = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => fileAccess.IsMatch(x.Text))
            .Select(x => $"{RepositoryFiles.Relative(x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Core must not access the file system:\n" + string.Join('\n', offenders));
    }
}
