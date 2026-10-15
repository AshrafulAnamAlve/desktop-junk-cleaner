using SafeClean.App.Mvvm;

namespace SafeClean.App.ViewModels;

/// <summary>A screen reachable from the sidebar. The title and subtitle appear in the page header.</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    public abstract string Subtitle { get; }
}
