using System.Text.RegularExpressions;

namespace SafeClean.Core.Tests;

/// <summary>
/// Smoke alarm for dangerous APIs. Scans every C#/XAML file under src/ and fails if a forbidden API
/// appears. When a later phase legitimately needs one (e.g. the single deletion engine), that exact
/// file is added to <see cref="Allowed"/> with a reason — nothing else gets the exception.
/// </summary>
public sealed class ForbiddenApiTests
{
    /// <summary>The single file allowed to delete (Phase 5). See docs/SECURITY.md §5.</summary>
    private static readonly string DeletionEngine = Path.Combine("src", "SafeClean.Infrastructure", "Cleanup", "HandleBasedFileDeleter.cs");

    // Patterns only the deletion engine may use.
    private const string HandleChange = @"\bSetFileInformationByHandle\b";
    private const string Disposition = @"(?i)\bFILE_?DISPOSITION";
    private const string DeleteAccess = @"\bDELETE\b";
    private const string DeleteAccessNumber = @"(?i)\b0x0*1_?0000\b";

    private static readonly (string Pattern, string Reason)[] Forbidden =
    [
        // Handle-based deletion: allowed only in the deletion engine (see Allowed below)
        (HandleChange, "changing a file through a handle"),
        (Disposition, "marking a file for deletion"),
        (DeleteAccess, "the DELETE access right"),
        (DeleteAccessNumber, "the DELETE access right written as a number"),

        // Other ways to delete, move or overwrite files: allowed nowhere
        (@"(?i)DELETE_?ON_?CLOSE", "delete-on-close (deletes by path)"),
        (@"\b(Nt|Zw)SetInformationFile\b", "low-level file changes"),
        (@"\b(File|Directory)\.(Move|Replace)\b|\bReplaceFile|\bMoveFile", "moving or replacing files"),

        // Path-based or broad deletion (SafeClean must delete only through a validated handle)
        (@"\bFile\.Delete\b", "path-based file deletion"),
        (@"\bDirectory\.Delete\b", "path-based directory deletion"),
        (@"\.Delete\s*\(", "FileInfo/DirectoryInfo deletion"),
        (@"\bDeleteFile", "Win32 path-based deletion"),
        (@"\bRemoveDirectory", "Win32 directory deletion"),
        (@"\bMoveFileEx\b", "delete-on-reboot / moving files"),
        (@"\bSHFileOperation\b", "shell file operations"),
        (@"\bIFileOperation\b", "shell file operations"),
        (@"\bSHEmptyRecycleBin\b", "emptying the Recycle Bin"),

        // Shells and launching other programs
        (@"\bProcess\.Start\b", "launching processes"),
        // "cmd.exe", "cmd /c", Process.Start("cmd", ...) — but not the ".cmd" file extension.
        (@"(?i)(?<![.\w])cmd(\.exe)?\b\s*[""/]", "command prompt"),
        (@"(?i)\bpowershell\b", "PowerShell"),

        // Killing processes or manipulating memory
        (@"\.Kill\s*\(", "terminating processes"),
        (@"\bCloseMainWindow\b", "closing other programs"),
        (@"\bEmptyWorkingSet\b", "trimming process memory"),
        (@"\bSetProcessWorkingSetSize", "trimming process memory"),
        (@"\bNtSetSystemInformation\b", "purging the standby list"),

        // System configuration
        (@"\bRegistry(Key)?\b", "registry access"),

        // Network (privacy: SafeClean never sends data anywhere)
        (@"\bHttpClient\b", "network access"),
        (@"\bWebClient\b", "network access"),
        (@"\bWebRequest\b", "network access"),
        (@"\bTcpClient\b", "network access"),
        (@"\bUdpClient\b", "network access"),
        (@"\bSocket\b", "network access"),
        (@"\bSystem\.Net\.(Http|Sockets)\b", "network access"),
    ];

