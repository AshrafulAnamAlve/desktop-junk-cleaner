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
    public void ViewModels_UseCoreInterfaces_NotInfrastructureClasses()
    {
        // Only App.xaml.cs (the composition root) may know concrete Infrastructure classes.
        var offenders = RepositoryFiles.SourceFiles(Path.Combine("SafeClean.App", "ViewModels"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => x.Text.Contains("SafeClean.Infrastructure", StringComparison.Ordinal))
            .Select(x => $"{RepositoryFiles.Relative(x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "View models must not use Infrastructure directly:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void App_HasNoAccessToCleanupCode_UntilPhase7()
    {
        // Real deletion may only be connected to the UI in Phase 7, after the Phase 6 test gate.
        // Phase 7 must update this test deliberately.
        var cleanup = new Regex(@"\bICleanupService\b|\bCleanupService\b|\bHandleBasedFileDeleter\b|Infrastructure\.Cleanup\b");
        var offenders = RepositoryFiles.SourceFiles("SafeClean.App")
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => cleanup.IsMatch(x.Text))
            .Select(x => $"{RepositoryFiles.Relative(x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "The UI must not reference cleanup code yet:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void OnlyTheCleanupService_CallsTheDeletionEngine()
    {
        // The engine must always be reached through CleanupService, which refuses unsafe requests first.
        var allowed = new[]
        {
            Path.Combine("src", "SafeClean.Infrastructure", "Cleanup", "HandleBasedFileDeleter.cs"),
            Path.Combine("src", "SafeClean.Infrastructure", "Cleanup", "CleanupService.cs"),
        };

        var users = RepositoryFiles.SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("HandleBasedFileDeleter", StringComparison.Ordinal))
            .Select(RepositoryFiles.Relative)
            .ToList();

        Assert.NotEmpty(users);
        Assert.All(users, file => Assert.Contains(file, allowed, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnsafeCode_OnlyInTheInfrastructureNativeFolder()
    {
        var unsafeKeyword = new Regex(@"\bunsafe\b");
        var allowedFolder = Path.Combine("src", "SafeClean.Infrastructure", "Native") + Path.DirectorySeparatorChar;

        var offenders = RepositoryFiles.SourceFiles()
            .Where(f => !RepositoryFiles.Relative(f).StartsWith(allowedFolder, StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => !x.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && unsafeKeyword.IsMatch(x.Text))
            .Select(x => $"{RepositoryFiles.Relative(x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Unsafe code outside src/SafeClean.Infrastructure/Native:\n" + string.Join('\n', offenders));
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
