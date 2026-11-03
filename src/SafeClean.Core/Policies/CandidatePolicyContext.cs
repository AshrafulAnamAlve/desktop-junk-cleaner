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
        IEnumerable<string>? excludedSubtrees = null,
        IEnumerable<string>? excludedNamePatterns = null)
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
        ExcludedNamePatterns = (excludedNamePatterns ?? []).Select(NormalizePattern).ToList();
    }

    public ApprovedRoot Root { get; }

    public ProtectedLocations Protected { get; }

    /// <summary>The moment the file is being judged.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>Files created or changed more recently than this are kept. Never below <see cref="SafetyDefaults.MinimumFileAge"/>.</summary>
    public TimeSpan MinimumAge { get; }

    /// <summary>User exclusions: folders or files to always skip.</summary>
    public IReadOnlyList<string> ExcludedSubtrees { get; }

    /// <summary>User exclusions: file-name patterns (<c>*.log</c>) to always skip, wherever the file is.</summary>
    public IReadOnlyList<string> ExcludedNamePatterns { get; }

    /// <summary>
    /// The rules as the user's settings make them. Scanning and cleanup both build their rules here, so a
    /// cleanup always uses the same (or newer, never looser than at scan time for selectable files) settings.
    /// </summary>
    public static CandidatePolicyContext ForSettings(ApprovedRoot root, ProtectedLocations protectedLocations, DateTimeOffset now, UserSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(root);
        settings ??= UserSettings.Default;

        var exclusions = settings.Exclusions ?? [];
        return new CandidatePolicyContext(
            root,
            protectedLocations,
            now,
            TimeSpan.FromDays(settings.MinimumFileAgeDays),
            exclusions.Where(e => !SettingsPolicy.IsNamePattern(e)).Select(relative => root.CanonicalPath + @"\" + relative.TrimEnd('\\')),
            exclusions.Where(SettingsPolicy.IsNamePattern));
    }

    internal bool IsExcluded(string path) =>
        ExcludedSubtrees.Any(excluded => WindowsPath.IsSameOrInside(excluded, path, StringComparison.OrdinalIgnoreCase))
        || (ExcludedNamePatterns.Count > 0 && ExcludedNamePatterns.Any(pattern => WildcardPattern.IsMatch(WindowsPath.GetName(path), pattern)));

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

    private static string NormalizePattern(string raw)
    {
        var result = SettingsPolicy.ValidateExclusion(raw);
        if (result.Error is not null || !SettingsPolicy.IsNamePattern(result.Normalized!))
        {
            throw new ArgumentException($"Exclusion pattern '{raw}' is not valid.", nameof(raw));
        }

        return result.Normalized!;
    }
}
