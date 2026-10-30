using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

/// <summary>DatText.Export against PyMS's own text (fixtures/pydat-txt, made by make_goldens.py).</summary>
public class DatTextExportTests
{
    public static IEnumerable<object[]> Goldens() =>
        new[]
        {
            "units", "weapons", "flingy", "sprites", "images", "upgrades", "techdata", "sfxdata", "portdata",
            "mapdata", "orders", "units-expanded", "images-expanded",
        }.Select(name => new object[] { name });

    static DatFile Load(string name)
    {
        string stem = name.Replace("-expanded", "");
        var format = DatFormats.All.Single(f => f.FileName == stem + ".dat");
        return DatFile.Parse(format, Fixtures.Bytes($"pydat-txt/{name}.dat"));
    }

    [Theory, MemberData(nameof(Goldens))]
    public void ExportMatchesPyMS(string name)
    {
        var file = Load(name);
        Assert.Equal(Fixtures.Text($"pydat-txt/{name}.txt"), DatText.Export(file, Enumerable.Range(0, file.Count)));
    }

    [Fact]
    public void TheExpandedGoldensAreExpanded()
    {
        Assert.Equal(252, Load("units-expanded").Count);
        Assert.True(Load("units-expanded").IsExpanded);
        Assert.Equal(1001, Load("images-expanded").Count);
        Assert.True(Load("images-expanded").IsExpanded);
    }

    [Fact]
    public void EveryPropertyHasATextKey()
    {
        foreach (var format in DatFormats.All)
        {
            var keyed = DatText.Keys(format.Id).Select(k => k.Property).ToHashSet();
            foreach (var p in format.Properties)
                Assert.True(keyed.Contains(p.Name), $"{format.FileName}: no text key for {p.Name}");
            foreach (var k in DatText.Keys(format.Id))
                Assert.True(format.Properties.Any(p => p.Name == k.Property), $"{format.FileName}: {k.Key} names no property");
        }
    }

    [Fact]
    public void ExportNamesArePyMSs()
    {
        var names = DatFormats.All.Select(f => DatText.ExportName(f.Id)).ToArray();
        Assert.Equal(["Unit", "Weapon", "Flingy", "Sprite", "Image", "Upgrade", "Technology", "Sound", "Portraits", "Map", "Order"], names);
        Assert.Equal("Technology", DatText.ExportName(DatId.Techdata));
        Assert.Equal("Portraits", DatText.ExportName(DatId.Portdata));
        Assert.Equal("Map", DatText.ExportName(DatId.Mapdata));
    }

    [Fact]
    public void SubTabExportHoldsOnlyItsProperties()
    {
        var units = Load("units");
        string text = DatText.Export(units, [0], DatTabs.Of(DatTabId.UnitsSounds).Properties);
        var lines = text.Split('\n');
        Assert.Equal("Unit(0):", lines[0]);
        Assert.Equal(
            ["ready_sound", "what_sound_start", "what_sound_end", "pissed_sound_start", "pissed_sound_end", "yes_sound_start", "yes_sound_end"],
            lines.Skip(1).SkipLast(1).Select(l => l.TrimStart('\t').Split(' ')[0]).ToArray());
        Assert.Equal("", lines[^1]);   // the text ends with the last value's newline
    }

    [Fact]
    public void ASubTabOfCompoundsKeepsAllTheirLines()
    {
        string text = DatText.Export(Load("units"), [0], DatTabs.Of(DatTabId.UnitsAdvanced).Properties);
        Assert.Contains("\tspecial_ability_flags.produces_units ", text);
        Assert.Contains("\tunknown_flags.flag_0x80 ", text);
        Assert.DoesNotContain("hit_points", text);
    }

    [Fact]
    public void AnEmptyOrNullPropertyListMeansAll()
    {
        var file = Load("orders");
        string all = DatText.Export(file, [3]);
        Assert.Equal(all, DatText.Export(file, [3], []));
        Assert.Equal(all, DatText.Export(file, [3], null));
    }

    [Fact]
    public void UncoveredValuesAreLeftOut()
    {
        var sprites = Load("sprites");
        Assert.False(sprites.Covers(5, "health_bar"));
        string text = DatText.Export(sprites, [5]);
        Assert.DoesNotContain("health_bar", text);
        Assert.DoesNotContain("selection_circle", text);
        Assert.Contains("\timage ", text);
        Assert.Contains("health_bar", DatText.Export(sprites, [200]));
    }

    [Fact]
    public void BoolsAreWordsOnlyForZeroAndOne()
    {
        var orders = Load("orders");
        string text = DatText.Export(orders, [0, 1, 2]);
        Assert.Contains("\tuse_weapon_targeting False\n", text);
        Assert.Contains("\tuse_weapon_targeting True\n", text);
        Assert.Matches(@"\tuse_weapon_targeting \d+\n", text);
    }

    [Fact]
    public void FlagsListEveryBitBelowTheirCount()
    {
        string text = DatText.Export(Load("sfxdata"), [0]);
        Assert.Contains("\tflags.preload ", text);
        Assert.Contains("\tflags.flag_0x04 ", text);
        Assert.Contains("\tflags.flag_0x08 ", text);
        Assert.Contains("\tflags.never_preempt ", text);
        Assert.DoesNotContain("flag_0x40", text);
    }

    [Fact]
    public void ThePortraitKeysHoldTheDot()
    {
        var keys = DatText.Keys(DatId.Portdata);
        Assert.Equal(["idle.portrait_file", "idle.smk_change", "idle.unknown", "talking.portrait_file", "talking.smk_change", "talking.unknown"],
            keys.Select(k => k.Key).ToArray());
        Assert.Equal("idle_smk_change", keys[1].Property);
        Assert.All(keys, k => Assert.Equal(DatCoderKind.Plain, k.Coder.Kind));
    }

    [Fact]
    public void OrdersTargetingIsExportedAsWeaponTargeting()
    {
        var key = DatText.Keys(DatId.Orders).Single(k => k.Property == "targeting");
        Assert.Equal("weapon_targeting", key.Key);
    }
}
