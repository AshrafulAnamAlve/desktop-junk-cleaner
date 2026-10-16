using SafeClean.Core.Policies;

namespace SafeClean.Core.Models;

/// <summary>The decision about the Temp folder, together with the protected locations it was checked against.</summary>
public sealed record RootResolution(RootDecision Decision, ProtectedLocations ProtectedLocations);
