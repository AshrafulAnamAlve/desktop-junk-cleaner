using SafeClean.Core.Models;
using SafeClean.Core.Policies;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>
/// Gathers the facts <see cref="RootPolicy"/> needs about a folder. It only reads.
/// The same code is used for the real Temp folder and for test sandboxes, so tests exercise the real rules.
/// </summary>
internal static class RootFactsReader
{
    /// <param name="requestedPath">The folder to check (for the app: <c>Path.GetTempPath()</c>).</param>
    /// <param name="expectedPath">The folder it must be identical to (for the app: <c>%LOCALAPPDATA%\Temp</c>).</param>
    public static RootFacts Read(string requestedPath, string expectedPath, FileInspector inspector)
    {
        ArgumentNullException.ThrowIfNull(inspector);

        var requested = WindowsPath.TrimTrailingSeparator(requestedPath ?? string.Empty);
        var expected = WindowsPath.TrimTrailingSeparator(expectedPath ?? string.Empty);

        var root = inspector.InspectDirectory(requested);
        var expectedFolder = inspector.InspectDirectory(expected);
        var finalPath = root.Facts?.FinalPath ?? string.Empty;

        return new RootFacts(
            RequestedPath: requested,
            FinalPath: finalPath,
            Attributes: root.Facts?.Attributes ?? 0,
            AnyAncestorIsReparsePoint: AnyAncestorIsReparsePoint(requested, inspector),
            IsFixedDrive: IsFixedDrive(finalPath),
            FileSystemName: root.FileSystemName ?? string.Empty,
            Identity: root.Facts?.Identity ?? default,
            ExpectedIdentity: expectedFolder.Facts?.Identity ?? default);
    }

    /// <summary>
    /// Checks every parent folder of the path as written (the final path can't reveal junctions, because
    /// Windows has already resolved them). A parent that can't be read counts as a reparse point.
    /// </summary>
    private static bool AnyAncestorIsReparsePoint(string path, FileInspector inspector)
    {
        if (!PathValidator.IsValid(path))
        {
            return false; // RootPolicy already refuses invalid paths; don't add a misleading second reason.
        }

        var current = path;
        while (true)
        {
            var separator = current.LastIndexOf('\\');
            if (separator <= 2)
            {
                return false; // reached the drive root
            }

            current = current[..separator];
            var parent = inspector.InspectDirectory(current);
            if (!parent.Succeeded || parent.Facts.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }
    }

    private static bool IsFixedDrive(string finalPath)
    {
        if (!WindowsPath.HasDriveRoot(finalPath))
        {
            return false;
        }

        try
        {
            return new DriveInfo(finalPath[..3]).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
