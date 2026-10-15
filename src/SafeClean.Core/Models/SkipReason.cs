namespace SafeClean.Core.Models;

/// <summary>Why a file is not offered for cleanup. Rule codes refer to docs/SECURITY.md §4.</summary>
public enum SkipReason
{
    /// <summary>C8: the path contains parts Windows treats specially (traversal, streams, device names…).</summary>
    InvalidPath,

    /// <summary>C5: the file is not strictly inside the approved root.</summary>
    OutsideRoot,

    /// <summary>C5: Windows reports a different location than where the file was found.</summary>
    PathMismatch,

    /// <summary>C2: it is a folder; only files are ever removed.</summary>
    IsDirectory,

    /// <summary>C3: symlink, junction, mount point, cloud placeholder or other redirection.</summary>
    ReparsePoint,

    /// <summary>C4: has an attribute outside the allow-list (read-only, hidden, system, offline…).</summary>
    DisallowedAttributes,

    /// <summary>C6: more than one hard link, or zero (already being deleted).</summary>
    UnexpectedLinkCount,

    /// <summary>C7: created or modified within the minimum age.</summary>
    TooRecent,

    /// <summary>C7: a timestamp is in the future or implausibly old.</summary>
    UntrustworthyTimestamp,

    /// <summary>C9: inside a protected location.</summary>
    Protected,

    /// <summary>C9: matches a user exclusion.</summary>
    Excluded,

    /// <summary>C1: nested deeper than the scan depth limit.</summary>
    TooDeep,

    /// <summary>The reported size is impossible (negative).</summary>
    UnexpectedSize,

    /// <summary>Windows did not let SafeClean read the file's details.</summary>
    Inaccessible,
}
