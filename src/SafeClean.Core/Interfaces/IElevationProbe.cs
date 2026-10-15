namespace SafeClean.Core.Interfaces;

/// <summary>Reports whether SafeClean is running with administrator rights.</summary>
/// <remarks>Cleanup is refused while elevated (rule R8 in docs/SECURITY.md).</remarks>
public interface IElevationProbe
{
    bool IsElevated { get; }
}
