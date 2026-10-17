using System.Windows;
using SafeClean.App.Interop;
using SafeClean.App.ViewModels;

namespace SafeClean.App.Views;

/// <summary>
/// The last step before a real cleanup. Cancel is the default button and has focus, so Enter, Esc and
/// closing the window all mean "no". Only a deliberate press of the red button means "yes".
/// There is no "don't ask again" option.
/// </summary>
public partial class ConfirmCleanupWindow : Window
{
    public ConfirmCleanupWindow(CleanupConfirmationViewModel request)
    {
        InitializeComponent();
        DataContext = request;
        SourceInitialized += (_, _) => DarkTitleBar.TryApply(this);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
