#if DEBUG
using Microsoft.Extensions.Logging;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>
/// <b>DEBUG BUILDS ONLY</b> (a test checks the <c>#if DEBUG</c> guard). Points SafeClean at a test folder
/// instead of the real Temp folder, so cleanup can be tried by hand on synthetic files.
/// <list type="bullet">
/// <item>The folder must contain a plain file named <see cref="MarkerFileName"/>; without it the folder is refused.</item>
/// <item>Every root rule still applies, except "must be %LOCALAPPDATA%\Temp": the folder is compared with itself instead.</item>
/// <item>There is no fallback: a refused sandbox never turns into a scan of the real Temp folder.</item>
/// </list>
/// </summary>
public sealed class DebugSandboxRootResolver(string sandboxRoot, FileInspector inspector, ILogger<DebugSandboxRootResolver> logger) : ITempRootResolver
{
    /// <summary>The marker file that must exist in a folder before it can be used as a sandbox.</summary>
    public const string MarkerFileName = ".safeclean-sandbox";

    public string SandboxRoot { get; } = WindowsPath.TrimTrailingSeparator(sandboxRoot ?? string.Empty);

    public RootResolution Resolve()
    {
        var protectedLocations = KnownProtectedLocations.Create(logger);

        // Without a valid marker, compare the folder with nothing: an unknown identity never matches, so RootPolicy refuses.
        var expected = HasMarker() ? SandboxRoot : string.Empty;
        var decision = RootPolicy.Evaluate(RootFactsReader.Read(SandboxRoot, expected, inspector), protectedLocations);

        logger.LogWarning(
            "DEBUG SANDBOX MODE: the sandbox folder was {Result}.",
            decision.IsApproved ? "approved" : "refused (" + string.Join(", ", decision.Problems) + ")");
        return new RootResolution(decision, protectedLocations);
    }

    /// <summary>True if the sandbox folder contains the marker as a plain file (not a folder or a link).</summary>
    public bool HasMarker()
    {
        var marker = inspector.InspectFile(SandboxRoot + @"\" + MarkerFileName);
        return marker.Succeeded && (marker.Facts.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }
}
#endif
