using System.Diagnostics.CodeAnalysis;
using SafeClean.Core.Models;
using SafeClean.Infrastructure.Native;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>What <see cref="FileInspector"/> found out, or why it couldn't.</summary>
public sealed class InspectionResult
{
    private InspectionResult(FileFacts? facts, string? fileSystemName, SkipReason failureReason, int win32Error)
    {
        Facts = facts;
        FileSystemName = fileSystemName;
        FailureReason = failureReason;
        Win32Error = win32Error;
    }

    public FileFacts? Facts { get; }

    /// <summary>File system of the volume (only filled in for folders), e.g. "NTFS".</summary>
    public string? FileSystemName { get; }

    /// <summary>Why inspection failed (<see cref="SkipReason.InvalidPath"/> or <see cref="SkipReason.Inaccessible"/>).</summary>
    public SkipReason FailureReason { get; }

    /// <summary>The Windows error code, or 0.</summary>
    public int Win32Error { get; }

    [MemberNotNullWhen(true, nameof(Facts))]
    public bool Succeeded => Facts is not null;

    /// <summary>The entry no longer exists (it was removed by someone else after it was listed).</summary>
    public bool IsMissing => Win32Error is NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND;

    internal static InspectionResult Success(FileFacts facts, string? fileSystemName) => new(facts, fileSystemName, SkipReason.Inaccessible, 0);

    internal static InspectionResult InvalidPath() => new(null, null, SkipReason.InvalidPath, 0);

    internal static InspectionResult Failed(int win32Error) => new(null, null, SkipReason.Inaccessible, win32Error);
}
