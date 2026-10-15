namespace SafeClean.App.ViewModels;

/// <summary>Performance page. Read-only monitoring arrives in a later phase.</summary>
public sealed class PerformanceViewModel : PageViewModel
{
    public override string Title => "Performance";

    public override string Subtitle => "A read-only view of memory, processor and disk usage.";
}
