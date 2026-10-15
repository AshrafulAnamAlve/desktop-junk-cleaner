using System.Windows;
using SafeClean.App.Interop;
using SafeClean.App.ViewModels;

namespace SafeClean.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => DarkTitleBar.TryApply(this);
    }
}
