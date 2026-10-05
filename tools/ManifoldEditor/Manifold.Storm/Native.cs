using System.Runtime.InteropServices;

namespace Manifold.Storm;

static class Native
{
    const string Dll = "StormLib.dll";
    public const uint STREAM_FLAG_READ_ONLY = 0x00000100;
    public const uint MPQ_FILE_COMPRESS = 0x00000200;
    public const uint MPQ_FILE_REPLACEEXISTING = 0x80000000;
    public const uint MPQ_COMPRESSION_ZLIB = 0x02;

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileOpenArchive(string mpqName, uint priority, uint flags, out IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCloseArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileFlushArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileOpenFileEx(IntPtr mpq, string name, uint scope, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern uint SFileGetFileSize(IntPtr file, IntPtr high);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileReadFile(IntPtr file, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCreateFile(IntPtr mpq, string name, ulong fileTime, uint size, uint locale, uint flags, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileWriteFile(IntPtr file, byte[] data, uint size, uint compression);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileFinishFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileSetMaxFileCount(IntPtr mpq, uint maxFileCount);
}
