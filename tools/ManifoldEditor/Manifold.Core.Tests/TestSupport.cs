using Manifold.Core.Data;

namespace Manifold.Core.Tests;

static class Fixtures
{
    public static string PathOf(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
    public static string Text(string name) => File.ReadAllText(PathOf(name));
    public static FunctionTable Conditions() => FunctionTable.Parse(Text("FireGraftConFunc.txt"));
    public static FunctionTable Actions() => FunctionTable.Parse(Text("FireGraftActFunc.txt"));

    /// <summary>The user's StarCraft folder, for tests that need StarCraft.exe or the MPQs.</summary>
    public static string? StarCraftDir => Environment.GetEnvironmentVariable("MANIFOLD_SC_DIR");
}

static class Make
{
    /// <summary>
    /// A GRP of <paramref name="width"/> x <paramref name="height"/> frames, each framed at
    /// (0,0,width,height) and given as width*height palette indexes; rows are coded as copies
    /// of at most 63 bytes.
    /// </summary>
    public static byte[] Grp(int width, int height, params byte[][] frames)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((ushort)frames.Length));
        bytes.AddRange(BitConverter.GetBytes((ushort)width));
        bytes.AddRange(BitConverter.GetBytes((ushort)height));
        int headerAt = bytes.Count;
        bytes.AddRange(new byte[8 * frames.Length]);
        for (int f = 0; f < frames.Length; f++)
        {
            int offset = bytes.Count;
            bytes[headerAt + 8 * f + 2] = (byte)width;
            bytes[headerAt + 8 * f + 3] = (byte)height;
            for (int i = 0; i < 4; i++) bytes[headerAt + 8 * f + 4 + i] = BitConverter.GetBytes(offset)[i];
            int rowTable = bytes.Count;
            bytes.AddRange(new byte[2 * height]);
            for (int row = 0; row < height; row++)
            {
                int rel = bytes.Count - offset;
                bytes[rowTable + 2 * row] = (byte)rel;
                bytes[rowTable + 2 * row + 1] = (byte)(rel >> 8);
                for (int col = 0; col < width; col += 63)
                {
                    int n = Math.Min(63, width - col);
                    bytes.Add((byte)n);
                    for (int i = 0; i < n; i++) bytes.Add(frames[f][row * width + col + i]);
                }
            }
        }
        return bytes.ToArray();
    }

    public static Button Button(ushort position, ushort icon = 1, uint condition = 0x4282D0, uint action = 0x424440,
        ushort enabledString = 0) =>
        new(position, icon, condition, action, 0, 0, enabledString, 0);

    public static ButtonSet Set(params Button[] buttons) => new(buttons, 0);

    public static ButtonSet[] EmptySets() => Enumerable.Repeat(ButtonSet.Empty, Card.SetCount).ToArray();

    /// <summary>A 32-bit DLL (the minimal PE, machine 0x14C, DLL flag), with extra bytes after it.</summary>
    public static byte[] Dll(int extra = 0, byte fill = 0xCC)
    {
        var bytes = PeImageTests.MinimalExe((_, _) => { });
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 4), (ushort)0x14C);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 22), (ushort)0x2102);
        return [.. bytes, .. Enumerable.Repeat(fill, extra)];
    }

    /// <summary>An MPQ header (format 0, header 32 bytes) and filler to the archive's size.</summary>
    public static byte[] Mpq(int size = 2048)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)(i % 251);
        "MPQ\x1A"u8.CopyTo(bytes);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), 32u);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), (uint)size);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), (ushort)0);
        return bytes;
    }

    /// <summary>
    /// FireGraft's exe (spec §2): a launcher whose sections end at 0x2400, the "PEGF" header,
    /// the plugin, zeros to 512 bytes, the MPQ.
    /// </summary>
    public static byte[] FireGraftExe(byte[] plugin, byte[]? mpq = null, uint id = 0x1B4D69B6, uint count = 1, uint reserved = 0)
    {
        var bytes = new List<byte>(PeImageTests.MinimalExe((_, _) => { }));
        foreach (uint value in new[] { 0x46474550u, 1u, count, id, (uint)plugin.Length, reserved })
            bytes.AddRange(BitConverter.GetBytes(value));
        bytes.AddRange(plugin);
        while (bytes.Count % 512 != 0) bytes.Add(0);
        bytes.AddRange(mpq ?? Mpq());
        return bytes.ToArray();
    }
}

sealed class FakeArchive(string name, Dictionary<string, byte[]>? files = null) : IWritableArchive
{
    public Dictionary<string, byte[]> Files { get; } = files ?? new();
    public bool Busy { get; set; }
    public int Writes { get; private set; }
    public string Name => name;
    /// <summary>Paths whose read fails (as StormLib's can), present or not.</summary>
    public HashSet<string> Unreadable { get; } = new();
    public byte[]? TryRead(string path) =>
        Unreadable.Contains(path) ? throw new IOException($"{name}: cannot read {path}")
        : Files.TryGetValue(path, out var data) ? data : null;

    /// <summary>Files matching "folder\\*.ext" (the only masks the editor uses).</summary>
    public IReadOnlyList<string> List(string mask)
    {
        int star = mask.IndexOf('*');
        string prefix = mask[..star], suffix = mask[(star + 1)..];
        return Files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToArray();
    }

    public void Write(IReadOnlyList<(string Path, byte[] Data)> files)
    {
        if (Busy) throw new ArchiveBusyException("the exe is open in another program (FireGraft?)");
        foreach (var (path, data) in files) Files[path] = data;
        Writes++;
    }
}
