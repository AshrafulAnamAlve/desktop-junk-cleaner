namespace SafeClean.App.Presentation;

/// <summary>
/// Keeps the main window inside the screen's usable area (above the taskbar). On small or highly scaled
/// screens (for example 1366×768, or any screen at 150%) the preferred size and even the minimum size can
/// be larger than the usable area, which would push the bottom of the window off-screen.
/// </summary>
public static class WindowFit
{
    /// <summary>Returns the size and minimum size to use for one dimension.</summary>
    /// <param name="preferred">The size the window would like.</param>
    /// <param name="minimum">The smallest size the layout is designed for.</param>
    /// <param name="available">The usable screen area (0 or less when unknown).</param>
    public static (double Size, double Minimum) Fit(double preferred, double minimum, double available)
    {
        if (available <= 0 || double.IsNaN(available) || double.IsInfinity(available))
        {
            return (preferred, minimum);
        }

        var fittedMinimum = Math.Min(minimum, available);
        return (Math.Clamp(preferred, fittedMinimum, available), fittedMinimum);
    }
}
