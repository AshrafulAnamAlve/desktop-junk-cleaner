using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace SafeClean.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    // Copies the log folder path so it can be pasted into File Explorer (SafeClean doesn't launch other programs).
    private void OnCopyLogPath(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LogPath.Text);
        }
        catch (ExternalException)
        {
            // Another program is using the clipboard; the path can still be selected and copied by hand.
        }
    }
}
