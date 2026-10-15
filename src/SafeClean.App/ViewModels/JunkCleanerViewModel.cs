using SafeClean.Core.Policies;

namespace SafeClean.App.ViewModels;

/// <summary>Junk Cleaner page. Scanning arrives in a later phase; this page explains what it will do.</summary>
public sealed class JunkCleanerViewModel : PageViewModel
{
    public override string Title => "Junk Cleaner";

    public override string Subtitle => "Find temporary files that are safe to remove.";

    public string AgeRuleText { get; } =
        $"Created and last changed more than {SafetyDefaults.MinimumFileAge.TotalDays:0} days ago.";
}
