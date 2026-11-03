using System.Globalization;
using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Cleanup;
using SafeClean.Infrastructure.Scanning;

namespace SafeClean.Infrastructure.Logging;

/// <summary>
/// "Delete old logs" (only when the user asks, after confirming). No new deletion code: the log folder is
/// approved by the same root rules as Temp, scanned read-only, judged by the same candidate rules, and
/// deleted by the same <see cref="CleanupService"/> — plus three extra narrowing rules:
/// <list type="number">
/// <item>the folder must be SafeClean's own log folder (its path ends in <c>\SafeClean\Logs</c>);</item>
/// <item>only files directly inside it named exactly <c>safeclean-YYYYMMDD.log</c>;</item>
/// <item>older than the retention by both the file's dates (minimum age) and the date in its name.</item>
/// </list>
/// While the user's saved settings can't be read, nothing is offered or deleted: the retention they chose is unknown.
/// </summary>
public sealed class LogMaintenance(
    string logDirectory,
    IElevationProbe elevationProbe,
    FileInspector inspector,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    IUserSettingsProvider? userSettings = null) : ILogMaintenance
{
    private const string Prefix = "safeclean-";
    private const string Extension = ".log";

    public async Task<LogCleanupPlan> FindOldLogsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        if (retentionDays is < SettingsPolicy.MinRetentionDays or > SettingsPolicy.MaxRetentionDays)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), retentionDays, "Retention must be between 7 and 90 days.");
        }

        if (!IsSafeCleanLogFolder(logDirectory))
        {
            return Nothing(retentionDays, "This isn't SafeClean's own log folder.");
        }

        if (userSettings?.LoadProblem is not null)
        {
            return Nothing(retentionDays, "Your saved settings couldn't be read, so the log retention you chose is unknown. Save your settings first.");
        }

        var scanner = new TempScanner(Resolver(), inspector, timeProvider, loggerFactory.CreateLogger<TempScanner>(), Settings(retentionDays));
        var result = await scanner.ScanAsync(null, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Nothing(retentionDays, "The log folder didn't pass the safety checks: " + string.Join(", ", result.RootDecision.Problems.Select(PolicyText.Describe)));
        }

        var newestAllowed = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date).AddDays(-retentionDays);
        var old = result.Session.Candidates
            .Where(c => c.Decision.IsSelectable && IsOldLogName(c.Decision.RelativePath, newestAllowed))
            .ToList();

        return new LogCleanupPlan(result.Session, old.Select(c => c.Id).ToList(), old.Sum(c => c.Size), retentionDays, null);
    }

    public Task<CleanupReport> DeleteOldLogsAsync(LogCleanupPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Session is null || plan.Ids.Count == 0)
        {
            throw new InvalidOperationException("There is nothing to delete in this plan.");
        }

        // Re-check the names here too: nothing outside the narrow rule can be passed in, even by mistake.
        var newestAllowed = DateOnly.FromDateTime(timeProvider.GetLocalNow().Date).AddDays(-plan.RetentionDays);
        var ids = plan.Ids
            .Where(id => plan.Session.Candidates.FirstOrDefault(c => c.Id == id) is { } c && IsOldLogName(c.Decision.RelativePath, newestAllowed))
            .ToList();

        var service = new CleanupService(Resolver(), elevationProbe, inspector, timeProvider, loggerFactory.CreateLogger<CleanupService>(), Settings(plan.RetentionDays));
        return service.CleanAsync(plan.Session, ids, CleanupMode.Real, null, cancellationToken);
    }

    /// <summary>SafeClean's own log folder ends in <c>\SafeClean\Logs</c>; anything else is refused.</summary>
    public static bool IsSafeCleanLogFolder(string folder) =>
        WindowsPath.TrimTrailingSeparator(folder ?? string.Empty).EndsWith(@"\SafeClean\Logs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True only for <c>safeclean-YYYYMMDD.log</c> directly in the folder (no sub-folder), with a real date
    /// on or before <paramref name="newestAllowed"/>.
    /// </summary>
    public static bool IsOldLogName(string? relativePath, DateOnly newestAllowed)
    {
        if (relativePath is null
            || relativePath.Length != Prefix.Length + 8 + Extension.Length
            || !relativePath.StartsWith(Prefix, StringComparison.Ordinal)
            || !relativePath.EndsWith(Extension, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = relativePath.AsSpan(Prefix.Length, 8);
        return DateOnly.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            && day <= newestAllowed;
    }

    private LogFolderRootResolver Resolver() => new(logDirectory, inspector, loggerFactory.CreateLogger<LogFolderRootResolver>());

    // The load problem is passed on, so the cleanup service refuses by itself if settings became unreadable.
    private FixedUserSettings Settings(int retentionDays) =>
        new(UserSettings.Default with { MinimumFileAgeDays = retentionDays }, userSettings?.LoadProblem);

    private static LogCleanupPlan Nothing(int retentionDays, string problem) => new(null, [], 0, retentionDays, problem);
}
