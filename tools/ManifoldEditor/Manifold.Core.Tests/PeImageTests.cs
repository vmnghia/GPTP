using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class PeImageTests
{
    /// <summary>
    /// A minimal 32-bit PE at image base 0x400000: .text at RVA 0x1000 (code), .data at
    /// RVA 0x118000 holding the button set table at 0x5187E8 and buttons at 0x519400.
    /// </summary>
    internal static byte[] MinimalExe(Action<byte[], Func<uint, int>> fillData)
    {
        const int pe = 0x40, optional = pe + 24, optionalSize = 0xE0, sections = optional + optionalSize;
        const int textRaw = 0x200, dataRaw = 0x400, dataSize = 0x2000;
        var bytes = new byte[dataRaw + dataSize];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.TryWriteBytes(bytes.AsSpan(0x3C), pe);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe), 0x00004550u);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 6), (ushort)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 20), (ushort)optionalSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(optional), (ushort)0x10B);
        BitConverter.TryWriteBytes(bytes.AsSpan(optional + 28), 0x400000u);
        void Section(int at, string name, uint va, uint vsize, uint raw, uint rawSize, uint flags)
        {
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, at);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 8), vsize);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 12), va);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 16), rawSize);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 20), raw);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 36), flags);
        }
        Section(sections, ".text", 0x1000, 0x100, textRaw, 0x200, 0x60000020);
        Section(sections + 40, ".data", 0x118000, dataSize, dataRaw, dataSize, 0xC0000040);
        fillData(bytes, va => (int)(va - 0x400000 - 0x118000 + dataRaw));
        return bytes;
    }

    [Fact]
    public void Maps_addresses_to_file_offsets_and_finds_the_code()
    {
        var exe = new PeImage(MinimalExe((_, _) => { }));
        Assert.Equal(0x400000u, exe.ImageBase);
        Assert.Equal(0x400 + 0x7E8, exe.OffsetOf(0x5187E8));
        Assert.Equal(-1, exe.OffsetOf(0x700000));
        Assert.Equal((0x401000u, 0x401200u), exe.CodeRange());
    }

    [Fact]
    public void Reads_the_vanilla_table()
    {
        var bytes = MinimalExe((b, offsetOf) =>
        {
            int entry = offsetOf(VanillaSets.TableAddress + 5 * 12);
            BitConverter.TryWriteBytes(b.AsSpan(entry), 2u);
            BitConverter.TryWriteBytes(b.AsSpan(entry + 4), 0x519400u);
            BitConverter.TryWriteBytes(b.AsSpan(entry + 8), 5u);
            Make.Button(1, icon: 228).Write(b.AsSpan(offsetOf(0x519400)));
            Make.Button(11, icon: 236).Write(b.AsSpan(offsetOf(0x519414)));
        });
        var sets = VanillaSets.Read(new PeImage(bytes));
        Assert.Equal(250, sets.Length);
        Assert.Equal(5u, sets[5].ConnectedUnit);
        Assert.Equal(new ushort[] { 228, 236 }, sets[5].Buttons.Select(b => b.Icon));
        Assert.Empty(sets[0].Buttons);
    }

    [Fact]
    public void A_table_that_is_not_1_16_1s_is_refused_with_where_it_went_wrong()
    {
        var bytes = MinimalExe((b, offsetOf) =>
        {
            int entry = offsetOf(VanillaSets.TableAddress + 7 * 12);
            BitConverter.TryWriteBytes(b.AsSpan(entry), 0xC0DE0000u);     // a count no set has
            BitConverter.TryWriteBytes(b.AsSpan(entry + 4), 0x519400u);
        });
        var e = Assert.Throws<InvalidDataException>(() => VanillaSets.Read(new PeImage(bytes)));
        Assert.Contains("set 7", e.Message);
        Assert.Contains("0x51883C", e.Message);
        Assert.Contains("1.16.1", e.Message);
    }

    [SkippableFact]
    public void Reads_StarCraft_exe()
    {
        Skip.If(Fixtures.StarCraftDir is null, "MANIFOLD_SC_DIR is not set");
        var exe = new PeImage(File.ReadAllBytes(System.IO.Path.Combine(Fixtures.StarCraftDir!, "StarCraft.exe")));
        var sets = VanillaSets.Read(exe);
        Assert.Empty(sets[228].Buttons);
        // The spec (§2): FireGraft's set 11 starts with vanilla Move.
        Assert.Equal(new Button(1, 228, 0x428DA0, 0x424440, 0, 0, 664, 0), sets[11].Buttons[0]);
        var (start, end) = exe.CodeRange();
        Assert.All(sets.SelectMany(s => s.Buttons), b => Assert.InRange(b.Condition, start, end - 1));
    }
}
