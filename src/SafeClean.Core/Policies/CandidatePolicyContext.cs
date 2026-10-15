using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>Everything <see cref="CandidatePolicy"/> needs besides the file itself.</summary>
public sealed class CandidatePolicyContext
{
    public CandidatePolicyContext(
        ApprovedRoot root,
        ProtectedLocations protectedLocations,
        DateTimeOffset now,
        TimeSpan? minimumAge = null,
        IEnumerable<string>? excludedSubtrees = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(protectedLocations);

        Root = root;
        Protected = protectedLocations;
        Now = now;

        // Settings may make SafeClean stricter, never looser: the age can only grow above the default.
        var requested = minimumAge ?? SafetyDefaults.MinimumFileAge;
        MinimumAge = requested > SafetyDefaults.MinimumFileAge ? requested : SafetyDefaults.MinimumFileAge;

        ExcludedSubtrees = (excludedSubtrees ?? []).Select(NormalizeExclusion).ToList();
    }

    public ApprovedRoot Root { get; }

    public ProtectedLocations Protected { get; }

    /// <summary>The moment the file is being judged.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>Files created or changed more recently than this are kept. Never below <see cref="SafetyDefaults.MinimumFileAge"/>.</summary>
    public TimeSpan MinimumAge { get; }

    /// <summary>User exclusions: folders or files to always skip.</summary>
    public IReadOnlyList<string> ExcludedSubtrees { get; }

    internal bool IsExcluded(string path) =>
        ExcludedSubtrees.Any(excluded => WindowsPath.IsSameOrInside(excluded, path, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeExclusion(string raw)
    {
        var path = WindowsPath.TrimTrailingSeparator(raw ?? string.Empty);
        var problem = PathValidator.FindProblem(path);
        if (problem is not null)
        {
            throw new ArgumentException($"Exclusion '{raw}' is not a valid path ({problem}).", nameof(raw));
        }

        return path;
    }
}
