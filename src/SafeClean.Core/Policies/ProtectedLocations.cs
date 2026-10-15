using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Locations SafeClean must never clean (docs/SECURITY.md §3).
/// <list type="bullet">
/// <item><b>Subtrees</b> (e.g. Windows, Documents): nothing at or below them may be touched.</item>
/// <item><b>Exact paths</b> (e.g. the user profile): may not be the cleanup root, but things below them may be legitimate.</item>
/// </list>
/// Every drive root (<c>C:\</c>) is always treated as an exact protected path.
/// Comparisons ignore case, so more paths match and more files are protected.
/// </summary>
public sealed class ProtectedLocations
{
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    public ProtectedLocations(IEnumerable<string?> subtrees, IEnumerable<string?> exactPaths)
    {
        ArgumentNullException.ThrowIfNull(subtrees);
        ArgumentNullException.ThrowIfNull(exactPaths);
        Subtrees = Normalize(subtrees, nameof(subtrees));
        ExactPaths = Normalize(exactPaths, nameof(exactPaths));
    }

    public IReadOnlyList<string> Subtrees { get; }

    public IReadOnlyList<string> ExactPaths { get; }

    /// <summary>True if a file at <paramref name="path"/> must not be cleaned.</summary>
    public bool Protects(string path) =>
        WindowsPath.IsDriveRoot(path)
        || Subtrees.Any(subtree => WindowsPath.IsSameOrInside(subtree, path, Comparison))
        || ExactPaths.Any(exact => string.Equals(exact, path, Comparison));

    /// <summary>Returns why <paramref name="root"/> may not be a cleanup root, or null if it doesn't conflict.</summary>
    public RootProblem? FindRootConflict(string root)
    {
        if (WindowsPath.IsDriveRoot(root))
        {
            return RootProblem.IsDriveRoot;
        }

        var all = Subtrees.Concat(ExactPaths).ToList();
        if (all.Any(location => string.Equals(location, root, Comparison)))
        {
            return RootProblem.IsProtectedLocation;
        }

        if (Subtrees.Any(subtree => WindowsPath.IsStrictlyInside(subtree, root, Comparison)))
        {
            return RootProblem.InsideProtectedLocation;
        }

        if (all.Any(location => WindowsPath.IsStrictlyInside(root, location, Comparison)))
        {
            return RootProblem.ContainsProtectedLocation;
        }

        return null;
    }

    private static List<string> Normalize(IEnumerable<string?> paths, string parameterName)
    {
        var result = new List<string>();
        foreach (var raw in paths)
        {
            // A known folder that doesn't exist on this PC comes back empty; there is nothing to protect.
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = WindowsPath.TrimTrailingSeparator(raw);
            var problem = PathValidator.FindProblem(path);
            if (problem is not null)
            {
                throw new ArgumentException($"Protected location '{raw}' is not a valid path ({problem}).", parameterName);
            }

            if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
