using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>
/// Finds the current user's Temp folder (rule R1: from <see cref="Path.GetTempPath"/>, never hard-coded)
/// and lets <see cref="RootPolicy"/> decide whether it may be scanned. Read-only.
/// </summary>
public sealed class TempRootResolver(FileInspector inspector, ILogger<TempRootResolver> logger) : ITempRootResolver
{
    public RootResolution Resolve()
    {
        var protectedLocations = KnownProtectedLocations.Create(logger);
        var userTemp = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData, System.Environment.SpecialFolderOption.DoNotVerify),
            "Temp");

        var facts = RootFactsReader.Read(Path.GetTempPath(), userTemp, inspector);
        var decision = RootPolicy.Evaluate(facts, protectedLocations);

        if (decision.IsApproved)
        {
            logger.LogInformation("Temp folder approved for scanning.");
        }
        else
        {
            logger.LogWarning("Temp folder refused: {Problems}.", string.Join(", ", decision.Problems));
        }

        return new RootResolution(decision, protectedLocations);
    }
}
