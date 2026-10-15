using SafeClean.Core.Interfaces;

namespace SafeClean.App.ViewModels;

/// <summary>
/// Overview page. Monitoring and scan figures are not connected yet, so the page shows honest
/// placeholders instead of numbers. Only the safety status uses real data.
/// </summary>
public sealed class DashboardViewModel(IElevationProbe elevation) : PageViewModel
{
    public override string Title => "Dashboard";

    public override string Subtitle => "An overview of your PC's storage, memory and safety status.";

    public bool IsElevated { get; } = elevation.IsElevated;

    public string RightsText => IsElevated
        ? "Running as administrator: cleanup will be disabled. Restart SafeClean normally."
        : "Running with normal user rights, as intended.";
}
