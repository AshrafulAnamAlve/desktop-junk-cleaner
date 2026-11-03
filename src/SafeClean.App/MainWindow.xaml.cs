using System.Windows;
using SafeClean.App.Interop;
using SafeClean.App.Presentation;
using SafeClean.App.ViewModels;

namespace SafeClean.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        FitToWorkArea();
        SourceInitialized += (_, _) => DarkTitleBar.TryApply(this);

        // Pages with live figures refresh only while they are visible and the window isn't minimised.
        Loaded += (_, _) => viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
        StateChanged += (_, _) => viewModel.SetWindowVisible(WindowState != WindowState.Minimized);
        Closed += (_, _) => viewModel.SetWindowVisible(false);

        // A running cleanup stops and reports first; see MainViewModel.CanCloseNow.
        Closing += (_, e) => e.Cancel = !viewModel.CanCloseNow();
    }

    /// <summary>Keeps the window above the taskbar on small or highly scaled screens (e.g. 1366×768).</summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        (Width, MinWidth) = WindowFit.Fit(Width, MinWidth, area.Width);
        (Height, MinHeight) = WindowFit.Fit(Height, MinHeight, area.Height);
    }
}
