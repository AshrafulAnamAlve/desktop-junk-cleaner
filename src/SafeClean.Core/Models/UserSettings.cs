namespace SafeClean.Core.Models;

/// <summary>
/// What the user can change. Every setting can only make SafeClean <b>stricter</b> than its defaults:
/// a longer minimum age, more exclusions. There is no setting for Real mode, cleanup locations or
/// protected folders.
/// </summary>
/// <param name="MinimumFileAgeDays">Files changed more recently than this are kept (7–365; 7 is the default and the lowest allowed).</param>
/// <param name="Exclusions">Things to always skip, written relative to the Temp folder (<c>Installers\Old</c>) or as file-name patterns (<c>*.log</c>).</param>
/// <param name="LogLevel">How much SafeClean writes to its log. Cleanup audit lines are always written.</param>
/// <param name="LogRetentionDays">How old SafeClean's own log files must be before "Delete old logs" may remove them (7–90).</param>
public sealed record UserSettings(int MinimumFileAgeDays, IReadOnlyList<string> Exclusions, SettingsLogLevel LogLevel, int LogRetentionDays)
{
    public static UserSettings Default { get; } = new(7, [], SettingsLogLevel.Information, 30);
}

/// <summary>Log detail levels offered in Settings.</summary>
public enum SettingsLogLevel
{
    Information,
    Warning,
    Error,
}
