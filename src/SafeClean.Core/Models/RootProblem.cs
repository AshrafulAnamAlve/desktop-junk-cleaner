namespace SafeClean.Core.Models;

/// <summary>Why the Temp folder was not approved as the cleanup root. Rule codes refer to docs/SECURITY.md §2.</summary>
public enum RootProblem
{
    /// <summary>R2: the path is not a plain local drive path.</summary>
    InvalidPath,

    /// <summary>R3: not on an internal (fixed) drive.</summary>
    NotFixedDrive,

    /// <summary>R3: the drive is not formatted with NTFS.</summary>
    NotNtfs,

    /// <summary>R4: what was opened is not a folder.</summary>
    NotADirectory,

    /// <summary>R4: the folder itself is a symlink, junction or mount point.</summary>
    RootIsReparsePoint,

    /// <summary>R4: a parent folder is a symlink, junction or mount point.</summary>
    AncestorIsReparsePoint,

    /// <summary>R5: not the same folder as the user's own %LOCALAPPDATA%\Temp, or its identity is unknown.</summary>
    NotUserTempDirectory,

    /// <summary>R6: it is the top of a drive, such as C:\.</summary>
    IsDriveRoot,

    /// <summary>R6: it is itself a protected location.</summary>
    IsProtectedLocation,

    /// <summary>R6: it lies inside a protected location.</summary>
    InsideProtectedLocation,

    /// <summary>R6: a protected location lies inside it.</summary>
    ContainsProtectedLocation,
}
