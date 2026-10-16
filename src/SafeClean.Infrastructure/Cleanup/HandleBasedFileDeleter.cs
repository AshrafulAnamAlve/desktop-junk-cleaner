using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SafeClean.Core.Models;
using SafeClean.Core.Policies;
using SafeClean.Infrastructure.Native;
using SafeClean.Infrastructure.Scanning;
using static SafeClean.Infrastructure.Native.NativeMethods;

namespace SafeClean.Infrastructure.Cleanup;

/// <summary>
/// <b>The only code in SafeClean that can delete a file</b> (docs/SECURITY.md §5, steps D3–D6):
/// <list type="number">
/// <item>D3: open the file exclusively, so no other program can open, change, rename or replace it while we hold it;</item>
/// <item>D4: through that same handle, check it is still exactly the file the scan recorded;</item>
/// <item>D5: mark that open file for deletion (never a path);</item>
/// <item>D6: close the handle, so Windows removes the file; only then do its bytes count.</item>
/// </list>
/// Anything unexpected leaves the file alone. Nothing is retried, forced, or changed to make deletion possible.
/// Tests make sure the delete access right and the disposition call appear in no other source file.
/// </summary>
internal static partial class HandleBasedFileDeleter
{
    // The access right that allows marking a file for deletion.
    private const uint DELETE = 0x0001_0000;

    // Standard right included with file handles; it grants nothing extra over the file's contents.
    private const uint SYNCHRONIZE = 0x0010_0000;

    /// <summary>Delete + read attributes (for the D4 checks) + synchronize. No read or write of contents.</summary>
    internal const uint DesiredAccess = DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE;

    /// <summary>Share nothing: while the handle is open, nobody else can open the file to read, write, rename or delete it.</summary>
    internal const uint ShareMode = 0;

    /// <summary>Open a link as itself; no backup semantics, so a folder can't even be opened.</summary>
    internal const uint Flags = FILE_FLAG_OPEN_REPARSE_POINT;

    private const int FileDispositionInfoClass = 4;

    /// <summary>Size of the Windows structure: a single BOOLEAN.</summary>
    internal static readonly uint DispositionInfoSize = (uint)Marshal.SizeOf<FILE_DISPOSITION_INFO>();

    /// <summary>Deletes one scanned file if, and only if, it is still exactly the file that was scanned.</summary>
    /// <param name="candidate">A candidate from the scan session.</param>
    /// <param name="context">The rules as they apply now, with the freshly re-approved Temp folder.</param>
    public static AttemptResult TryDelete(ScanCandidate candidate, CandidatePolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        // D1/D2 again, so this method can never be pointed at anything the rules wouldn't select.
        if (CleanupPolicy.CheckRecorded(candidate, context) is { } refused)
        {
            return AttemptResult.Skipped(refused);
        }

        long size;
        using (var handle = CreateFile(ExtendedPath.Add(candidate.Path), DesiredAccess, ShareMode, IntPtr.Zero, OPEN_EXISTING, Flags, IntPtr.Zero))
        {
            // D3: in use, missing or access denied → skip. Other programs are never closed.
            if (handle.IsInvalid)
            {
                return AttemptResult.FromOpenError(Marshal.GetLastPInvokeError());
            }

            // D4: everything is read through this exclusive handle, so it describes the object we would delete.
            var current = FileInspector.ReadFacts(handle, candidate.Path, isDirectory: false);
            if (!current.Succeeded)
            {
                return AttemptResult.Failed(current.Win32Error);
            }

            if (CleanupPolicy.CheckCurrent(candidate, current.Facts, context) is { } changed)
            {
                return AttemptResult.Skipped(changed);
            }

            // D5: mark this handle's file for deletion. On failure: give up on this file, no retry, no force.
            var disposition = new FILE_DISPOSITION_INFO { MarkForDeletion = 1 };
            if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disposition, DispositionInfoSize))
            {
                return AttemptResult.Failed(Marshal.GetLastPInvokeError());
            }

            size = current.Facts.Size;
        }

        // D6: the handle is closed, so Windows removes the file. Only now does its size count as freed.
        return AttemptResult.Deleted(size);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_DISPOSITION_INFO
    {
        // A single BOOLEAN in the Windows headers. Named differently here so nothing in this file
        // resembles the forbidden path-based deletion function.
        public byte MarkForDeletion;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref FILE_DISPOSITION_INFO info, uint bufferSize);
}
