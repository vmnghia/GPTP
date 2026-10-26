using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>
/// Reads PyMS's copies of the vanilla .dat files (MIT). Set MANIFOLD_ARR_DIR to the folder
/// that holds them (PyMS/MPQ/arr); without it these tests are skipped.
/// </summary>
public class DatVanillaTests
{
    static string? ArrDir => Environment.GetEnvironmentVariable("MANIFOLD_ARR_DIR");

    static DatFile Load(DatId id)
    {
        var format = DatFormats.Of(id);
        return DatFile.Parse(format, File.ReadAllBytes(Path.Combine(ArrDir!, format.FileName)));
    }

    [SkippableFact]
    public void EveryVanillaFileRoundTrips()
    {
        Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set");
        foreach (var format in DatFormats.All)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ArrDir!, format.FileName));
            var file = DatFile.Parse(format, bytes);
            Assert.False(file.IsExpanded);
            Assert.Equal(format.Entries, file.Count);
            Assert.Equal(bytes, file.Save());
        }
    }

    [SkippableFact]
    public void TheVanillaFilesHaveNoChecks()
    {
        Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set");
        var files = DatFormats.All.ToDictionary(f => f.Id, f => Load(f.Id));
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: new DatSet(files, files));
        Assert.Empty(DatChecks.Lines(doc));
    }

    [SkippableFact]
    public void SpotValues()
    {
        Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set");
        var units = Load(DatId.Units);
        Assert.Equal(10240UL, units.Get(0, "hit_points"));
        Assert.Equal(0UL, units.Get(0, "ground_weapon"));
        Assert.Equal(360UL, units.Get(0, "build_time"));
        Assert.Equal(50UL, units.Get(0, "mineral_cost"));
        Assert.Equal(10UL, units.Get(0, "staredit_group_flags"));
        Assert.Equal(2UL, units.Get(0, "supply_required"));
        Assert.Equal(78UL, units.Get(0, "graphics"));
        Assert.Equal(228UL, units.Get(0, "subunit1"));
        Assert.Equal(7UL, units.Get(0, "sight_range"));
        Assert.Equal(1310737UL, units.Get(0, "staredit_placement_size"));
        Assert.Equal(130UL, units.Get(106, "infestation"));
        Assert.Equal(6UL, units.Get(5, "subunit1"));

        var weapons = Load(DatId.Weapons);
        Assert.Equal(229UL, weapons.Get(0, "label"));
        Assert.Equal(6UL, weapons.Get(0, "damage_amount"));
        Assert.Equal(15UL, weapons.Get(0, "weapon_cooldown"));
        Assert.Equal(128UL, weapons.Get(0, "maximum_range"));
        Assert.Equal(323UL, weapons.Get(0, "icon"));
        Assert.Equal(7UL, weapons.Get(0, "damage_upgrade"));
        Assert.Equal(142UL, weapons.Get(0, "graphics"));

        var sprites = Load(DatId.Sprites);
        Assert.Equal(589UL, sprites.Get(0, "image"));
        Assert.Equal(19UL, sprites.Get(130, "health_bar"));

        var flingy = Load(DatId.Flingy);
        Assert.Equal(130UL, flingy.Get(0, "sprite"));
        Assert.Equal(1707UL, flingy.Get(0, "speed"));
        Assert.Equal(13616UL, flingy.Get(0, "halt_distance"));

        Assert.Equal(1UL, Load(DatId.Images).Get(0, "grp_file"));
    }

    /// <summary>
    /// Every vanilla unit, sprite and image through the previews, the GRPs from PyMS's MPQ
    /// folder (only the selection circles and cmdicons are there): each builds and renders,
    /// and a scene with no picture says why (previews spec §7).
    /// </summary>
    [SkippableFact]
    public void EveryVanillaPreviewDrawsOrGivesAReason()
    {
        Skip.If(ArrDir is null, "MANIFOLD_ARR_DIR is not set");
        string mpq = Path.Combine(ArrDir!, "..");
        byte[]? Read(string path)
        {
            string file = Path.Combine(mpq, path.Replace('\\', '/'));
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        var palettes = PreviewPalettes.Load(Fixtures.PathOf("palettes"));
        var preview = new DatPreview(Read, palettes);
        var files = DatFormats.All.ToDictionary(f => f.Id, f => Load(f.Id));
        DatFile? Dat(DatId id) => files[id];
        var tbl = StringTable.Parse(File.ReadAllBytes(Path.Combine(ArrDir!, "images.tbl")));

        void Check(PreviewScene scene, string what)
        {
            Assert.Equal(DatPreview.Size * DatPreview.Size, scene.Render().Length);
            if (!scene.Layers.Any(l => l is PictureLayer)) Assert.True(scene.Reasons.Count > 0, what);
        }
        var all = new UnitPreviewOptions(true, true, true, 106);
        for (int unit = 0; unit < files[DatId.Units].Count; unit++)
        {
            Check(preview.UnitGraphicsScene(Dat, tbl, unit, all), $"unit {unit}");
            Check(preview.UnitStarEditScene(Dat, tbl, unit), $"unit {unit} (StarEdit)");
        }
        for (int sprite = 0; sprite < files[DatId.Sprites].Count; sprite++) Check(preview.SpriteScene(Dat, tbl, sprite), $"sprite {sprite}");
        for (int image = 0; image < files[DatId.Images].Count; image++) Check(preview.ImageScene(Dat, tbl, image), $"image {image}");
        for (int image = 561; image <= 570; image++)
            Assert.NotNull(preview.Image(files[DatId.Images], tbl, image).Picture);
    }
}
