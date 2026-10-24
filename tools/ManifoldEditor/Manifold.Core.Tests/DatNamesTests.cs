using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class DatNamesTests
{
    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    static StringTable Table(params string[] segments) => StringTable.Parse(Tbl.Build(segments));

    static DatLists Lists(params (string Name, string[] Entries)[] lists) =>
        DatLists.From(lists.ToDictionary(l => l.Name, l => l.Entries), new Dictionary<string, string>());

    static DatNameSources Sources(Func<DatId, DatFile?>? dat = null, StringTable? statTxt = null, StringTable? unitNames = null,
        StringTable? images = null, DatLists? lists = null) =>
        new(dat ?? (_ => null), statTxt, unitNames, images, null, null, null, lists ?? Lists());

    [Fact]
    public void BasicUsesTheList()
    {
        var sources = Sources(lists: Lists(("Units", ["Terran Marine", "Terran Ghost"])));
        var names = DatNames.Build(DatId.Units, sources, NamesMode.Basic, false);
        Assert.Equal(228, names.Count);
        Assert.Equal("Terran Marine", names[0]);
        Assert.Equal("Terran Ghost", names[1]);
        Assert.Equal("Unit #2", names[2]);
    }

    [Fact]
    public void TblUsesStatTxtAndFollowsEdits()
    {
        var table = Table("Baneling\0*\0Zerg\0");
        Assert.Equal("Baneling<0>*<0>Zerg", DatNames.Build(DatId.Units, Sources(statTxt: table), NamesMode.Tbl, false)[0]);
        Assert.Equal("Baneling", DatNames.Build(DatId.Units, Sources(statTxt: table), NamesMode.Tbl, true)[0]);
        var edited = table.With(1, Tbl.Latin1("Raptor\0*\0\0"));
        Assert.Equal("Raptor", DatNames.Build(DatId.Units, Sources(statTxt: edited), NamesMode.Tbl, true)[0]);
    }

    [Fact]
    public void SimpleAddsTheSecondPartUnlessItIsAStar()
    {
        var table = Table("Zergling\0Zerg\0Ground\0");
        Assert.Equal("Zergling (Zerg)", DatNames.Build(DatId.Units, Sources(statTxt: table), NamesMode.Tbl, true)[0]);
    }

    [Fact]
    public void UnitNamesTblWins()
    {
        var sources = Sources(statTxt: Table("Marine\0"), unitNames: Table("Hero\0*\0x\0"));
        Assert.StartsWith("Hero", DatNames.Build(DatId.Units, sources, NamesMode.Tbl, false)[0]);
    }

    [Fact]
    public void CombinedJoinsDifferentNames()
    {
        var lists = Lists(("Units", ["Terran Marine", "Same"]));
        var sources = Sources(statTxt: Table("Baneling\0*\0Zerg\0", "Same\0"), lists: lists);
        var names = DatNames.Build(DatId.Units, sources, NamesMode.Combined, true);
        Assert.Equal("Terran Marine (Baneling)", names[0]);
        Assert.Equal("Same", names[1]);
        // Only one of the two exists.
        Assert.Equal("Unit #2", names[2]);
        var onlyTbl = DatNames.Build(DatId.Units, Sources(statTxt: Table("Baneling\0")), NamesMode.Combined, true);
        Assert.Equal("Unit #0 (Baneling)", onlyTbl[0]);
        var onlyList = DatNames.Build(DatId.Units, Sources(lists: lists), NamesMode.Combined, true);
        Assert.Equal("Terran Marine", onlyList[0]);
    }

    [Fact]
    public void LabelsStripHotkeysWhenSimple()
    {
        var weapons = Zeros(DatId.Weapons).With(0, "label", 1).With(1, "label", 0).With(2, "label", 99);
        var sources = Sources(dat: id => id == DatId.Weapons ? weapons : null, statTxt: Table("G\u0001Gauss Rifle\0"));
        var raw = DatNames.Build(DatId.Weapons, sources, NamesMode.Tbl, false);
        var simple = DatNames.Build(DatId.Weapons, sources, NamesMode.Tbl, true);
        Assert.Equal("G<1>Gauss Rifle", raw[0]);
        Assert.Equal("Gauss Rifle", simple[0]);
        Assert.Equal("None", simple[1]);
        Assert.Equal("Weapon #2", simple[2]);
        Assert.True(DatNames.HasSimple(DatId.Weapons));
        Assert.False(DatNames.HasSimple(DatId.Images));
    }

    [Fact]
    public void NoneNamesDifferPerFile()
    {
        var dats = Enum.GetValues<DatId>().ToDictionary(id => id, Zeros);
        var sources = new DatNameSources(id => dats[id], Table("x\0"), null, Table("x\0"), Table("x\0"), Table("x\0"), Table("x\0"), Lists());
        Assert.Equal("No sound", DatNames.Build(DatId.Sfxdata, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("None", DatNames.Build(DatId.Portdata, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("None", DatNames.Build(DatId.Orders, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("None", DatNames.Build(DatId.Techdata, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("None", DatNames.Build(DatId.Upgrades, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("Map #0", DatNames.Build(DatId.Mapdata, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("Image #0", DatNames.Build(DatId.Images, sources, NamesMode.Tbl, false)[0]);
    }

    [Fact]
    public void ImagesSpritesFlingyChainThroughImagesTbl()
    {
        var images = Zeros(DatId.Images).With(0, "grp_file", 1);
        var sprites = Zeros(DatId.Sprites).With(3, "image", 0);
        var flingy = Zeros(DatId.Flingy).With(5, "sprite", 3);
        DatFile? Dat(DatId id) => id switch { DatId.Images => images, DatId.Sprites => sprites, DatId.Flingy => flingy, _ => null };
        var sources = Sources(Dat, images: Table("zerg\\marine.grp\0", "other.grp\0"));
        Assert.Equal("zerg\\marine.grp", DatNames.Build(DatId.Images, sources, NamesMode.Tbl, false)[0]);
        Assert.Equal("zerg\\marine.grp", DatNames.Build(DatId.Sprites, sources, NamesMode.Tbl, false)[3]);
        Assert.Equal("zerg\\marine.grp", DatNames.Build(DatId.Flingy, sources, NamesMode.Tbl, false)[5]);
        Assert.Equal("Image #1", DatNames.Build(DatId.Images, sources, NamesMode.Tbl, false)[1]);
    }

    [Fact]
    public void ExpandedEntriesAreNamedAsPyMS()
    {
        var images = Expanded(DatId.Images, 1001);
        var names = DatNames.Build(DatId.Images, Sources(dat: id => id == DatId.Images ? images : null,
            lists: Lists(("Images", Enumerable.Range(0, 999).Select(i => "Img" + i).ToArray()))), NamesMode.Basic, false);
        Assert.Equal(1001, names.Count);
        Assert.Equal("Img998", names[998]);
        Assert.Equal("Expanded Image #999", names[999]);
        Assert.Equal("Expanded Image #1000", names[1000]);

        var units = Expanded(DatId.Units, 252);
        var unitNames = DatNames.Build(DatId.Units, Sources(dat: id => id == DatId.Units ? units : null), NamesMode.Tbl, false);
        Assert.Equal(252, unitNames.Count);
        Assert.Equal("Reserved Unit #230", unitNames[230]);
        Assert.Equal("Reserved Unit #228", unitNames[228]);
        Assert.Equal("Expanded Unit #251", unitNames[251]);
        Assert.Equal("Unit #227", unitNames[227]);
    }

    [Fact]
    public void MissingSourcesFallBack()
    {
        Assert.Equal("Unit #0", DatNames.Build(DatId.Units, Sources(), NamesMode.Tbl, false)[0]);
        Assert.Equal("Unit #0", DatNames.Build(DatId.Units, Sources(), NamesMode.Combined, true)[0]);
        Assert.Equal("Image #3", DatNames.Build(DatId.Images, Sources(), NamesMode.Combined, false)[3]);
    }

    [Fact]
    public void TblStringIsOneSegmentWithoutItsFinalNul()
    {
        var table = Table("a\u0003b\0", "last");
        Assert.Equal("a<3>b", DatNames.TblString(table, 1));
        Assert.Equal("last", DatNames.TblString(table, 2));
        Assert.Null(DatNames.TblString(table, 0));
        Assert.Null(DatNames.TblString(table, 3));
        Assert.Null(DatNames.TblString(null, 1));
        Assert.Equal("Unit", DatNames.TypeName(DatId.Units));
        Assert.Equal("Tech", DatNames.TypeName(DatId.Techdata));
    }
}