    /// <summary>Pattern → files (relative to the repository root) allowed to use it, with the reason documented there.</summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [HandleChange] = [DeletionEngine],
        [Disposition] = [DeletionEngine],
        [DeleteAccess] = [DeletionEngine],
        [DeleteAccessNumber] = [DeletionEngine],
    };

    public static TheoryData<string, string> ForbiddenPatterns()
    {
        var data = new TheoryData<string, string>();
        foreach (var (pattern, reason) in Forbidden)
        {
            data.Add(pattern, reason);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenPatterns))]
    public void SourceCode_DoesNotUseForbiddenApi(string pattern, string reason)
    {
        var regex = new Regex(pattern);
        var allowed = Allowed.TryGetValue(pattern, out var files) ? files : [];

        var offenders = RepositoryFiles.SourceFiles()
            .Where(f => !allowed.Contains(RepositoryFiles.Relative(f), StringComparer.OrdinalIgnoreCase))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => regex.IsMatch(x.Text))
            .Select(x => $"{RepositoryFiles.Relative(x.File)}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, $"Forbidden API ({reason}) found:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Scanner_ActuallySeesTheSourceFiles()
    {
        // Guards against the tests above passing only because no files were found.
        var files = RepositoryFiles.SourceFiles().Select(Path.GetFileName).ToList();

        Assert.Contains("CleanupMode.cs", files);
        Assert.Contains("FileLoggerProvider.cs", files);
        Assert.Contains("MainWindow.xaml", files);
        Assert.Contains("HandleBasedFileDeleter.cs", files);
    }

    [Fact]
    public void Patterns_DetectKnownBadCode()
    {
        // Each sample must be caught by at least one forbidden pattern.
        string[] samples =
        [
            "File.Delete(path);",
            "Directory.Delete(path, true);",
            "new FileInfo(p).Delete();",
            "Process.Start(\"cmd.exe\", \"/c del *\");",
            "process.Kill();",
            "using var http = new HttpClient();",
            "Registry.CurrentUser.OpenSubKey(x);",
            "SetFileInformationByHandle(handle, 4, ref info, 1);",
            "new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);",
            "const uint Access = 0x00010000;",
            "File.Move(a, b, overwrite: true);",
            "NtSetInformationFile(h, out io, info, len, 13);",
        ];

        Assert.All(samples, sample =>
            Assert.Contains(Forbidden, f => Regex.IsMatch(sample, f.Pattern)));
    }

    [Fact]
    public void AllowList_NamesOnlyTheDeletionEngine_AndItExists()
    {
        var allowedFiles = Allowed.Values.SelectMany(files => files).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal([DeletionEngine], allowedFiles);
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root, DeletionEngine)), "The allow-listed deletion engine file is missing.");
    }

    [Fact]
    public void DeletionEngine_IsWhereTheHandleDeletionCodeActuallyLives()
    {
        // Proves the allowances are needed (not stale): the engine really uses each allowed pattern.
        var source = File.ReadAllText(Path.Combine(RepositoryFiles.Root, DeletionEngine));

        Assert.All(Allowed.Keys, pattern => Assert.Matches(pattern, source));
    }

    [Theory]
    [InlineData("private const uint SYNCHRONIZE = 0x0010_0000;")]
    [InlineData("internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;")]
    [InlineData("var generic = 0x10000000;")]
    public void DeleteAccessNumberPattern_IgnoresOtherFlags(string sample)
    {
        Assert.DoesNotMatch(DeleteAccessNumber, sample);
    }

    [Theory]
    [InlineData("Process.Start(\"cmd.exe\", \"/c del *\");")]
    [InlineData("Process.Start(\"cmd\", \"/c rd /s\");")]
    [InlineData("var shell = @\"C:\\Windows\\System32\\cmd.exe\";")]
    [InlineData("run(\"cmd /c del *\");")]
    public void CommandPromptPattern_CatchesShellLaunches(string sample)
    {
        Assert.Matches(CommandPromptPattern, sample);
    }

    [Theory]
    [InlineData("\".bat\", \".cmd\", \".vbs\"")]
    [InlineData("var command = \"x\";")]
    [InlineData("// a .cmd file is a script")]
    public void CommandPromptPattern_IgnoresHarmlessText(string sample)
    {
        Assert.DoesNotMatch(CommandPromptPattern, sample);
    }

    private static string CommandPromptPattern => Forbidden.Single(f => f.Reason == "command prompt").Pattern;
}
