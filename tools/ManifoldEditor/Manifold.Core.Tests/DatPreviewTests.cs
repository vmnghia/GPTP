using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

/// <summary>DatPreview.Image: frame 0 of an image's GRP with PyDAT's palettes and draw functions (previews spec §2).</summary>
public class DatPreviewTests
{
    const string Drone = "unit\\zerg\\drone.grp";

    /// <summary>A palette whose colour i is <paramref name="marker"/> | i, so a test can tell palettes apart.</summary>
    static uint[] Palette(uint marker) => Enumerable.Range(0, 256).Select(i => 0xFF000000u | marker | (uint)i).ToArray();

    static readonly PreviewPalettes Palettes =
        new(Palette(0), Palette(0x010000), Palette(0x020000), Palette(0x030000), Palette(0x040000));

    static DatFile Images(params (int Image, ulong Grp, ulong Draw, ulong Remap)[] entries)
    {
        var format = DatFormats.Of(DatId.Images);
        var file = DatFile.Parse(format, new byte[format.VanillaSize]);
        foreach (var (image, grp, draw, remap) in entries)
            file = file.With(image, "grp_file", grp).With(image, "draw_function", draw).With(image, "remapping", remap);
        return file;
    }

    static StringTable Tbl(params string[] strings) => StringTable.Parse(Manifold.Core.Tests.Tbl.Build(strings));

    static readonly byte[] ThreeByTwo = Make.Grp(3, 2, new byte[] { 0, 5, 6, 7, 0, 8 });

    static (DatPreview Preview, Func<int> Reads) With(Dictionary<string, byte[]> files, PreviewPalettes? palettes = null)
    {
        int reads = 0;
        var preview = new DatPreview(path => { reads++; return files.TryGetValue(path, out var b) ? b : null; }, palettes ?? Palettes);
        return (preview, () => reads);
    }

