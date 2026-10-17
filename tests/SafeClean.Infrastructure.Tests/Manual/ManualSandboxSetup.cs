#if DEBUG
using System.Globalization;
using SafeClean.Infrastructure.Scanning;
using SafeClean.Infrastructure.Tests.Sandbox;

namespace SafeClean.Infrastructure.Tests.Manual;

/// <summary>
/// Creates a folder of synthetic files for trying cleanup by hand in a Debug build — no scripts needed.
/// Explicit: runs only when asked for. It never deletes anything; each run makes a new folder under
/// <c>&lt;repository&gt;\.sandbox\</c> (ignored by Git).
/// </summary>
public sealed class ManualSandboxSetup(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public void CreateSandboxForManualTesting()
    {
        var root = Path.Combine(FindRepositoryRoot(), ".sandbox", "manual-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(root);

        Create(root, "report-old.tmp", 2 * 1024, ageDays: 30);           // safe to remove
        Create(root, @"logs\setup-old.log", 10 * 1024, ageDays: 30);     // safe to remove
        Create(root, @"cache\data-old.dat", 50 * 1024, ageDays: 30);     // safe to remove
        Create(root, "installer-old.exe", 100 * 1024, ageDays: 30);      // needs review
        Create(root, "fresh-new.tmp", 1024, ageDays: 0);                 // skipped: too recent
        var readOnly = Create(root, "readonly-old.tmp", 1024, ageDays: 30);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);           // skipped: read-only

        // The marker is hidden and read-only, so SafeClean always skips it and can never remove it.
        var marker = Path.Combine(root, DebugSandboxRootResolver.MarkerFileName);
        File.WriteAllText(marker, "SafeClean test sandbox. Debug builds started with --sandbox-root may delete synthetic files in this folder.");
        File.SetAttributes(marker, FileAttributes.Hidden | FileAttributes.ReadOnly);

        output.WriteLine("Sandbox created: " + root);
        output.WriteLine($"Start SafeClean on it:  dotnet run --project src/SafeClean.App -- --sandbox-root \"{root}\"");
    }

    private static string Create(string root, string relativePath, int size, int ageDays)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        if (ageDays > 0)
        {
            TestSandbox.SetAge(path, ageDays);
        }

        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SafeClean.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find SafeClean.sln.");
    }
}
#endif
