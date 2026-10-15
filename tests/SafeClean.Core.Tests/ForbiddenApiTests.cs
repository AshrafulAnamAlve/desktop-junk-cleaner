using System.Text.RegularExpressions;

namespace SafeClean.Core.Tests;

/// <summary>
/// Smoke alarm for dangerous APIs. Scans every C#/XAML file under src/ and fails if a forbidden API
/// appears. When a later phase legitimately needs one (e.g. the single deletion engine), that exact
/// file is added to <see cref="Allowed"/> with a reason — nothing else gets the exception.
/// </summary>
public sealed class ForbiddenApiTests
{
    private static readonly (string Pattern, string Reason)[] Forbidden =
    [
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
        (@"(?i)\bcmd(\.exe)?\b\s*[""/]", "command prompt"),
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
    private static readonly Dictionary<string, string[]> Allowed = [];

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
        ];

        Assert.All(samples, sample =>
            Assert.Contains(Forbidden, f => Regex.IsMatch(sample, f.Pattern)));
    }
}
