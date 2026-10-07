using System.ComponentModel;
using System.Runtime.InteropServices;
using Manifold.Core.Data;

namespace Manifold.Storm;

/// <summary>An MPQ on disk (a vanilla .mpq, or the MPQ inside the mod exe), read and written through StormLib.</summary>
public sealed class StormArchive(string path, string? name = null) : IWritableArchive
{
    const int ERROR_FILE_NOT_FOUND = 2, ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32,
        ERROR_LOCK_VIOLATION = 33, ERROR_DISK_FULL = 112;

    /// <summary>SFileGetFileSize's failure value (SFILE_INVALID_SIZE).</summary>
    const uint SizeError = 0xFFFFFFFF;

    public string Name { get; } = name ?? Path.GetFileName(path);

    public byte[]? TryRead(string file)
    {
        if (!Native.SFileOpenArchive(path, 0, Native.STREAM_FLAG_READ_ONLY, out var mpq))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Name}: cannot open its MPQ");
        try
        {
            if (!Native.SFileOpenFileEx(mpq, file, 0, out var handle)) return null;
            try
            {
                uint size = Native.SFileGetFileSize(handle, IntPtr.Zero);
                if (size == SizeError)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Name}: StormLib can't tell the size of {file}");
                var data = new byte[size];
                if (!Native.SFileReadFile(handle, data, size, out var read, IntPtr.Zero) || read != size)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Name}: cannot read {file}");
                return data;
            }
            finally { Native.SFileCloseFile(handle); }
        }
        finally { Native.SFileCloseArchive(mpq); }
    }

    /// <summary>Adds or replaces one file: on a copy of the archive, swapped in at the end.</summary>
    public void Write(string file, byte[] data)
    {
        string temp = path + ".saving";
        try
        {
            try { File.Copy(path, temp, overwrite: true); }
            catch (IOException e) when (IsBusy(e)) { throw Busy(); }
            WriteInto(temp, file, data);
            try { File.Replace(temp, path, path + ".bak"); }
            catch (IOException e) when (IsBusy(e)) { throw Busy(); }
            catch (UnauthorizedAccessException) { throw Busy(); }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void WriteInto(string archive, string file, byte[] data)
    {
        if (!Native.SFileOpenArchive(archive, 0, 0, out var mpq))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "cannot open the MPQ for writing");
        try
        {
            if (!Native.SFileCreateFile(mpq, file, 0, (uint)data.Length, 0,
                    Native.MPQ_FILE_COMPRESS | Native.MPQ_FILE_REPLACEEXISTING, out var handle))
            {
                int error = Marshal.GetLastWin32Error();
                // A full hash table: make room once, then try again.
                if (error != ERROR_DISK_FULL || !Native.SFileSetMaxFileCount(mpq, 4096) ||
                    !Native.SFileCreateFile(mpq, file, 0, (uint)data.Length, 0,
                        Native.MPQ_FILE_COMPRESS | Native.MPQ_FILE_REPLACEEXISTING, out handle))
                    throw new Win32Exception(error, $"cannot add {file}");
            }
            bool written = Native.SFileWriteFile(handle, data, (uint)data.Length, Native.MPQ_COMPRESSION_ZLIB);
            bool finished = Native.SFileFinishFile(handle);
            if (!written || !finished || !Native.SFileFlushArchive(mpq))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"cannot write {file}");
        }
        finally { Native.SFileCloseArchive(mpq); }
    }

    static bool IsBusy(IOException e) =>
        (e.HResult & 0xFFFF) is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION or ERROR_ACCESS_DENIED;

    ArchiveBusyException Busy() =>
        new($"{Name} is in use by another program (FireGraft, or a repack running). The edits are kept; save again once it is closed.");
}
