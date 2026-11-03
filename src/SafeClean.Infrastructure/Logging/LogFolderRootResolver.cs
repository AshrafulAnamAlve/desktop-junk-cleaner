using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Scanning;

namespace SafeClean.Infrastructure.Logging;

/// <summary>
/// Approves SafeClean's own log folder as a cleanup root for <see cref="LogMaintenance"/>, through the same
/// <see cref="RootPolicy"/> as the Temp folder (fixed NTFS drive, no links, not protected…). The folder is
/// compared with itself instead of <c>%LOCALAPPDATA%\Temp</c>, and must be SafeClean's log folder by path.
/// </summary>
public sealed class LogFolderRootResolver(string logDirectory, FileInspector inspector, ILogger<LogFolderRootResolver> logger) : ITempRootResolver
{
    public RootResolution Resolve()
    {
        var protectedLocations = KnownProtectedLocations.Create(logger);
        var folder = WindowsPath.TrimTrailingSeparator(logDirectory ?? string.Empty);

        // Not SafeClean's log folder → compare with nothing, so the identity can't match and RootPolicy refuses.
        var expected = LogMaintenance.IsSafeCleanLogFolder(folder) ? folder : string.Empty;
        var decision = RootPolicy.Evaluate(RootFactsReader.Read(folder, expected, inspector), protectedLocations);
        if (!decision.IsApproved)
        {
            logger.LogWarning("Log folder refused for log maintenance: {Problems}.", string.Join(", ", decision.Problems));
        }

        return new RootResolution(decision, protectedLocations);
    }
}
