using System.Windows;
using SafeClean.App.ViewModels;
using SafeClean.App.Views;

namespace SafeClean.App.Services;

/// <summary>Shows <see cref="ConfirmCleanupWindow"/> as a modal dialog over the main window.</summary>
public sealed class DialogCleanupConfirmation : ICleanupConfirmation
{
    public bool Confirm(CleanupConfirmationViewModel request)
    {
        var dialog = new ConfirmCleanupWindow(request) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true;
    }
}
