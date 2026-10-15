using System.Security.Principal;
using SafeClean.Core.Interfaces;

namespace SafeClean.Infrastructure.Environment;

/// <summary>
/// Detects administrator rights. With User Account Control enabled, the Administrators group is only
/// active in an elevated ("Run as administrator") process, so this returns false for normal launches.
/// </summary>
public sealed class WindowsElevationProbe : IElevationProbe
{
    public bool IsElevated { get; } = Detect();

    private static bool Detect()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
