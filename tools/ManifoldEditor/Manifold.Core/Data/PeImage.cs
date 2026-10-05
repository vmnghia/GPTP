using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A 32-bit PE file read from disk: maps virtual addresses to file offsets, so tables
/// can be read from StarCraft.exe without running it.
/// </summary>
public sealed class PeImage
{
    const uint CodeFlag = 0x20; // IMAGE_SCN_CNT_CODE

    public sealed record Section(string Name, uint VirtualAddress, uint VirtualSize,
        uint RawOffset, uint RawSize, uint Characteristics);

    readonly byte[] bytes;
    public uint ImageBase { get; }
    public IReadOnlyList<Section> Sections { get; }

    public PeImage(byte[] bytes)
    {
        this.bytes = bytes;
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("not an exe");
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 0 || pe + 24 > bytes.Length || BitConverter.ToUInt32(bytes, pe) != 0x00004550)
            throw new InvalidDataException("no PE header");
        int sectionCount = BitConverter.ToUInt16(bytes, pe + 6);
        int optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
        int optional = pe + 24;
        if (BitConverter.ToUInt16(bytes, optional) != 0x10B) throw new InvalidDataException("not a 32-bit exe");
        ImageBase = BitConverter.ToUInt32(bytes, optional + 28);
        var sections = new List<Section>();
        int at = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++, at += 40)
            sections.Add(new Section(
                Encoding.ASCII.GetString(bytes, at, 8).TrimEnd('\0'),
                BitConverter.ToUInt32(bytes, at + 12), BitConverter.ToUInt32(bytes, at + 8),
                BitConverter.ToUInt32(bytes, at + 20), BitConverter.ToUInt32(bytes, at + 16),
                BitConverter.ToUInt32(bytes, at + 36)));
        Sections = sections;
    }

    /// <summary>The file offset of a virtual address, or -1 if no section's data holds it.</summary>
    public long OffsetOf(uint va)
    {
        uint rva = va - ImageBase;
        foreach (var s in Sections)
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.RawSize)
                return s.RawOffset + (rva - s.VirtualAddress);
        return -1;
    }

    public ReadOnlySpan<byte> Read(uint va, int length)
    {
        long offset = OffsetOf(va);
        if (offset < 0 || offset + length > bytes.Length)
            throw new InvalidDataException($"0x{va:X} is not in the file");
        return bytes.AsSpan((int)offset, length);
    }

    /// <summary>The first code section's address range [start, end).</summary>
    public (uint Start, uint End) CodeRange()
    {
        var code = Sections.First(s => (s.Characteristics & CodeFlag) != 0);
        uint start = ImageBase + code.VirtualAddress;
        return (start, start + Math.Max(code.VirtualSize, code.RawSize));
    }
}

/// <summary>StarCraft.exe 1.16.1's own button sets, read from its file image.</summary>
public static class VanillaSets
{
    /// <summary>buttonSetTable (GPTP scbwdata.h): 250 x {u32 count, BUTTON* first, u32 unit}.</summary>
    public const uint TableAddress = 0x005187E8;

    public static ButtonSet[] Read(PeImage exe)
    {
        var sets = new ButtonSet[Card.SetCount];
        var table = exe.Read(TableAddress, Card.SetCount * 12);
        for (int s = 0; s < Card.SetCount; s++)
        {
            uint count = BitConverter.ToUInt32(table[(s * 12)..]);
            uint first = BitConverter.ToUInt32(table[(s * 12 + 4)..]);
            uint unit = BitConverter.ToUInt32(table[(s * 12 + 8)..]);
            var buttons = new Button[first == 0 ? 0 : count];
            if (buttons.Length > 0)
            {
                var raw = exe.Read(first, buttons.Length * Button.Size);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i] = Button.Read(raw[(i * Button.Size)..]);
            }
            sets[s] = new ButtonSet(buttons, unit);
        }
        return sets;
    }
}
