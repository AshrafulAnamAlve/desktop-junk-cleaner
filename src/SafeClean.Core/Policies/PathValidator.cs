using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SafeClean.Core.Models;

namespace SafeClean.Core.Policies;

/// <summary>
/// Accepts only plain, canonical local drive paths such as <c>C:\Users\Name\AppData\Local\Temp\a.tmp</c>.
/// Anything Windows could interpret in a surprising way is rejected (rule C8 in docs/SECURITY.md).
/// The validator never "fixes" a path: an unusual path is refused, not repaired.
/// </summary>
public static class PathValidator
{
    /// <summary>Longest path Windows supports (with the extended-length prefix).</summary>
    public const int MaxPathLength = 32_767;

    /// <summary>Longest single file or folder name NTFS allows.</summary>
    public const int MaxSegmentLength = 255;

    private static readonly SearchValues<char> InvalidNameCharacters = SearchValues.Create("<>\"|?*");

    private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$"];

    public static bool IsValid([NotNullWhen(true)] string? path) => FindProblem(path) is null;

    /// <summary>Returns the first problem found, or null if the path is acceptable.</summary>
    public static PathProblem? FindProblem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PathProblem.Empty;
        }

        if (path.Length > MaxPathLength)
        {
            return PathProblem.TooLong;
        }

        // \\?\C:\..., \\.\C:\..., \??\... reach Windows internals directly and skip normal path rules.
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal)
            || path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return PathProblem.DevicePath;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return PathProblem.NetworkPath;
        }

        // Windows paths produced by Windows itself always use backslashes.
        if (path.Contains('/'))
        {
            return PathProblem.ForwardSlash;
        }

        // Rejects relative ("a.tmp"), drive-relative ("C:a.tmp") and root-relative ("\Temp") paths.
        if (!WindowsPath.HasDriveRoot(path))
        {
            return PathProblem.NotFullyQualified;
        }

        if (WindowsPath.IsDriveRoot(path))
        {
            return null;
        }

        var rest = path.AsSpan(3);
        foreach (var range in rest.Split('\\'))
        {
            var problem = FindSegmentProblem(rest[range]);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    private static PathProblem? FindSegmentProblem(ReadOnlySpan<char> segment)
    {
        // Empty segments come from "\\" in the middle or a trailing "\".
        if (segment.IsEmpty)
        {
            return PathProblem.EmptySegment;
        }

        if (segment.Length > MaxSegmentLength)
        {
            return PathProblem.SegmentTooLong;
        }

        if (segment is "." or "..")
        {
            return PathProblem.DotSegment;
        }

        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            if (c < '\u0020')
            {
                return PathProblem.ControlCharacter;
            }

            // A colon after the drive letter selects an alternate data stream ("file.txt:hidden").
            if (c == ':')
            {
                return PathProblem.AlternateDataStream;
            }

            if (InvalidNameCharacters.Contains(c))
            {
                return PathProblem.InvalidCharacter;
            }

            // A lone half of a surrogate pair is not valid text and could be mangled in conversions.
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= segment.Length || !char.IsLowSurrogate(segment[i + 1]))
                {
                    return PathProblem.InvalidCharacter;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return PathProblem.InvalidCharacter;
            }
        }

        // Windows silently strips trailing dots and spaces, so "a.tmp." could refer to "a.tmp".
        if (segment[^1] is '.' or ' ')
        {
            return PathProblem.TrailingDotOrSpace;
        }

        if (IsReservedDeviceName(segment))
        {
            return PathProblem.ReservedDeviceName;
        }

        return null;
    }

    /// <summary>
    /// Names such as CON, NUL, COM1 or LPT1 refer to devices, even with an extension ("NUL.txt")
    /// or spaces before the extension ("CON .txt").
    /// </summary>
    private static bool IsReservedDeviceName(ReadOnlySpan<char> segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');

        foreach (var device in DeviceNames)
        {
            if (stem.Equals(device, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
        {
            // COM0–COM9 and LPT0–LPT9, plus the superscript digits ¹ ² ³ that Windows also treats as devices.
            return stem[3] is (>= '0' and <= '9') or '\u00B9' or '\u00B2' or '\u00B3';
        }

        return false;
    }
}
