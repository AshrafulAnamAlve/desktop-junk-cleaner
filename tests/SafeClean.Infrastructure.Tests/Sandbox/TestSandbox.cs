using System.Security.Cryptography;

namespace SafeClean.Infrastructure.Tests.Sandbox;

/// <summary>
/// A disposable folder for tests, created under the test project's own output folder
/// (bin/.../TestSandbox/&lt;random id&gt;) — never in the real Temp folder or any personal folder.
/// Disposing removes only this folder, and never follows links out of it.
/// </summary>
internal sealed class TestSandbox : IDisposable
{
    private static readonly string BaseDirectory = Path.Combine(AppContext.BaseDirectory, "TestSandbox");

    private TestSandbox(string root) => Root = root;

    public string Root { get; }

    public static TestSandbox Create()
    {
        var root = Path.Combine(BaseDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestSandbox(root);
    }

    /// <summary>Returns a full path inside the sandbox; throws if <paramref name="relativePath"/> would escape it.</summary>
    public string PathFor(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!IsInside(Root, full))
        {
            throw new InvalidOperationException($"'{relativePath}' escapes the test sandbox.");
        }

        return full;
    }

    public string CreateDirectory(string relativePath)
    {
        var path = PathFor(relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates a synthetic file, by default dated 30 days ago so the age rule doesn't skip it.</summary>
    public string CreateFile(string relativePath, string content = "synthetic test data", int ageDays = 30)
    {
        var path = PathFor(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (ageDays > 0)
        {
            SetAge(path, ageDays);
        }

        return path;
    }

    public static void SetAge(string path, int days)
    {
        var time = DateTime.UtcNow.AddDays(-days);
        File.SetCreationTimeUtc(path, time);
        File.SetLastWriteTimeUtc(path, time);
    }

    /// <summary>
    /// Records size, dates, attributes and a SHA-256 hash of every file in the sandbox (links are recorded
    /// but not followed). Used to prove a scan changed nothing.
    /// </summary>
    public SortedDictionary<string, string> Snapshot()
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SnapshotFolder(Root, snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            DeleteTree(Root);
        }
    }

    private void SnapshotFolder(string folder, SortedDictionary<string, string> snapshot)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", OneLevel))
        {
            var key = Path.GetRelativePath(Root, entry.FullName);
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                snapshot[key] = $"link attrs={entry.Attributes} target={entry.LinkTarget}";
            }
            else if (entry is DirectoryInfo directory)
            {
                snapshot[key] = $"dir attrs={directory.Attributes}";
                SnapshotFolder(directory.FullName, snapshot);
            }
            else if (entry is FileInfo file)
            {
                // Last-access time is deliberately not compared: reading the file for the hash updates it.
                snapshot[key] = $"file len={file.Length} created={file.CreationTimeUtc.Ticks} written={file.LastWriteTimeUtc.Ticks} attrs={file.Attributes} sha256={Hash(file.FullName)}";
            }
        }
    }

    private static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static readonly EnumerationOptions OneLevel = new() { AttributesToSkip = 0, RecurseSubdirectories = false, IgnoreInaccessible = false };

    private static void DeleteTree(string directory)
    {
        if (!IsInside(BaseDirectory, directory))
        {
            throw new InvalidOperationException($"Refusing to delete '{directory}': it is outside the test sandbox base.");
        }

        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", OneLevel))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Remove the link itself; its target is never visited or modified.
                entry.Delete();
            }
            else if (entry is DirectoryInfo subdirectory)
            {
                DeleteTree(subdirectory.FullName);
            }
            else
            {
                entry.Attributes = FileAttributes.Normal;
                entry.Delete();
            }
        }

        Directory.Delete(directory, recursive: false);
    }

    private static bool IsInside(string parent, string candidate) =>
        candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
