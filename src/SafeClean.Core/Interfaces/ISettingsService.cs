using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Core.Interfaces;

/// <summary>The settings in force right now. Scanning and cleanup read them at the start of each run.</summary>
public interface IUserSettingsProvider
{
    UserSettings Current { get; }

    /// <summary>
    /// Why the saved settings couldn't be used (safe defaults are in force), or null. While this is set,
    /// cleanup is refused: exclusions the user relied on might be missing.
    /// </summary>
    string? LoadProblem { get; }
}

/// <summary>Reads and saves the user's settings. Invalid settings are never saved.</summary>
public interface ISettingsService : IUserSettingsProvider
{
    event EventHandler? Changed;

    /// <summary>Validates and saves; on any problem nothing is saved and the errors are returned.</summary>
    SettingsValidation Save(UserSettings proposed);
}

/// <summary>Fixed settings (tests, and log maintenance which uses its own retention age).</summary>
public sealed class FixedUserSettings(UserSettings settings, string? loadProblem = null) : IUserSettingsProvider
{
    public UserSettings Current { get; } = settings;

    public string? LoadProblem { get; } = loadProblem;
}
