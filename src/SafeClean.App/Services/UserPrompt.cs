using System.Windows;

namespace SafeClean.App.Services;

/// <summary>Asks a yes/no question. "No" is the default answer, so Enter or Esc never confirms.</summary>
public interface IUserPrompt
{
    bool Confirm(string title, string message);
}

/// <summary>A standard Windows message box with Yes/No buttons and "No" as the default.</summary>
public sealed class MessageBoxPrompt : IUserPrompt
{
    public bool Confirm(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var answer = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }
}
