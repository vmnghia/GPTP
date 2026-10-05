using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class ResourceTests
{
    [Fact]
    public void Prefers_the_mod_exe_over_the_vanilla_mpqs()
    {
        var mod = new FakeArchive("SCManifold.exe", new() { ["rez\\stat_txt.tbl"] = new byte[] { 1 } });
        var patch = new FakeArchive("patch_rt.mpq", new() { ["rez\\stat_txt.tbl"] = new byte[] { 2 }, ["unit\\cmdbtns\\ticon.pcx"] = new byte[] { 3 } });
        var resolver = new ResourceResolver(new IArchive[] { mod, patch, new FakeArchive("BrooDat.mpq") });
        var found = resolver.Find("rez\\stat_txt.tbl")!.Value;
        Assert.Equal(new byte[] { 1 }, found.Data);
        Assert.Equal("SCManifold.exe", found.Source);
        Assert.Equal("patch_rt.mpq", resolver.Find("unit\\cmdbtns\\ticon.pcx")!.Value.Source);
        Assert.Null(resolver.Find("unit\\cmdbtns\\cmdicons.grp"));
    }

    [Fact]
    public void Stat_txt_ids_count_from_1_and_the_hotkey_is_the_first_character()
    {
        // count 2, offsets 6 and 10: "m\u0001<M>ove"-like strings
        var tbl = new List<byte> { 2, 0, 6, 0, 0, 0 };
        tbl.AddRange("m\u0001M\0"u8.ToArray());
        tbl.AddRange("s\u0001Stop\0"u8.ToArray());
        var bytes = tbl.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (ushort)10);
        var text = StatTxt.Parse(bytes);
        Assert.Equal(2, text.Count);
        Assert.Null(text.Get(0));
        Assert.Equal("m\u0001M", text.Get(1));
        Assert.Equal("s\u0001Stop", text.Get(2));
        Assert.Null(text.Get(3));
        Assert.Equal('S', StatTxt.HotkeyOf(text.Get(2)));
        Assert.Null(StatTxt.HotkeyOf(null));
    }

    [Fact]
    public void Decodes_a_grp_frame_with_skips_repeats_and_copies()
    {
        // One 4x2 frame at (0,0): row 0 = skip 1, repeat 7 x2, copy [9]; row 1 = copy [1,2,3,4]
        var grp = new List<byte> { 1, 0, 4, 0, 2, 0, 0, 0, 4, 2, 14, 0, 0, 0 };
        grp.AddRange(new byte[] { 4, 0, 9, 0 });                       // row offsets, relative to 14
        grp.AddRange(new byte[] { 0x81, 0x42, 7, 0x01, 9 });           // row 0
        grp.AddRange(new byte[] { 0x04, 1, 2, 3, 4 });                 // row 1
        var frame = new Grp(grp.ToArray()).DecodeFrame(0);
        Assert.Equal(new byte[] { 0, 7, 7, 9, 1, 2, 3, 4 }, frame);
    }

    [Fact]
    public void Reads_a_pcx_palette_and_keeps_index_0_transparent()
    {
        var pcx = new byte[128 + 10 + 769];
        int at = pcx.Length - 769;
        pcx[at] = 0x0C;
        pcx[at + 1 + 3 * 5] = 0x11; pcx[at + 2 + 3 * 5] = 0x22; pcx[at + 3 + 3 * 5] = 0x33;
        var palette = PcxPalette.Read(pcx);
        Assert.Equal(0xFF112233u, palette[5]);
        Assert.Equal(new[] { 0u, 0xFF112233u }, PcxPalette.ToArgb(new byte[] { 0, 5 }, palette));
    }

    [Fact]
    public void Icon_names_match_the_grp_frames()
    {
        var names = Fixtures.Text("Icons.txt").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.Equal(390, names.Length);
        Assert.Equal("Marine", names[0]);
    }
}
