namespace SafeClean.Core.Policies;

/// <summary>
/// Small, pure helpers for Windows drive paths like <c>C:\Users\Name\AppData\Local\Temp</c>.
/// They work on text only and never touch the disk. Paths are expected to have passed
/// <see cref="PathValidator"/> first.
/// </summary>
public static class WindowsPath
{
    /// <summary>True for paths that start with a drive letter, a colon and a backslash, like <c>C:\</c>.</summary>
    public static bool HasDriveRoot(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';

    /// <summary>True for the top of a drive, such as <c>C:\</c>.</summary>
    public static bool IsDriveRoot(string path) => path.Length == 3 && HasDriveRoot(path);

    /// <summary>Removes one trailing backslash, except from a drive root (<c>C:\</c> stays as it is).</summary>
    public static string TrimTrailingSeparator(string path) =>
        path.Length > 3 && path[^1] == '\\' ? path[..^1] : path;

    /// <summary>
    /// True if <paramref name="candidate"/> is below <paramref name="parent"/> (not equal to it).
    /// <c>C:\Temp2\a</c> is <b>not</b> inside <c>C:\Temp</c>: the character after the prefix must be a separator.
    /// </summary>
    /// <param name="comparison">
    /// <see cref="StringComparison.Ordinal"/> for paths reported by Windows (exact match errs toward skipping),
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> for protection checks (more matches err toward protecting).
    /// </param>
    public static bool IsStrictlyInside(string parent, string candidate, StringComparison comparison)
    {
        EnsureOrdinal(comparison);
        if (IsDriveRoot(parent))
        {
            return candidate.Length > parent.Length && candidate.StartsWith(parent, comparison);
        }

        return candidate.Length > parent.Length + 1
            && candidate[parent.Length] == '\\'
            && candidate.StartsWith(parent, comparison);
    }

    /// <summary>True if <paramref name="candidate"/> equals <paramref name="parent"/> or is below it.</summary>
    public static bool IsSameOrInside(string parent, string candidate, StringComparison comparison)
    {
        EnsureOrdinal(comparison);
        return string.Equals(parent, candidate, comparison) || IsStrictlyInside(parent, candidate, comparison);
    }

    /// <summary>The part of <paramref name="path"/> below <paramref name="root"/>, e.g. <c>sub\file.tmp</c>.</summary>
    public static string GetRelativePath(string root, string path)
    {
        if (!IsStrictlyInside(root, path, StringComparison.Ordinal))
        {
            throw new ArgumentException("The path is not inside the root.", nameof(path));
        }

        return IsDriveRoot(root) ? path[root.Length..] : path[(root.Length + 1)..];
    }

    /// <summary>How many folders a root-relative path passes through: <c>a.tmp</c> → 0, <c>x\y\a.tmp</c> → 2.</summary>
    public static int CountFolderLevels(string relativePath) => relativePath.Count(c => c == '\\');

    /// <summary>The last segment of a path: <c>C:\Temp\a.tmp</c> → <c>a.tmp</c>.</summary>
    public static string GetName(string path) => path[(path.LastIndexOf('\\') + 1)..];

    /// <summary>The extension including the dot (<c>.tmp</c>), or an empty string if the name has no dot.</summary>
    public static string GetExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[dot..] : string.Empty;
    }

    private static void EnsureOrdinal(StringComparison comparison)
    {
        // Culture-aware comparisons can treat different paths as equal; never allow them for paths.
        if (comparison is not (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(comparison), comparison, "Paths must be compared with Ordinal or OrdinalIgnoreCase.");
        }
    }
}
