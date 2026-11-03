using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Checks settings before they are saved or used. Rule: settings can make SafeClean stricter, never looser —
/// the minimum age can't go below the safe default, and exclusions can only cause more files to be kept.
/// </summary>
public static class SettingsPolicy
{
    public const int MaxAgeDays = 365;
    public const int MinRetentionDays = 7;
    public const int MaxRetentionDays = 90;
    public const int MaxExclusions = 100;

    /// <summary>The lowest minimum age allowed: the safe default (7 days).</summary>
    public static int MinAgeDays { get; } = (int)SafetyDefaults.MinimumFileAge.TotalDays;

    public static IReadOnlyList<int> AgeChoices { get; } = [7, 14, 30, 60, 90, 180, 365];

    public static IReadOnlyList<int> RetentionChoices { get; } = [7, 14, 30, 60, 90];

    /// <summary>A file-name pattern (<c>*.log</c>) rather than a path inside the Temp folder.</summary>
    public static bool IsNamePattern(string exclusion) => exclusion.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>Returns the cleaned-up settings, or the list of problems (nothing is saved when there are any).</summary>
    public static SettingsValidation Validate(UserSettings proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        var errors = new List<string>();

        if (proposed.MinimumFileAgeDays < MinAgeDays)
        {
            errors.Add($"The minimum file age can't be lower than {MinAgeDays} days (the safe default).");
        }
        else if (proposed.MinimumFileAgeDays > MaxAgeDays)
        {
            errors.Add($"The minimum file age can't be more than {MaxAgeDays} days.");
        }

        if (proposed.LogRetentionDays is < MinRetentionDays or > MaxRetentionDays)
        {
            errors.Add($"Log retention must be between {MinRetentionDays} and {MaxRetentionDays} days.");
        }

        if (!Enum.IsDefined(proposed.LogLevel))
        {
            errors.Add("The log level isn't recognised.");
        }

        var exclusions = new List<string>();
        foreach (var raw in proposed.Exclusions ?? [])
        {
            var result = ValidateExclusion(raw);
            if (result.Error is not null)
            {
                errors.Add($"Exclusion \"{raw}\": {result.Error}");
            }
            else if (!exclusions.Contains(result.Normalized!, StringComparer.OrdinalIgnoreCase))
            {
                exclusions.Add(result.Normalized!);
            }
        }

        if (exclusions.Count > MaxExclusions)
        {
            errors.Add($"At most {MaxExclusions} exclusions are allowed.");
        }

        return errors.Count > 0
            ? new SettingsValidation(null, errors)
            : new SettingsValidation(proposed with { Exclusions = exclusions }, []);
    }

    /// <summary>
    /// An exclusion is either a path inside the Temp folder written relative to it (a folder or a file),
    /// or a file-name pattern. Full paths, <c>..</c>, streams, device names and similar are refused.
    /// </summary>
    public static ExclusionValidation ValidateExclusion(string? raw)
    {
        var text = raw?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return ExclusionValidation.Fail("Enter a folder inside your Temp folder or a file-name pattern.");
        }

        if (IsNamePattern(text))
        {
            if (text.AsSpan().IndexOfAny('\\', '/', ':') >= 0)
            {
                return ExclusionValidation.Fail("A pattern matches file names only, for example *.log (no folders).");
            }

            if (text.Length > 255 || text.Any(c => char.IsControl(c) || c is '<' or '>' or '"' or '|'))
            {
                return ExclusionValidation.Fail("The pattern contains characters that can't appear in file names.");
            }

            return ExclusionValidation.Ok(text);
        }

        if (text.StartsWith('\\') || text.StartsWith('/') || text.Contains(':', StringComparison.Ordinal))
        {
            return ExclusionValidation.Fail(@"Write it relative to your Temp folder (for example Installers\Old), not as a full path.");
        }

        var relative = text.TrimEnd('\\');
        var problem = PathValidator.FindProblem(@"C:\SafeCleanExclusionCheck\" + relative);
        return problem switch
        {
            null => ExclusionValidation.Ok(relative),
            PathProblem.DotSegment => ExclusionValidation.Fail("\".\" and \"..\" aren't allowed: an exclusion must stay inside the Temp folder."),
            PathProblem.ForwardSlash => ExclusionValidation.Fail(@"Use backslashes, for example Installers\Old."),
            _ => ExclusionValidation.Fail("This isn't a valid folder or file name."),
        };
    }
}

/// <summary>Validated settings, or the problems found.</summary>
public sealed record SettingsValidation(UserSettings? Settings, IReadOnlyList<string> Errors)
{
    public bool IsValid => Settings is not null;
}

/// <summary>A cleaned-up exclusion, or why it was refused.</summary>
public sealed record ExclusionValidation(string? Normalized, string? Error)
{
    public static ExclusionValidation Ok(string normalized) => new(normalized, null);

    public static ExclusionValidation Fail(string error) => new(null, error);
}
