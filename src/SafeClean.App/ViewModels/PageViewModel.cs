using SafeClean.App.Mvvm;

namespace SafeClean.App.ViewModels;

/// <summary>A screen reachable from the sidebar. The title and subtitle appear in the page header.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    public abstract string Subtitle { get; }

    /// <summary>Called when the page becomes the visible one. Pages with live data start their refresh here.</summary>
    public virtual void OnShown()
    {
    }

    /// <summary>Called when another page is shown. Pages stop all background refreshing here (no hidden work).</summary>
    public virtual void OnHidden()
    {
    }
}
