namespace SafeClean.App.ViewModels;

/// <summary>One entry in the sidebar.</summary>
public sealed class NavigationItem(string title, string glyph, PageViewModel page)
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public PageViewModel Page { get; } = page;
}
