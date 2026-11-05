using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeClean.Core.Tests;

/// <summary>
/// Phase 12: the promises about the published program that can be checked from the repository's files. The release
/// script also checks each finished build itself (no sandbox code, normal rights, bundled runtime).
/// </summary>
public sealed partial class PublishingTests
{
    private static readonly string ReleaseScript = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "tools", "publish-release.ps1"));

    /// <summary>The script's commands, without its comments, which are allowed to explain what it avoids.</summary>
    private static string ReleaseScriptCommands => CommentLine().Replace(ReleaseScript, string.Empty);

    [Fact]
    public void Release_NeverUnpacksIntoTheTempFolder()
    {
        // With "true", .NET would unpack DLLs into %TEMP%\.net on every run: a Temp cleaner would fill its own Temp folder.
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=false", ReleaseScriptCommands, StringComparison.Ordinal);
        Assert.DoesNotContain("IncludeNativeLibrariesForSelfExtract=true", ReleaseScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Release_IsARuntimeIncludedReleaseBuild_WithoutTrimming()
    {
        // A Debug build contains the sandbox option, and trimming isn't supported for WPF.
        Assert.Contains("-c Release", ReleaseScriptCommands, StringComparison.Ordinal);
        Assert.Contains("-r win-x64", ReleaseScriptCommands, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", ReleaseScriptCommands, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", ReleaseScriptCommands, StringComparison.Ordinal);
        Assert.Contains("-p:PublishTrimmed=false", ReleaseScriptCommands, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScript_DeletesNothing_AndRefusesToOverwrite()
    {
        Assert.DoesNotMatch(DeletingCommand(), ReleaseScriptCommands);
        Assert.Contains("already exists", ReleaseScriptCommands, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseScript_ChecksTheFinishedBuild()
    {
        foreach (var check in new[] { "sandbox-root", "DebugSandboxRootResolver", "level=\"asInvoker\"", "level=\"requireAdministrator\"", "Get-FileHash" })
        {
            Assert.Contains(check, ReleaseScriptCommands, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppIcon_IsReferenced_AndHasTheSizesWindowsUses()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryFiles.SourceDirectory, "SafeClean.App", "SafeClean.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>Resources\SafeClean.ico</ApplicationIcon>", project, StringComparison.Ordinal);

        var icon = File.ReadAllBytes(Path.Combine(RepositoryFiles.SourceDirectory, "SafeClean.App", "Resources", "SafeClean.ico"));
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, icon[..4]);

        // Each entry is 16 bytes after the 6-byte header; its first byte is the width, where 0 means 256.
        var widths = Enumerable.Range(0, BitConverter.ToUInt16(icon, 4))
            .Select(i => icon[6 + (16 * i)] == 0 ? 256 : icon[6 + (16 * i)])
            .ToList();
        Assert.Contains(16, widths);
        Assert.Contains(32, widths);
        Assert.Contains(48, widths);
        Assert.Contains(256, widths);
    }

    [Fact]
    public void Builds_RequireAnSdkWithThePatchedRuntime()
    {
        // The published program carries the runtime of the SDK that built it, so an older SDK would ship older security fixes.
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.Root, "global.json")));
        var version = Version.Parse(globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);
        Assert.True(version >= new Version(10, 0, 403), $"SDK {version} is older than 10.0.403.");
    }

    [Fact]
    public void PortableReadme_TellsPeopleWhatTheyNeedToKnow()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "tools", "README-portable.txt"));
        foreach (var needed in new[] { "Extract All", "Windows protected your PC", "Get-FileHash", "%LOCALAPPDATA%\\SafeClean", "Recycle Bin" })
        {
            Assert.Contains(needed, readme, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"(?m)^\s*#.*$")]
    private static partial Regex CommentLine();

    [GeneratedRegex(@"(?i)\b(Remove-Item|Clear-Content|rmdir|rd|del|erase|ri)\b|\.Delete\(|\[IO\.(File|Directory)\]::Delete")]
    private static partial Regex DeletingCommand();
}
