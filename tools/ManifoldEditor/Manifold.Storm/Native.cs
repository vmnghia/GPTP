using System.Runtime.InteropServices;

namespace Manifold.Storm;

/// <summary>
/// StormLib's API, as PyMS declares it (PyMS/FileFormats/MPQ/StormLib.py): stdcall, ANSI
/// strings. Its success flags are C++ <c>bool</c>, one byte, not Win32's four-byte BOOL, so
/// every bool return is marshalled as U1: read as four bytes, a failure (0 in the low byte)
/// can come back true from whatever the rest of the register held.
/// </summary>
static class Native
{
    const string Dll = "StormLib.dll";
    public const uint STREAM_FLAG_READ_ONLY = 0x00000100;
    // PKWARE implode: the one compression StarCraft 1.16.1's storm.dll reads for
    // ordinary files. It has no zlib, and a zlib file stops the game with
    // "The file data is corrupt".
    public const uint MPQ_FILE_IMPLODE = 0x00000100;
    public const uint MPQ_FILE_REPLACEEXISTING = 0x80000000;

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileOpenArchive(string mpqName, uint priority, uint flags, out IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileCloseArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileFlushArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileOpenFileEx(IntPtr mpq, string name, uint scope, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern uint SFileGetFileSize(IntPtr file, IntPtr high);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileReadFile(IntPtr file, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileCreateFile(IntPtr mpq, string name, ulong fileTime, uint size, uint locale, uint flags, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileWriteFile(IntPtr file, byte[] data, uint size, uint compression);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileFinishFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileSetMaxFileCount(IntPtr mpq, uint maxFileCount);

    /// <summary>SFILE_FIND_DATA, as PyMS declares it.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FindData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] public string FileName;
        public IntPtr PlainName;
        public uint HashIndex, BlockIndex, FileSize, FileFlags, CompressedSize, FileTimeLo, FileTimeHi, Locale;
    }

    /// <summary>A find handle, or NULL (0 or -1 in practice) when nothing matches.</summary>
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern IntPtr SFileFindFirstFile(IntPtr mpq, string mask, out FindData data, string? listFile);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileFindNextFile(IntPtr find, out FindData data);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SFileFindClose(IntPtr find);
}
