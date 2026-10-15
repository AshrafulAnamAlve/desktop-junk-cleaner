namespace SafeClean.App.ViewModels;

/// <summary>Settings page. Shows fixed safety settings and where logs are stored.</summary>
public sealed class SettingsViewModel(AppInfo info) : PageViewModel
{
    public override string Title => "Settings";

    public override string Subtitle => "How SafeClean behaves on this PC.";

    public string Version { get; } = info.Version;

    public string LogDirectory { get; } = info.LogDirectory;
}
