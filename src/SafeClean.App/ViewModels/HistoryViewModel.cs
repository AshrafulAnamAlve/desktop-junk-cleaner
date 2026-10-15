namespace SafeClean.App.ViewModels;

/// <summary>Cleanup history page. Empty until cleanup and history recording exist.</summary>
public sealed class HistoryViewModel : PageViewModel
{
    public override string Title => "Cleanup History";

    public override string Subtitle => "A record of past cleanups on this PC.";
}
