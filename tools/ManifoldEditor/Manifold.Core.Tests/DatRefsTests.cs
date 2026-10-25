using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class DatRefsTests
{
    static DatFile Zero(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    /// <summary>Every file zeroed except those given; a file left out of <paramref name="files"/> is a zero one, null in the dictionary is not open.</summary>
    static Func<DatId, DatFile?> Lookup(Dictionary<DatId, DatFile?> files) =>
        id => files.TryGetValue(id, out var f) ? f : Zero(id);

    [Fact]
    public void EveryRefNamesARealProperty()
    {
        Assert.NotEmpty(DatRefs.All);
        foreach (var r in DatRefs.All)
        {
            var from = DatFormats.Of(r.From);
            Assert.True(from.Properties.Any(p => p.Name == r.Property), $"{from.FileName} has no {r.Property}");
            if (r.RangeEnd is { } end)
                Assert.True(from.Properties.Any(p => p.Name == end), $"{from.FileName} has no {end}");
            if (r.SubTab is { } tab)
            {
                Assert.Equal(r.From, DatTabs.Of(tab).File);
                Assert.Contains(r.Property, DatTabs.Of(tab).Properties);
            }
        }
    }

    [Fact]
    public void WeaponIsUsedByUnitsAndOrders()
    {
        var units = Zero(DatId.Units).With(0, "ground_weapon", 3).With(0, "air_weapon", 3);
        var orders = Zero(DatId.Orders).With(10, "targeting", 3);
        var matches = DatRefs.UsedBy(DatId.Weapons, 3, Lookup(new() { [DatId.Units] = units, [DatId.Orders] = orders }));
        Assert.Equal(
        [
            new DatRefMatch(DatId.Units, 0, "Ground Weapon", DatTabId.UnitsBasic),
            new DatRefMatch(DatId.Units, 0, "Air Weapon", DatTabId.UnitsBasic),
            new DatRefMatch(DatId.Orders, 10, "Targeting", null),
        ], matches);
    }

    [Fact]
    public void SoundRangeMatchesInside()
    {
        var units = Zero(DatId.Units).With(2, "what_sound_start", 5).With(2, "what_sound_end", 8);
        var files = Lookup(new() { [DatId.Units] = units });
        var inside = DatRefs.UsedBy(DatId.Sfxdata, 7, files);
        Assert.Contains(new DatRefMatch(DatId.Units, 2, "What", DatTabId.UnitsSounds), inside);
        Assert.Contains(new DatRefMatch(DatId.Units, 2, "What", DatTabId.UnitsSounds), DatRefs.UsedBy(DatId.Sfxdata, 5, files));
        Assert.Contains(new DatRefMatch(DatId.Units, 2, "What", DatTabId.UnitsSounds), DatRefs.UsedBy(DatId.Sfxdata, 8, files));
        Assert.DoesNotContain(DatRefs.UsedBy(DatId.Sfxdata, 9, files), m => m.Label == "What");
        Assert.DoesNotContain(DatRefs.UsedBy(DatId.Sfxdata, 4, files), m => m.Label == "What");
    }

    [Fact]
    public void BackwardsRangeMatchesNothing()
    {
        var units = Zero(DatId.Units).With(2, "what_sound_start", 8).With(2, "what_sound_end", 5);
        var files = Lookup(new() { [DatId.Units] = units });
        foreach (int id in new[] { 5, 6, 7, 8 })
            Assert.DoesNotContain(DatRefs.UsedBy(DatId.Sfxdata, id, files), m => m.Entry == 2 && m.Label == "What");
    }

    [Fact]
    public void MissingFileIsSkipped()
    {
        var orders = Zero(DatId.Orders).With(10, "targeting", 3);
        var matches = DatRefs.UsedBy(DatId.Weapons, 3, Lookup(new() { [DatId.Units] = null, [DatId.Orders] = orders }));
        Assert.Equal([new DatRefMatch(DatId.Orders, 10, "Targeting", null)], matches);
    }

    [Fact]
    public void AnUncoveredPropertyNeverMatches()
    {
        // infestation only has a value on units 106-201; an all-zero file points at unit 0 elsewhere only.
        var matches = DatRefs.UsedBy(DatId.Units, 0, Lookup(new()));
        var infestation = matches.Where(m => m.Label == "Infestation").Select(m => m.Entry).ToArray();
        Assert.Equal(Enumerable.Range(106, 96), infestation);
        Assert.DoesNotContain(matches, m => m.Label == "Infestation" && m.Entry < 106);
    }

    [Fact]
    public void SameValueFindsOtherImages()
    {
        var images = Zero(DatId.Images).With(4, "grp_file", 9).With(7, "grp_file", 9).With(8, "grp_file", 2);
        var matches = DatRefs.SameValue(images, 4, "grp_file", "GRP File");
        Assert.Equal([new DatRefMatch(DatId.Images, 7, "GRP File", null)], matches);
        var scripts = Zero(DatId.Images).With(1, "iscript_id", 6).With(3, "iscript_id", 6).With(5, "iscript_id", 6);
        Assert.Equal(
        [
            new DatRefMatch(DatId.Images, 3, "IScript ID", null),
            new DatRefMatch(DatId.Images, 5, "IScript ID", null),
        ], DatRefs.SameValue(scripts, 1, "iscript_id", "IScript ID").Where(m => m.Entry is 3 or 5));
        Assert.DoesNotContain(DatRefs.SameValue(scripts, 1, "iscript_id", "IScript ID"), m => m.Entry == 1);
    }

    [Fact]
    public void DescribeIsPyDATsText()
    {
        Assert.Equal("units.dat, Ground Weapon field (Basic sub-tab), entry 0: Terran Marine",
            DatRefs.Describe(new DatRefMatch(DatId.Units, 0, "Ground Weapon", DatTabId.UnitsBasic), "Terran Marine"));
        Assert.Equal("orders.dat, Targeting field, entry 10: Attack",
            DatRefs.Describe(new DatRefMatch(DatId.Orders, 10, "Targeting", null), "Attack"));
    }
}
