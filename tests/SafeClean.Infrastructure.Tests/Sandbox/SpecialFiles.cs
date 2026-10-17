using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SafeClean.Infrastructure.Tests.Sandbox;

/// <summary>Gives sandbox files unusual properties for tests — directly through Windows, no shell commands.</summary>
internal static class SpecialFiles
{
    private const uint FsctlSetSparse = 0x000900C4;

    /// <summary>Marks a sandbox file as sparse (an attribute SafeClean must refuse).</summary>
    public static void MakeSparse(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (!DeviceIoControl(stream.SafeFileHandle, FsctlSetSparse, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not mark the file as sparse.");
        }
    }

    /// <summary>The 8.3 short name Windows generated for a file (e.g. "LONG-N~1.TMP"), or null if the volume has none.</summary>
    public static string? ShortName(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(@"\\?\" + path, buffer, (uint)buffer.Length);
        if (length == 0 || length >= buffer.Length)
        {
            return null;
        }

        var shortName = Path.GetFileName(new string(buffer, 0, (int)length));
        return string.Equals(shortName, Path.GetFileName(path), StringComparison.Ordinal) ? null : shortName;
    }

    /// <summary>Waits until no other program (for example antivirus) has the file open, so a test starts from a known state.</summary>
    public static async Task WaitUntilNotInUseAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return;
                }
            }
            catch (IOException) when (attempt < 300)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inBuffer, int inBufferSize, IntPtr outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);
}
