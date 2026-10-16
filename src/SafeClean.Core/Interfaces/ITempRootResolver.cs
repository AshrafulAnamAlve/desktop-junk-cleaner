using SafeClean.Core.Models;

namespace SafeClean.Core.Interfaces;

/// <summary>Finds the user's Temp folder and decides (through <see cref="Policies.RootPolicy"/>) whether it may be scanned.</summary>
public interface ITempRootResolver
{
    RootResolution Resolve();
}
