using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Native;
using static SafeClean.Infrastructure.Native.NativeMethods;

namespace SafeClean.Infrastructure.Scanning;

/// <summary>
/// Asks Windows about one file or folder through a <b>read-only</b> handle:
/// <list type="bullet">
/// <item>access is only "read attributes" — not contents, not write, not delete;</item>
/// <item>other programs may keep reading, writing and deleting while we look;</item>
/// <item>links are opened as themselves, never followed to their target.</item>
/// </list>
/// </summary>
public sealed class FileInspector
{
    /// <summary>The only access right this class ever requests.</summary>
    internal const uint DesiredAccess = FILE_READ_ATTRIBUTES;

    internal const uint ShareMode = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;

    public InspectionResult InspectFile(string path) => Inspect(path, isDirectory: false);

    public InspectionResult InspectDirectory(string path) => Inspect(path, isDirectory: true);

    private static InspectionResult Inspect(string path, bool isDirectory)
    {
        // Only canonical paths reach Windows; anything odd is refused before any system call.
        if (!PathValidator.IsValid(path))
        {
            return InspectionResult.InvalidPath();
        }

        var flags = FILE_FLAG_OPEN_REPARSE_POINT | (isDirectory ? FILE_FLAG_BACKUP_SEMANTICS : 0);
        using var handle = CreateFile(ExtendedPath.Add(path), DesiredAccess, ShareMode, IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return InspectionResult.Failed(Marshal.GetLastPInvokeError());
        }

        return ReadFacts(handle, path, isDirectory);
    }

    /// <summary>
    /// Reads the facts through a handle that is already open. Only asks questions; changes nothing.
    /// The cleanup engine uses this to check a file through its own exclusive handle.
    /// </summary>
    internal static InspectionResult ReadFacts(SafeFileHandle handle, string path, bool isDirectory)
    {
        if (!HandleQueries.TryGet(handle, FileInfoClass.FileBasicInfo, out FILE_BASIC_INFO basic)
            || !HandleQueries.TryGet(handle, FileInfoClass.FileStandardInfo, out FILE_STANDARD_INFO standard)
            || !HandleQueries.TryGet(handle, FileInfoClass.FileIdInfo, out FILE_ID_INFO id))
        {
            return InspectionResult.Failed(Marshal.GetLastPInvokeError());
        }

        var finalPath = HandleQueries.TryGetFinalPath(handle);
        if (finalPath is null)
        {
            return InspectionResult.Failed(Marshal.GetLastPInvokeError());
        }

        var attributes = (FileAttributes)basic.FileAttributes;
        if (standard.Directory != 0)
        {
            attributes |= FileAttributes.Directory;
        }

        var facts = new FileFacts(
            Path: path,
            FinalPath: ExtendedPath.Remove(finalPath),
            Attributes: attributes,
            Size: standard.EndOfFile,
            CreationTimeUtc: HandleQueries.FromFileTime(basic.CreationTime),
            LastWriteTimeUtc: HandleQueries.FromFileTime(basic.LastWriteTime),
            Identity: new FileIdentity(id.VolumeSerialNumber, new UInt128(id.FileIdHigh, id.FileIdLow)),
            // A file already marked for deletion is reported with 0 links, which the policy skips.
            LinkCount: standard.DeletePending != 0 ? 0 : standard.NumberOfLinks);

        return InspectionResult.Success(facts, isDirectory ? HandleQueries.TryGetFileSystemName(handle) : null);
    }
}
