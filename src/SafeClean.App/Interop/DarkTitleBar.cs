using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SafeClean.App.Interop;

/// <summary>
/// Asks Windows to draw this window's own title bar in dark colours. Purely cosmetic: affects only
/// SafeClean's window, changes no system setting, and is silently ignored on Windows versions without it.
/// </summary>
internal static class DarkTitleBar
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 version 2004 and later, Windows 11)
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void TryApply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
