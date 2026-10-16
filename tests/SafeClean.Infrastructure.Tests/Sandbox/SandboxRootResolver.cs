using Microsoft.Extensions.Logging.Abstractions;
using SafeClean.Core.Interfaces;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Scanning;

namespace SafeClean.Infrastructure.Tests.Sandbox;

/// <summary>
/// Points the scanner at a sandbox folder. It goes through exactly the same <see cref="RootFactsReader"/>
/// and <see cref="RootPolicy"/> as the real app; the only difference is that the "expected" folder is the
/// sandbox itself instead of %LOCALAPPDATA%\Temp. There is no bypass.
/// </summary>
internal sealed class SandboxRootResolver(string rootPath, ProtectedLocations? protectedLocations = null) : ITempRootResolver
{
    public RootResolution Resolve()
    {
        var locations = protectedLocations ?? KnownProtectedLocations.Create(NullLogger.Instance);
        var facts = RootFactsReader.Read(rootPath, rootPath, new FileInspector());
        return new RootResolution(RootPolicy.Evaluate(facts, locations), locations);
    }
}

/// <summary>Reports progress immediately on the calling thread (unlike <see cref="Progress{T}"/>).</summary>
internal sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}
