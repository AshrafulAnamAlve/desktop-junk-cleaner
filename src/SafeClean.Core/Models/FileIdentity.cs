namespace SafeClean.Core.Models;

/// <summary>
/// Uniquely identifies a file or folder on this PC: the volume serial number plus the 128-bit file ID
/// that NTFS assigns. A file that is deleted and recreated with the same name gets a new ID, which is
/// how SafeClean notices a file was replaced.
/// </summary>
public readonly record struct FileIdentity(ulong VolumeSerialNumber, UInt128 FileId)
{
    /// <summary>All zeros: the identity could not be read.</summary>
    public bool IsUnknown => VolumeSerialNumber == 0 && FileId == UInt128.Zero;

    /// <summary>
    /// Safe comparison: true only if both identities are known and equal.
    /// Two unknown identities never match each other.
    /// </summary>
    public bool Matches(FileIdentity other) => !IsUnknown && !other.IsUnknown && this == other;
}
