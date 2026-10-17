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
    public void App_ReachesCleanupOnlyThroughTheServiceInterface()
    {
        // Phase 7 (after the Phase 6 gate) connected cleanup to the UI — deliberately, and only like this:
        // view models use ICleanupService (Core); only the composition root names the concrete CleanupService;
        // nothing in the App names the deletion engine.
        var compositionRoot = Path.Combine("src", "SafeClean.App", "App.xaml.cs");
        var concrete = new Regex(@"\bCleanupService\b|Infrastructure\.Cleanup\b");
        var engine = new Regex(@"\bHandleBasedFileDeleter\b|\bAttemptResult\b");

        var offenders = RepositoryFiles.SourceFiles("SafeClean.App")
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: RepositoryFiles.Relative(f), Line: i + 1, Text: line)))
            .Where(x => engine.IsMatch(x.Text) || (concrete.IsMatch(x.Text) && !string.Equals(x.File, compositionRoot, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "The UI may reach cleanup only through ICleanupService:\n" + string.Join('\n', offenders));
        Assert.Contains("AddSingleton<ICleanupService, CleanupService>()", File.ReadAllText(Path.Combine(RepositoryFiles.Root, compositionRoot)), StringComparison.Ordinal);
    }

    [Fact]
    public void SandboxMode_ExistsOnlyInDebugBuilds()
    {
        // The sandbox resolver is wrapped entirely in one #if DEBUG block.
        var resolverFile = Path.Combine(RepositoryFiles.SourceDirectory, "SafeClean.Infrastructure", "Scanning", "DebugSandboxRootResolver.cs");
        var resolverLines = File.ReadAllLines(resolverFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        Assert.Equal("#if DEBUG", resolverLines[0]);
        Assert.Equal("#endif", resolverLines[^1]);
        Assert.Single(resolverLines, l => l.StartsWith("#if", StringComparison.Ordinal));

        // Everywhere else, anything that switches sandbox mode on sits inside an #if DEBUG block.
        var sandboxCode = new Regex(@"DebugSandboxRootResolver|--sandbox-root|ReadSandboxArgument");
        var offenders = new List<string>();
        var users = 0;
        foreach (var file in RepositoryFiles.SourceFiles().Where(f => !string.Equals(f, resolverFile, StringComparison.OrdinalIgnoreCase)))
        {
            var debugOnly = new Stack<bool>();
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    debugOnly.Push(trimmed == "#if DEBUG");
                }
                else if (trimmed.StartsWith("#else", StringComparison.Ordinal) || trimmed.StartsWith("#elif", StringComparison.Ordinal))
                {
                    debugOnly.Pop();
                    debugOnly.Push(false); // the other branch is never "debug only"
                }
                else if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    debugOnly.Pop();
                }
                else if (sandboxCode.IsMatch(line))
                {
                    users++;
                    if (!debugOnly.Contains(true))
                    {
                        offenders.Add($"{RepositoryFiles.Relative(file)}:{lineNumber}: {trimmed}");
                    }
                }
            }
        }

        Assert.True(users > 0, "Expected the composition root to use the sandbox resolver in Debug builds.");
        Assert.True(offenders.Count == 0, "Sandbox code outside #if DEBUG:\n" + string.Join('\n', offenders));
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
