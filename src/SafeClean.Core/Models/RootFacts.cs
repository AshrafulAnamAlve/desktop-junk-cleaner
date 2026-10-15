namespace SafeClean.Core.Models;

/// <summary>What SafeClean learned about the Temp folder before deciding whether to approve it as the cleanup root.</summary>
/// <param name="RequestedPath">The path Windows/.NET gave for the user's Temp folder (may use short 8.3 names).</param>
/// <param name="FinalPath">The path Windows reports for an opened handle to that folder.</param>
/// <param name="Attributes">The folder's own attributes.</param>
/// <param name="AnyAncestorIsReparsePoint">True if any parent folder is a symlink, junction or mount point.</param>
/// <param name="IsFixedDrive">True for an internal (fixed) drive; false for removable, network, CD, RAM disks.</param>
/// <param name="FileSystemName">File system of the volume, e.g. "NTFS".</param>
/// <param name="Identity">Identity of the folder that was opened.</param>
/// <param name="ExpectedIdentity">Identity of the user's own <c>%LOCALAPPDATA%\Temp</c> folder.</param>
public sealed record RootFacts(
    string RequestedPath,
    string FinalPath,
    FileAttributes Attributes,
    bool AnyAncestorIsReparsePoint,
    bool IsFixedDrive,
    string FileSystemName,
    FileIdentity Identity,
    FileIdentity ExpectedIdentity);
