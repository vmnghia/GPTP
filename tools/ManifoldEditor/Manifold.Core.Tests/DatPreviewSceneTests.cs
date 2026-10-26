using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

/// <summary>PyDAT's preview layouts and the square they render to (previews spec §2).</summary>
public class DatPreviewSceneTests
{
    const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Yellow = 0xFFFFFF00, Black = 0xFF000000, BarGreen = 0xFF008000;

    static uint[] Palette() => Enumerable.Range(0, 256).Select(i => 0xFF000000u | (uint)i).ToArray();
    static readonly PreviewPalettes Palettes = new(Palette(), Palette(), Palette(), Palette(), Palette());

    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);
    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    /// <summary>
    /// The four files a unit's picture is found through, all zero, and images.tbl. String 1 is
    /// the unit's GRP, 2 the add-on parent's, 3 a selection circle's.
    /// </summary>
    sealed class World
    {
        public Dictionary<DatId, DatFile?> Files = new()
        {
            [DatId.Units] = Zeros(DatId.Units), [DatId.Flingy] = Zeros(DatId.Flingy),
            [DatId.Sprites] = Zeros(DatId.Sprites), [DatId.Images] = Zeros(DatId.Images),
        };
        public StringTable Strings = StringTable.Parse(Manifold.Core.Tests.Tbl.Build("zerg\\drone.grp\0", "terran\\control.grp\0", "thingy\\o048.grp\0"));
        public Dictionary<string, byte[]> Grps = new()
        {
            ["unit\\zerg\\drone.grp"] = Make.Grp(3, 3, Enumerable.Repeat((byte)5, 9).ToArray()),
            ["unit\\terran\\control.grp"] = Make.Grp(4, 4, Enumerable.Repeat((byte)6, 16).ToArray()),
            ["unit\\thingy\\o048.grp"] = Make.Grp(2, 4, new byte[] { 0, 0, 1, 1, 1, 1, 0, 0 }),
        };
        public DatPreview Preview => new(p => Grps.TryGetValue(p, out var b) ? b : null, Palettes);
        public DatFile? Dat(DatId id) => Files.TryGetValue(id, out var f) ? f : null;

        public void Set(DatId id, int entry, string property, ulong value) => Files[id] = Files[id]!.With(entry, property, value);

        /// <summary>Unit → flingy → sprite → image → images.tbl string.</summary>
        public void Chain(int unit, int flingy, int sprite, int image, ulong tblString)
        {
            Set(DatId.Units, unit, "graphics", (ulong)flingy);
            Set(DatId.Flingy, flingy, "sprite", (ulong)sprite);
            Set(DatId.Sprites, sprite, "image", (ulong)image);
            Set(DatId.Images, image, "grp_file", tblString);
        }

        public void Size(int unit, int width, int height) =>
            Set(DatId.Units, unit, "staredit_placement_size", (ulong)width | (ulong)height << 16);
    }

    static void IsBox(PreviewLayer layer, int x1, int y1, int x2, int y2, uint outline, uint? fill = null) =>
        Assert.Equal(new BoxLayer(x1, y1, x2, y2, outline, fill), layer);

    static void IsPicture(PreviewLayer layer, int x, int y)
    {
        var picture = Assert.IsType<PictureLayer>(layer);
        Assert.Equal((x, y), (picture.X, picture.Y));
    }

    [Fact]
    public void PlacementBoxRoundsAsPyDat()
    {
        IsBox(DatPreview.PlacementBox(32, 32), 114, 114, 145, 145, Red);
        IsBox(DatPreview.PlacementBox(33, 21), 114, 120, 146, 140, Red);
    }

    [Fact]
    public void StarEditPlacementBoxUsesIntegerHalves()
    {
        IsBox(DatPreview.StarEditPlacementBox(33, 21), 114, 120, 145, 139, Red);
        IsBox(DatPreview.StarEditPlacementBox(32, 32), 114, 114, 145, 145, Red);
    }

    [Fact]
    public void DimensionsBox() => IsBox(DatPreview.DimensionsBox(8, 9, 10, 11), 122, 121, 140, 141, Green);

    [Fact]
    public void AddonBoxAsPyDat()
    {
        var (box, x, y) = DatPreview.AddonBox(64, 64, 96, 0, 128, 96);
        IsBox(box, 1, 97, 129, 193, Yellow);
        Assert.Equal((65, 145), (x, y));
        Assert.Equal(1, DatPreview.AddonBox(65, 64, 96, 0, 128, 96).Box.X1);   // 65 / 2 rounds down
    }

    [Fact]
    public void HealthBarAsPyDat()
    {
        var circle = new PreviewPicture(new uint[1], 1, 1, 10, 20);
        var bar = DatPreview.HealthBar(140, circle, 13);
        Assert.Equal(5, bar.Count);
        IsBox(bar[0], 124, 151, 136, 155, Black, Black);
        IsBox(bar[1], 125, 152, 126, 154, BarGreen, BarGreen);
        IsBox(bar[2], 128, 152, 129, 154, BarGreen, BarGreen);
        IsBox(bar[3], 131, 152, 132, 154, BarGreen, BarGreen);
        IsBox(bar[4], 134, 152, 135, 154, BarGreen, BarGreen);
        for (int health = 0; health <= 3; health++) Assert.Equal(2, DatPreview.HealthBar(140, circle, health).Count);
    }

    static uint At(uint[] square, int x, int y) => square[y * DatPreview.Size + x];

    [Fact]
    public void RenderPlacesAPictureByItsCentre()
    {
        var three = new PreviewPicture(Enumerable.Range(1, 9).Select(i => 0xFF000000u | (uint)i).ToArray(), 3, 3, 0, 3);
        var square = new PreviewScene([new PictureLayer(three, 130, 130)], []).Render();
        Assert.Equal(DatPreview.Size * DatPreview.Size, square.Length);
        Assert.Equal(0xFF000001u, At(square, 129, 129));
        Assert.Equal(0xFF000009u, At(square, 131, 131));
        Assert.Equal(Black, At(square, 128, 129));

        var four = new PreviewPicture(Enumerable.Repeat(0xFF0000AAu, 16).ToArray(), 4, 4, 0, 4);
        four.Pixels[0] = 0xFF0000BB;
        four.Pixels[5] = 0;   // transparent: the square shows through
        square = new PreviewScene([new PictureLayer(four, 130, 130)], []).Render();
        Assert.Equal(0xFF0000BBu, At(square, 128, 128));
        Assert.Equal(Black, At(square, 129, 129));

        square = new PreviewScene([new BoxLayer(10, 20, 13, 24, Red)], []).Render();
        Assert.Equal(Red, At(square, 10, 20));
        Assert.Equal(Red, At(square, 13, 24));
        Assert.Equal(Red, At(square, 13, 22));
        Assert.Equal(Black, At(square, 11, 22));   // no fill: the inside untouched
        Assert.Equal(Black, At(square, 14, 24));

        square = new PreviewScene([new BoxLayer(10, 20, 13, 24, Red, Green)], []).Render();
        Assert.Equal(Green, At(square, 11, 22));
        Assert.Equal(Red, At(square, 10, 22));
    }

    [Fact]
    public void RenderClipsToTheSquare()
    {
        var big = new PreviewPicture(Enumerable.Range(0, 300 * 300).Select(i => 0xFF000000u | (uint)i).ToArray(), 300, 300, 0, 300);
        var square = new PreviewScene(
        [
            new PictureLayer(big, 130, 130), new PictureLayer(big, -50, 400),
            new BoxLayer(-10, -10, 300, 300, Red),
        ], []).Render();
        Assert.Equal(0xFF000000u | (20 * 300 + 20), At(square, 0, 0));   // top-left at (-20,-20)
        Assert.Equal(0xFF000000u | (276 * 300 + 276), At(square, 256, 256));

        square = new PreviewScene([new BoxLayer(-10, -10, 300, 300, Red, Green)], []).Render();
        Assert.Equal(Green, At(square, 0, 0));
        Assert.Equal(Green, At(square, 256, 256));
    }

    [Fact]
    public void UnitGraphicsSceneOrder()
    {
        var w = new World();
        w.Chain(107, 1, 1, 1, 1);
        w.Chain(106, 2, 2, 2, 2);
        w.Size(107, 64, 64);
        w.Size(106, 128, 96);
        w.Set(DatId.Units, 107, "addon_position", 96);
        w.Set(DatId.Units, 107, "unit_extents", 8UL | 9UL << 16 | 10UL << 32 | 11UL << 48);

        var all = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, new UnitPreviewOptions(true, true, true, 106));
        Assert.Empty(all.Reasons);
        Assert.Equal(5, all.Layers.Count);
        IsPicture(all.Layers[0], 65, 145);
        Assert.Equal(4, ((PictureLayer)all.Layers[0]).Picture.Width);   // the parent's
        IsPicture(all.Layers[1], 130, 130);
        Assert.Equal(3, ((PictureLayer)all.Layers[1]).Picture.Width);
        IsBox(all.Layers[2], 1, 97, 129, 193, Yellow);
        IsBox(all.Layers[3], 98, 98, 161, 161, Red);
        IsBox(all.Layers[4], 122, 121, 140, 141, Green);

        var noPlacement = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, new UnitPreviewOptions(false, true, true, 106));
        Assert.DoesNotContain(noPlacement.Layers, l => l is BoxLayer { Outline: Red });

        var none = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, new UnitPreviewOptions(false, false, false, 106));
        IsPicture(Assert.Single(none.Layers), 130, 130);
    }

    [Fact]
    public void AddonLeftOutForABadParentOrZeroPosition()
    {
        var w = new World();
        w.Chain(107, 1, 1, 1, 1);
        w.Chain(106, 2, 2, 2, 2);
        var options = new UnitPreviewOptions(false, false, true, 106);

        var zero = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, options);   // add-on position 0,0
        IsPicture(Assert.Single(zero.Layers), 130, 130);
        Assert.Empty(zero.Reasons);

        w.Set(DatId.Units, 107, "addon_position", 96);
        var badParent = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, options with { AddonParent = 300 });
        IsPicture(Assert.Single(badParent.Layers), 130, 130);
        Assert.Equal(["unit 300 is past units.dat's end"], badParent.Reasons);

        w.Chain(5, 1, 1, 1, 1);   // under 106: no add-on position in a vanilla file
        var plain = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 5, options);
        IsPicture(Assert.Single(plain.Layers), 130, 130);
        Assert.Empty(plain.Reasons);
    }

    [Fact]
    public void AChainPastAFilesEndNamesIt()
    {
        var w = new World();
        w.Set(DatId.Units, 0, "graphics", 250);
        w.Size(0, 32, 32);
        var scene = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 0, new UnitPreviewOptions(true, false, false, 106));
        Assert.Equal(["flingy 250 is past flingy.dat's end"], scene.Reasons);
        IsBox(Assert.Single(scene.Layers), 114, 114, 145, 145, Red);

        w.Chain(1, 1, 1, 1, 1);
        w.Set(DatId.Flingy, 1, "sprite", 600);
        Assert.Equal(["sprite 600 is past sprites.dat's end"], w.Preview.UnitStarEditScene(w.Dat, w.Strings, 1).Reasons);

        w.Set(DatId.Flingy, 1, "sprite", 1);
        w.Set(DatId.Sprites, 1, "image", 1200);
        Assert.Equal(["image 1200 is past images.dat's end"], w.Preview.UnitStarEditScene(w.Dat, w.Strings, 1).Reasons);

        Assert.Equal(["unit 300 is past units.dat's end"], w.Preview.UnitStarEditScene(w.Dat, w.Strings, 300).Reasons);

        w.Files.Remove(DatId.Flingy);
        Assert.Equal(["flingy.dat not found"], w.Preview.UnitStarEditScene(w.Dat, w.Strings, 1).Reasons);
    }

    [Fact]
    public void UnitStarEditScene()
    {
        var w = new World();
        w.Chain(0, 1, 1, 1, 1);
        w.Size(0, 33, 21);
        var scene = w.Preview.UnitStarEditScene(w.Dat, w.Strings, 0);
        Assert.Empty(scene.Reasons);
        Assert.Equal(2, scene.Layers.Count);
        IsPicture(scene.Layers[0], 130, 130);
        IsBox(scene.Layers[1], 114, 120, 145, 139, Red);
    }

    [Fact]
    public void SpriteSceneOrder()
    {
        var w = new World();
        w.Set(DatId.Sprites, 130, "image", 1);
        w.Set(DatId.Images, 1, "grp_file", 1);
        w.Set(DatId.Sprites, 130, "selection_circle_image", 2);
        w.Set(DatId.Sprites, 130, "selection_circle_offset", 10);
        w.Set(DatId.Sprites, 130, "health_bar", 13);
        w.Set(DatId.Images, 563, "grp_file", 3);

        var scene = w.Preview.SpriteScene(w.Dat, w.Strings, 130);
        Assert.Empty(scene.Reasons);
        Assert.Equal(7, scene.Layers.Count);
        IsPicture(scene.Layers[0], 130, 140);
        var circle = ((PictureLayer)scene.Layers[0]).Picture;
        Assert.Equal((1, 3), (circle.Top, circle.Bottom));
        Assert.Equal(DatPreview.HealthBar(140, circle, 13).Cast<PreviewLayer>(), scene.Layers.Skip(1).Take(5));
        IsPicture(scene.Layers[6], 130, 130);
    }

    [Fact]
    public void CircleAndBarOnlyWhereTheFileCoversThem()
    {
        var w = new World();
        w.Set(DatId.Sprites, 5, "image", 1);
        w.Set(DatId.Images, 1, "grp_file", 1);
        var vanilla = w.Preview.SpriteScene(w.Dat, w.Strings, 5);
        IsPicture(Assert.Single(vanilla.Layers), 130, 130);
        Assert.Empty(vanilla.Reasons);

        w.Files[DatId.Sprites] = Expanded(DatId.Sprites, 600);
        w.Set(DatId.Sprites, 5, "image", 1);
        w.Set(DatId.Images, 561, "grp_file", 3);
        var expanded = w.Preview.SpriteScene(w.Dat, w.Strings, 5);
        Assert.Equal(4, expanded.Layers.Count);   // circle, black bar, one box, the sprite
        Assert.Empty(expanded.Reasons);

        w.Grps.Remove("unit\\thingy\\o048.grp");
        var noCircle = w.Preview.SpriteScene(w.Dat, w.Strings, 5);
        IsPicture(Assert.Single(noCircle.Layers), 130, 130);
        Assert.Equal(["unit\\thingy\\o048.grp not found"], noCircle.Reasons);
    }

    [Fact]
    public void ImageSceneIsThePictureAtTheCentre()
    {
        var w = new World();
        w.Set(DatId.Images, 4, "grp_file", 1);
        IsPicture(Assert.Single(w.Preview.ImageScene(w.Dat, w.Strings, 4).Layers), 130, 130);
        var missing = w.Preview.ImageScene(w.Dat, w.Strings, 5);
        Assert.Empty(missing.Layers);
        Assert.Equal(["image 5 has no GRP file"], missing.Reasons);
    }

    [Fact]
    public void SoundPathAsPyDat()
    {
        var sfx = Zeros(DatId.Sfxdata).With(3, "sound_file", 1).With(4, "sound_file", 9);
        var tbl = StringTable.Parse(Tbl.Build("Terran\\Marine\\TMaPss00.wav\0"));
        Assert.Equal("sound\\Terran\\Marine\\TMaPss00.wav", DatPreview.SoundPath(sfx, tbl, 3));
        Assert.Null(DatPreview.SoundPath(sfx, tbl, 0));      // sound_file 0: No sound
        Assert.Null(DatPreview.SoundPath(sfx, tbl, 4));      // no string 9
        Assert.Null(DatPreview.SoundPath(sfx, null, 3));
        Assert.Null(DatPreview.SoundPath(sfx, tbl, sfx.Count));
    }

    [Fact]
    public void ReasonsAreDistinct()
    {
        var w = new World();
        w.Chain(107, 1, 1, 1, 1);
        w.Chain(106, 1, 1, 1, 1);   // the same GRP
        w.Set(DatId.Units, 107, "addon_position", 96);
        w.Grps.Clear();
        var scene = w.Preview.UnitGraphicsScene(w.Dat, w.Strings, 107, new UnitPreviewOptions(false, false, true, 106));
        Assert.Equal(["unit\\zerg\\drone.grp not found"], scene.Reasons);
    }

    [Fact]
    public void EmptySceneIsBlack()
    {
        Assert.All(PreviewScene.Empty.Render(), c => Assert.Equal(Black, c));
        Assert.Empty(PreviewScene.Empty.Reasons);
    }
}
