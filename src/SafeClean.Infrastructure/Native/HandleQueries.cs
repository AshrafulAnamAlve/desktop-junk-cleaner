using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static SafeClean.Infrastructure.Native.NativeMethods;

namespace SafeClean.Infrastructure.Native;

/// <summary>Safe wrappers that ask Windows about an already-open handle. They never change anything.</summary>
internal static unsafe class HandleQueries
{
    private const int MaxFinalPathLength = 32_768;
    private const int FileSystemNameLength = 261;

    public static bool TryGet<T>(SafeFileHandle handle, FileInfoClass infoClass, out T info)
        where T : unmanaged
    {
        T value = default;
        var ok = GetFileInformationByHandleEx(handle, infoClass, &value, (uint)sizeof(T));
        info = value;
        return ok;
    }

    /// <summary>The path Windows reports for the open handle (still with its <c>\\?\</c> prefix), or null.</summary>
    public static string? TryGetFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            uint length;
            fixed (char* pointer = buffer)
            {
                length = GetFinalPathNameByHandleW(handle, pointer, (uint)buffer.Length, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            }

            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, (int)length);
            }

            // Buffer too small: "length" is the size needed, including the terminating null.
            if (length > MaxFinalPathLength)
            {
                return null;
            }

            buffer = new char[length];
        }
    }

    /// <summary>File system of the volume the handle is on, e.g. "NTFS", or null.</summary>
    public static string? TryGetFileSystemName(SafeFileHandle handle)
    {
        var name = stackalloc char[FileSystemNameLength];
        return GetVolumeInformationByHandleW(handle, null, 0, null, null, null, name, FileSystemNameLength)
            ? new string(name)
            : null;
    }

    /// <summary>Path of a Windows known folder (e.g. Downloads), or null if it isn't defined.</summary>
    public static string? TryGetKnownFolderPath(Guid folderId)
    {
        var result = SHGetKnownFolderPath(folderId, KF_FLAG_DONT_VERIFY, IntPtr.Zero, out var pointer);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(pointer) : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    /// <summary>Converts a Windows FILETIME to UTC; impossible values become extreme dates that the policy distrusts.</summary>
    public static DateTimeOffset FromFileTime(long fileTime)
    {
        if (fileTime < 0)
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MaxValue;
        }
    }
}