    [Fact]
    public void DrawsFrameZeroWithTheUnitsPalette()
    {
        var (preview, _) = With(new() { [Drone] = ThreeByTwo });
        var (picture, reason) = preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3);
        Assert.Null(reason);
        Assert.NotNull(picture);
        var units = Palettes.Units!;
        Assert.Equal(new[] { 0u, units[5], units[6], units[7], 0u, units[8] }, picture!.Pixels);
        Assert.Equal((3, 2, 0, 2), (picture.Width, picture.Height, picture.Top, picture.Bottom));
    }

    [Fact]
    public void TopAndBottomAreTheDrawnRows()
    {
        var grp = Make.Grp(2, 4, new byte[] { 0, 0, 0, 3, 4, 0, 0, 0 });
        var (preview, _) = With(new() { [Drone] = grp });
        var picture = preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3).Picture!;
        Assert.Equal((1, 3), (picture.Top, picture.Bottom));

        var empty = Make.Grp(2, 2, new byte[4]);
        var (blank, _) = With(new() { [Drone] = empty });
        var none = blank.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3).Picture!;
        Assert.Equal((-1, -1), (none.Top, none.Bottom));
    }

    [Fact]
    public void ATrailingNulInTheTblStringIsDropped()
    {
        var (preview, _) = With(new() { [Drone] = ThreeByTwo });
        Assert.NotNull(preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp\0"), 3).Picture);
    }

    [Fact]
    public void ThingyTilesetUsesTerrain()
    {
        var (preview, _) = With(new() { ["unit\\thingy\\tileset\\x.grp"] = ThreeByTwo });
        var picture = preview.Image(Images((3, 1, 0, 0)), Tbl("thingy\\tileset\\x.grp"), 3).Picture!;
        Assert.Equal(Palettes.Terrain![5], picture.Pixels[1]);
    }

    [Fact]
    public void FireRemappingUsesTheFirePalettes()
    {
        var (preview, _) = With(new() { [Drone] = ThreeByTwo });
        var tbl = Tbl("zerg\\drone.grp");
        uint Pixel(ulong draw, ulong remap) => preview.Image(Images((3, 1, draw, remap)), tbl, 3).Picture!.Pixels[1];
        Assert.Equal(Palettes.Ofire![5], Pixel(9, 1));
        Assert.Equal(Palettes.Gfire![5], Pixel(9, 2));
        Assert.Equal(Palettes.Bfire![5], Pixel(9, 3));
        Assert.Equal(Palettes.Units![5], Pixel(9, 0));
        Assert.Equal(Palettes.Units![5], Pixel(9, 4));
        Assert.Equal(Palettes.Units![5], Pixel(0, 1));
    }

    [Fact]
    public void SelectionCircleDrawsOneToNineGreen()
    {
        var grp = Make.Grp(4, 1, new byte[] { 0, 1, 9, 10 });
        var (preview, _) = With(new() { [Drone] = grp });
        var picture = preview.Image(Images((3, 1, 13, 0)), Tbl("zerg\\drone.grp"), 3).Picture!;
        Assert.Equal(new[] { 0u, 0xFF249824u, 0xFF249824u, Palettes.Units![10] }, picture.Pixels);
    }

    [Fact]
    public void ShadowDrawsEveryPixelGrey()
    {
        var grp = Make.Grp(4, 1, new byte[] { 0, 1, 9, 200 });
        var (preview, _) = With(new() { [Drone] = grp });
        var picture = preview.Image(Images((3, 1, 10, 0)), Tbl("zerg\\drone.grp"), 3).Picture!;
        Assert.Equal(new[] { 0u, 0xFF323232u, 0xFF323232u, 0xFF323232u }, picture.Pixels);
    }

    [Fact]
    public void ReasonsForWhatIsMissing()
    {
        var tbl = Tbl("zerg\\drone.grp");
        var images = Images((3, 1, 0, 0), (4, 0, 0, 0), (5, 7, 0, 0));
        string? Reason(DatPreview p, StringTable? t, int image) => p.Image(images, t, image).Reason;

        var (found, _) = With(new() { [Drone] = ThreeByTwo });
        Assert.Equal("image 1200 is past images.dat's end", Reason(found, tbl, 1200));
        Assert.Equal("image 4 has no GRP file", Reason(found, tbl, 4));
        Assert.Equal("images.tbl has no string 7", Reason(found, tbl, 5));
        Assert.Equal("images.tbl has no string 1", Reason(found, null, 3));

        var (missing, _) = With(new());
        Assert.Equal("unit\\zerg\\drone.grp not found", Reason(missing, tbl, 3));

        var (noPalette, _) = With(new() { [Drone] = ThreeByTwo }, Palettes with { Units = null });
        Assert.Equal("Units.pal not found", Reason(noPalette, tbl, 3));

        var (noFrames, _) = With(new() { [Drone] = Make.Grp(3, 2) });
        Assert.Equal("unit\\zerg\\drone.grp has no frames", Reason(noFrames, tbl, 3));

        var (emptyFrame, _) = With(new() { [Drone] = Make.Grp(0, 2, Array.Empty<byte>()) });
        Assert.Equal("unit\\zerg\\drone.grp has an empty frame", Reason(emptyFrame, tbl, 3));
    }

    [Fact]
    public void DamagedGrpsGiveAReasonAtEveryLength()
    {
        var whole = Make.Grp(4, 4, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());
        string[] allowed =
        [
            "unit\\zerg\\drone.grp is damaged", "unit\\zerg\\drone.grp has no frames", "unit\\zerg\\drone.grp has an empty frame",
        ];
        for (int length = 0; length < whole.Length; length++)
        {
            var (preview, _) = With(new() { [Drone] = whole[..length] });
            var (picture, reason) = preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3);
            Assert.True(picture is not null || allowed.Contains(reason), $"length {length}: {reason}");
        }

        var farOffset = (byte[])whole.Clone();
        BitConverter.GetBytes(10000).CopyTo(farOffset, 6 + 4);
        var (far, _) = With(new() { [Drone] = farOffset });
        Assert.Equal("unit\\zerg\\drone.grp is damaged", far.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3).Reason);
    }

    [Fact]
    public void FramesAreCachedPerPathPaletteAndDrawFunction()
    {
        var (preview, reads) = With(new() { [Drone] = ThreeByTwo });
        var tbl = Tbl("zerg\\drone.grp");
        var images = Images((3, 1, 0, 0), (4, 1, 0, 0), (5, 1, 13, 0), (6, 1, 2, 0), (7, 1, 9, 1));
        preview.Image(images, tbl, 3);
        preview.Image(images, tbl, 4);
        Assert.Equal(1, reads());
        preview.Image(images, tbl, 5);
        Assert.Equal(2, reads());
        preview.Image(images, tbl, 6);   // draw function 2 draws as normal
        Assert.Equal(2, reads());
        preview.Image(images, tbl, 7);   // another palette
        Assert.Equal(3, reads());
    }

    [Fact]
    public void PalettesLoadFromAFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "manifold-palettes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, "Units.pal"), new byte[768]);
            File.WriteAllBytes(Path.Combine(folder, "Terrain.pal"), new byte[5]);   // unreadable: null
            var loaded = PreviewPalettes.Load(folder);
            Assert.NotNull(loaded.Units);
            Assert.Null(loaded.Terrain);
            Assert.Null(loaded.Ofire);
            Assert.Equal(new[] { "Terrain.pal", "ofire.pal", "gfire.pal", "bfire.pal" }, loaded.Missing);
        }
        finally { Directory.Delete(folder, true); }

        var shipped = PreviewPalettes.Load(Fixtures.PathOf("palettes"));
        Assert.Empty(shipped.Missing);
        foreach (var palette in new[] { shipped.Units, shipped.Terrain, shipped.Ofire, shipped.Gfire, shipped.Bfire })
        {
            Assert.Equal(256, palette!.Length);
            Assert.All(palette, c => Assert.Equal(0xFF000000u, c & 0xFF000000u));
        }
    }

    [Fact]
    public void ResourcesNoteTheMissingPalettesAndKeepTheResolver()
    {
        var resolver = new ResourceResolver([]);
        var r = EditorResources.Load(resolver, null, Palettes with { Units = null });
        Assert.Contains("Units.pal: not found; previews that need it show why", r.Notes);
        Assert.Same(resolver, r.Resolver);
        Assert.DoesNotContain(EditorResources.Load(resolver, null).Notes, n => n.Contains(".pal: not found; previews"));
    }

    [Fact]
    public void PreviewSettingsRoundTripAndDefault()
    {
        var fresh = new EditorSettings().Previews;
        Assert.False(fresh.Unit || fresh.UnitPlacement || fresh.UnitDimensions || fresh.UnitAddon || fresh.StarEdit || fresh.Sprite || fresh.Image);
        Assert.Equal(106, fresh.AddonParent);

        string folder = Path.Combine(Path.GetTempPath(), "manifold-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.json");
        try
        {
            new EditorSettings { Previews = new() { Unit = true, AddonParent = 107 } }.Save(path);
            var loaded = EditorSettings.Load(path).Previews;
            Assert.True(loaded.Unit);
            Assert.Equal(107, loaded.AddonParent);
            Assert.False(loaded.Sprite);

            File.WriteAllText(path, """{ "LastExe": "C:\\x.exe" }""");   // written before part 3
            Assert.Equal(new DatPreviewSettings(), EditorSettings.Load(path).Previews);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void HeadersOutsidePyMsBoundsAreDamaged()
    {
        // PyMS's CacheGRP.load_file: 1-2400 frames, width and height 1-256.
        byte[] hostile = [0x01, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x01, 0x01, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00];
        var wide = Make.Grp(3, 2, new byte[6]);
        BitConverter.GetBytes((ushort)300).CopyTo(wide, 2);
        var tall = Make.Grp(3, 2, new byte[6]);
        BitConverter.GetBytes((ushort)257).CopyTo(tall, 4);
        var many = Make.Grp(3, 2, new byte[6]);
        BitConverter.GetBytes((ushort)2401).CopyTo(many, 0);
        foreach (var bytes in new[] { hostile, wide, tall, many })
        {
            var (preview, _) = With(new() { [Drone] = bytes });
            Assert.Equal("unit\\zerg\\drone.grp is damaged", preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3).Reason);
        }
    }

    [Fact]
    public void AnArchiveThatCantBeReadIsAReason()
    {
        foreach (Exception failure in new Exception[] { new IOException("locked"), new System.ComponentModel.Win32Exception(5, "denied") })
        {
            var preview = new DatPreview(_ => throw failure, Palettes);
            Assert.Equal($"unit\\zerg\\drone.grp can't be read ({failure.Message})",
                preview.Image(Images((3, 1, 0, 0)), Tbl("zerg\\drone.grp"), 3).Reason);
        }
    }
}
