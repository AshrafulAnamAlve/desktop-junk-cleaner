namespace SafeClean.App;

/// <summary>Read-only facts about this running copy of SafeClean, shown in the UI.</summary>
/// <param name="Version">Version number.</param>
/// <param name="LogDirectory">Where the log files are written.</param>
/// <param name="SandboxRoot">Debug builds only: the test folder used instead of the real Temp folder, or null.</param>
public sealed record AppInfo(string Version, string LogDirectory, string? SandboxRoot = null)
{
    public bool IsSandbox => SandboxRoot is not null;
}
