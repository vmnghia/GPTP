namespace Manifold.Core.Data;

/// <summary>
/// The layout of an exe FireGraft builds (spec 2026-10-14 §2, read from the user's
/// SCManifold.exe): a launcher (a PE), at the end of its last section a 24-byte header of
/// six u32 ("PEGF", 1, the plugin count 1, the plugin id, the plugin's size, 0), the plugin
/// stored as is, zeros to the next 512-byte boundary, then the MPQ to the end of the file.
/// Only this layout is known: anything else is refused rather than guessed at.
/// </summary>
public sealed record FireGraftExe(int HeaderOffset, uint PluginId, int PluginOffset, int PluginSize, int MpqOffset)
{
    /// <summary>"PEGF" read as a little-endian u32.</summary>
    public const uint Magic = 0x46474550;
    public const int HeaderSize = 24;
    /// <summary>Storm looks for an MPQ's header at each 512-byte boundary of the file.</summary>
    public const int Alignment = 512;

    static readonly byte[] MpqSignature = "MPQ\x1A"u8.ToArray();
    static readonly byte[] UserDataSignature = "MPQ\x1B"u8.ToArray();

    static uint U32(byte[] bytes, int at) => BitConverter.ToUInt32(bytes, at);
    static int Align(int offset) => (offset + Alignment - 1) / Alignment * Alignment;

    /// <summary>
    /// The exe's layout, or null when it has no "PEGF" header where FireGraft puts it (not
    /// FireGraft's exe). Throws <see cref="InvalidDataException"/> for a header the editor
    /// doesn't know, or bytes after the plugin that aren't padding and the MPQ.
    /// </summary>
    public static FireGraftExe? Parse(byte[] exe)
    {
        PeImage launcher;
        try { launcher = new PeImage(exe); }
        catch (InvalidDataException) { return null; }
        if (launcher.Sections.Count == 0) return null;
        long end = launcher.Sections.Max(s => (long)s.RawOffset + s.RawSize);
        if (end + HeaderSize > exe.Length || U32(exe, (int)end) != Magic) return null;
        int header = (int)end;

        uint version = U32(exe, header + 4), count = U32(exe, header + 8), id = U32(exe, header + 12);
        uint size = U32(exe, header + 16), reserved = U32(exe, header + 20);
        const string unknown = "a FireGraft layout the editor doesn't know";
        if (version != 1) throw new InvalidDataException($"header version {version}: {unknown}");
        if (count != 1) throw new InvalidDataException($"plugin count {count}: {unknown}");
        if (reserved != 0) throw new InvalidDataException($"reserved field {reserved}: {unknown}");

        int plugin = header + HeaderSize;
        if (size > exe.Length - plugin) throw new InvalidDataException("the plugin runs past the end of the file");
        if (exe[plugin] != 'M' || exe[plugin + 1] != 'Z') throw new InvalidDataException("the plugin is not a PE file");
        int pluginEnd = plugin + (int)size, mpq = Align(pluginEnd);
        if (mpq > exe.Length) throw new InvalidDataException("no MPQ right after the plugin");
        if (exe.AsSpan(pluginEnd, mpq - pluginEnd).ContainsAnyExcept((byte)0))
            throw new InvalidDataException("non-zero bytes between the plugin and the MPQ");
        if (mpq + 4 > exe.Length) throw new InvalidDataException("no MPQ right after the plugin");
        var signature = exe.AsSpan(mpq, 4);
        if (signature.SequenceEqual(UserDataSignature))
            throw new InvalidDataException("an MPQ user-data header (MPQ\\x1B) after the plugin: " + unknown);
        if (!signature.SequenceEqual(MpqSignature)) throw new InvalidDataException("no MPQ right after the plugin");
        return new FireGraftExe(header, id, plugin, (int)size, mpq);
    }

    public byte[] Plugin(byte[] exe) => exe[PluginOffset..(PluginOffset + PluginSize)];

    /// <summary>
    /// The exe with another plugin (spec §4): the launcher's bytes, the header with the new
    /// size (the id kept), the plugin, zeros to 512 bytes, the MPQ byte for byte. Throws
    /// <see cref="InvalidDataException"/> for an exe without FireGraft's layout, a plugin
    /// that isn't a 32-bit DLL, or a result with an MPQ header before the mod's MPQ.
    /// </summary>
    public static byte[] WithPlugin(byte[] exe, byte[] plugin)
    {
        var layout = Parse(exe) ?? throw new InvalidDataException("no FireGraft plugin header in the exe");
        if (DllProblem(plugin) is string problem) throw new InvalidDataException(problem);
        int pluginOffset = layout.PluginOffset, mpq = Align(pluginOffset + plugin.Length);
        var result = new byte[mpq + (exe.Length - layout.MpqOffset)];
        exe.AsSpan(0, pluginOffset).CopyTo(result);
        BitConverter.TryWriteBytes(result.AsSpan(layout.HeaderOffset + 16), (uint)plugin.Length);
        plugin.CopyTo(result, pluginOffset);
        exe.AsSpan(layout.MpqOffset).CopyTo(result.AsSpan(mpq));
        // Storm opens the first MPQ header it finds at a 512-byte boundary.
        for (int at = 0; at < mpq; at += Alignment)
            if (result.AsSpan(at, 4).SequenceEqual(MpqSignature))
                throw new InvalidDataException(
                    $"the new plugin holds an MPQ header at a 512-byte boundary (0x{at:X}): Storm would open it instead of the mod's MPQ");
        return result;
    }

    /// <summary>Null for a 32-bit DLL (PE machine 0x14C, IMAGE_FILE_DLL); else why it isn't one.</summary>
    public static string? DllProblem(byte[] plugin)
    {
        if (plugin.Length < 0x40 || plugin[0] != 'M' || plugin[1] != 'Z') return "not a DLL: no PE header";
        int pe = BitConverter.ToInt32(plugin, 0x3C);
        if (pe < 0 || pe > plugin.Length - 24 || U32(plugin, pe) != 0x00004550) return "not a DLL: no PE header";
        ushort machine = BitConverter.ToUInt16(plugin, pe + 4), characteristics = BitConverter.ToUInt16(plugin, pe + 22);
        if (machine != 0x14C) return $"not a 32-bit DLL (machine 0x{machine:X})";
        if ((characteristics & 0x2000) == 0) return "not a DLL (an exe?)";
        return null;
    }
}
