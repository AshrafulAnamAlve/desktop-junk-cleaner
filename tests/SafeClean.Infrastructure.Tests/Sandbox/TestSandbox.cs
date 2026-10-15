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

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            DeleteTree(Root);
        }
    }

    private static void DeleteTree(string directory)
    {
        if (!IsInside(BaseDirectory, directory))
        {
            throw new InvalidOperationException($"Refusing to delete '{directory}': it is outside the test sandbox base.");
        }

        var options = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = false, IgnoreInaccessible = false };
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
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
