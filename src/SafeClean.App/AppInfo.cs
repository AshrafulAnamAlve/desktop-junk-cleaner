namespace SafeClean.App;

/// <summary>Read-only facts about this running copy of SafeClean, shown in the UI.</summary>
public sealed record AppInfo(string Version, string LogDirectory);
