using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SafeClean.Infrastructure.Native;

/// <summary>
/// The Windows functions SafeClean calls directly. They live in DLLs that ship with Windows and are
/// always loaded from System32, so a planted DLL elsewhere can't be picked up.
/// Only the read-only access right is defined here; nothing in this file can write or delete.
/// </summary>
internal static unsafe partial class NativeMethods
{
    // Access right: read a file's attributes, times, size and ID — not its contents.
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;

    // Share modes: let every other program keep reading, writing or deleting while we look.
    internal const uint FILE_SHARE_READ = 0x1;
    internal const uint FILE_SHARE_WRITE = 0x2;
    internal const uint FILE_SHARE_DELETE = 0x4;

    internal const uint OPEN_EXISTING = 3;

    // Needed to open a folder (grants no extra rights to a normal user).
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    // Open a link itself instead of following it to its target.
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    // GetFinalPathNameByHandle: normalised long names, with a drive letter.
    internal const uint FILE_NAME_NORMALIZED = 0x0;
    internal const uint VOLUME_NAME_DOS = 0x0;

    // SHGetKnownFolderPath: return the path even if the folder doesn't exist yet.
    internal const uint KF_FLAG_DONT_VERIFY = 0x4000;

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_SHARING_VIOLATION = 32;
    internal const int ERROR_LOCK_VIOLATION = 33;
    internal const int ERROR_DELETE_PENDING = 303;

    internal enum FileInfoClass
    {
        FileBasicInfo = 0,
        FileStandardInfo = 1,
        FileIdInfo = 18,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_BASIC_INFO
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_STANDARD_INFO
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_ID_INFO
    {
        public ulong VolumeSerialNumber;

        // FILE_ID_128 is 16 bytes, little-endian: low half first.
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandleEx(SafeFileHandle file, FileInfoClass infoClass, void* info, uint bufferSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char* buffer, uint bufferLength, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVolumeInformationByHandleW(
        SafeFileHandle file,
        char* volumeName,
        uint volumeNameSize,
        uint* serialNumber,
        uint* maxComponentLength,
        uint* fileSystemFlags,
        char* fileSystemName,
        uint fileSystemNameSize);

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
