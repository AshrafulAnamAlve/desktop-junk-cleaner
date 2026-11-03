using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Infrastructure.Settings;

/// <summary>
/// Reads and saves <c>%LOCALAPPDATA%\SafeClean\settings.json</c>.
/// <list type="bullet">
/// <item>No file yet → safe defaults.</item>
/// <item>A file that can't be read or contains invalid values → safe defaults <b>and</b> <see cref="LoadProblem"/> is set,
/// which pauses cleanup (the user's exclusions might be missing) until the settings are saved again. Saving then
/// keeps a copy of the old file (<see cref="UnreadableCopySuffix"/>), because it may hold exclusions worth recovering.</item>
/// <item>Unknown names in the file make it unusable too: a mistyped <c>"exclusion"</c> must not silently drop the
/// user's exclusions while cleanup carries on.</item>
/// <item>Saving validates first (nothing invalid is ever written) and writes atomically: a complete temporary file
/// replaces the old one, so a crash can't leave half-written settings. This file is the only place allowed to
/// move, copy or replace a file (<c>ForbiddenApiTests</c>), and only ever its own settings file.</item>
/// </list>
/// </summary>
public sealed class UserSettingsStore : ISettingsService
{
    /// <summary>Added to the settings file's name for the copy kept when an unusable file is replaced.</summary>
    public const string UnreadableCopySuffix = ".unreadable";

    private const long MaxFileBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ILogger<UserSettingsStore> _logger;
    private readonly Lock _gate = new();
    private readonly bool _writingAllowed;

    /// <param name="path">The settings file.</param>
    /// <param name="logger">The log.</param>
    /// <param name="writingAllowed">False while running as administrator: saving is refused (docs/SECURITY.md R8).</param>
    public UserSettingsStore(string path, ILogger<UserSettingsStore> logger, bool writingAllowed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _logger = logger;
        _writingAllowed = writingAllowed;
        (Current, LoadProblem) = Load();
    }

    /// <summary>%LOCALAPPDATA%\SafeClean\settings.json (outside the Temp folder SafeClean cleans).</summary>
    public static string DefaultPath { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SafeClean", "settings.json");

    public event EventHandler? Changed;

    public UserSettings Current { get; private set; }

    public string? LoadProblem { get; private set; }

    public SettingsValidation Save(UserSettings proposed)
    {
        if (!_writingAllowed)
        {
            return new SettingsValidation(null, ["Settings can't be saved while SafeClean runs as administrator. Start it normally to change them."]);
        }

        var validation = SettingsPolicy.Validate(proposed);
        if (!validation.IsValid)
        {
            return validation;
        }

        var settings = validation.Settings!;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, SettingsFile.From(settings), Json);
                }

                // The file that couldn't be used may hold the user's exclusions: keep a copy, don't overwrite the only one.
                if (LoadProblem is not null && File.Exists(_path))
                {
                    File.Copy(_path, _path + UnreadableCopySuffix, overwrite: true);
                }

                // Atomic switch: readers see either the old complete file or the new complete file.
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Settings could not be saved.");
                return new SettingsValidation(null, ["The settings couldn't be saved: " + ex.Message]);
            }

            Current = settings;
            LoadProblem = null;
        }

        _logger.LogInformation(
            "Settings saved: minimum age {Days} days, {Exclusions} exclusions, log level {Level}, log retention {Retention} days.",
            settings.MinimumFileAgeDays,
            settings.Exclusions.Count,
            settings.LogLevel,
            settings.LogRetentionDays);
        Changed?.Invoke(this, EventArgs.Empty);
        return validation;
    }

    private (UserSettings Settings, string? Problem) Load()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
            {
                return (UserSettings.Default, null);
            }

            if (info.Length > MaxFileBytes)
            {
                return Unusable("the settings file is unexpectedly large");
            }

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var file = JsonSerializer.Deserialize<SettingsFile>(stream, Json);
            if (file is null)
            {
                return Unusable("the settings file is empty");
            }

            var validation = SettingsPolicy.Validate(file.ToSettings());
            return validation.IsValid
                ? (validation.Settings!, null)
                : Unusable(string.Join(" ", validation.Errors));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Unusable("the settings file couldn't be read (" + ex.GetType().Name + ")");
        }
    }

    private (UserSettings, string) Unusable(string reason)
    {
        _logger.LogWarning("Settings not used: {Reason}. Safe defaults are in force and cleanup is paused until settings are saved.", reason);
        return (UserSettings.Default, $"Your saved settings couldn't be used ({reason}). Safe defaults are in force and cleanup is paused. Restarting SafeClean may fix this; otherwise check your exclusions below, because any you had aren't shown, and save again.");
    }

    /// <summary>What is stored on disk. Missing values mean "default"; wrong types make the whole file unusable.</summary>
    private sealed class SettingsFile
    {
        public int? MinimumFileAgeDays { get; set; }

        public List<string>? Exclusions { get; set; }

        public SettingsLogLevel? LogLevel { get; set; }

        public int? LogRetentionDays { get; set; }

        public static SettingsFile From(UserSettings settings) => new()
        {
            MinimumFileAgeDays = settings.MinimumFileAgeDays,
            Exclusions = [.. settings.Exclusions],
            LogLevel = settings.LogLevel,
            LogRetentionDays = settings.LogRetentionDays,
        };

        public UserSettings ToSettings() => new(
            MinimumFileAgeDays ?? UserSettings.Default.MinimumFileAgeDays,
            Exclusions ?? [],
            LogLevel ?? UserSettings.Default.LogLevel,
            LogRetentionDays ?? UserSettings.Default.LogRetentionDays);
    }
}
