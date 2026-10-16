using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SafeClean.Infrastructure.Tests.Sandbox;

/// <summary>
/// Creates junctions, hard links and symbolic links for tests — directly through Windows, no shell commands.
/// Callers only ever point links at other folders inside the same test sandbox.
/// </summary>
internal static class FileSystemLinks
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;

    /// <summary>Creates a directory junction (needs no special rights, unlike symbolic links).</summary>
    public static void CreateJunction(string linkPath, string targetPath)
    {
        var target = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(linkPath);

        var substituteName = Encoding.Unicode.GetBytes(@"\??\" + target);
        var printName = Encoding.Unicode.GetBytes(target);
        var pathBufferLength = substituteName.Length + 2 + printName.Length + 2;

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.Unicode, leaveOpen: true))
        {
            writer.Write(IoReparseTagMountPoint);
            writer.Write((ushort)(8 + pathBufferLength)); // ReparseDataLength
            writer.Write((ushort)0);                      // Reserved
            writer.Write((ushort)0);                      // SubstituteNameOffset
            writer.Write((ushort)substituteName.Length);
            writer.Write((ushort)(substituteName.Length + 2)); // PrintNameOffset
            writer.Write((ushort)printName.Length);
            writer.Write(substituteName);
            writer.Write((ushort)0);
            writer.Write(printName);
            writer.Write((ushort)0);
        }

        var data = buffer.ToArray();
        using var handle = CreateFile(@"\\?\" + Path.GetFullPath(linkPath), GenericWrite, 0, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the folder to turn it into a junction.");
        }

        if (!DeviceIoControl(handle, FsctlSetReparsePoint, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the junction.");
        }
    }

    public static void CreateHardLink(string newLinkPath, string existingFilePath)
    {
        if (!CreateHardLinkW(@"\\?\" + Path.GetFullPath(newLinkPath), @"\\?\" + Path.GetFullPath(existingFilePath), IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the hard link.");
        }
    }

    /// <summary>Creates a file symbolic link, or skips the calling test if Windows doesn't allow it here.</summary>
    public static void CreateFileSymbolicLinkOrSkip(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Creating symbolic links needs Developer Mode or administrator rights on this PC ({ex.Message.Trim()}).");
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr securityAttributes, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, byte[] inBuffer, int inBufferSize, IntPtr outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);
}
