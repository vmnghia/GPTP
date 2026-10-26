using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class DatValuesTests
{
    static string Pydat => Fixtures.PathOf("pydat");

    [Fact]
    public void ParseNumberRefusesWhatTheTypeCantHold()
    {
        Assert.Null(DatValues.ParseNumber("", DatType.Byte)); Assert.Null(DatValues.ParseNumber("-1", DatType.Byte));
        Assert.Null(DatValues.ParseNumber("abc", DatType.Byte)); Assert.Null(DatValues.ParseNumber("0x10", DatType.Byte));
        Assert.Null(DatValues.ParseNumber("256", DatType.Byte)); Assert.Equal(255UL, DatValues.ParseNumber(" 255 ", DatType.Byte));
        Assert.Equal(4294967295UL, DatValues.ParseNumber("4294967295", DatType.Long));
        Assert.Null(DatValues.ParseNumber("4294967296", DatType.Long));
        Assert.Null(DatValues.ParseNumber("99999999999999999999999", DatType.Extents));
    }

    [Fact]
    public void ParseIdTakesHex()
    {
        Assert.Equal(16UL, DatValues.ParseId("0x10")); Assert.Equal(16UL, DatValues.ParseId("16"));
        Assert.Equal(16UL, DatValues.ParseId(" 0X10 ")); Assert.Null(DatValues.ParseId("x")); Assert.Null(DatValues.ParseId("0x"));
        Assert.Null(DatValues.ParseId(""));
    }

    [Fact]
    public void CompoundParts()
    {
        Assert.Equal((40u, (byte)0), DatValues.HitPoints(10240)); Assert.Equal(10240UL + 127, DatValues.HitPoints(40, 127));
        Assert.Equal(((byte)1, false), DatValues.Supply(2)); Assert.Equal(3UL, DatValues.Supply(1, true));
        Assert.Equal((ushort)17, DatValues.Part(1310737, 0)); Assert.Equal((ushort)20, DatValues.Part(1310737, 1));
        Assert.Equal(1310737UL, DatValues.WithPart(DatValues.WithPart(0, 0, 17), 1, 20));
        Assert.Equal(15.0, DatValues.Seconds(360));
        Assert.Equal(2.5, DatValues.Pixels(640));
    }

    [Fact]
    public void ExtentsHaveFourParts()
    {
        ulong v = DatValues.WithPart(DatValues.WithPart(DatValues.WithPart(DatValues.WithPart(0, 0, 1), 1, 2), 2, 3), 3, 4);
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, Enumerable.Range(0, 4).Select(i => DatValues.Part(v, i)).ToArray());
        Assert.Equal((ushort)9, DatValues.Part(DatValues.WithPart(v, 2, 9), 2));
        Assert.Equal((ushort)4, DatValues.Part(DatValues.WithPart(v, 2, 9), 3));
    }

    static byte[] Bin(params ushort[] words) => words.SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void IscriptIdsReadTheTable()
    {
        // the table starts at 4; pairs (2,x),(0,x), then the terminator (0xFFFF,0)
        var bin = Bin(4, 0, 2, 100, 0, 100, 0xFFFF, 0, 7, 7);
        Assert.Equal([0, 2], IscriptIds.Read(bin));
        Assert.Equal(["A", "*Unused*", "Unnamed Custom Entry"], IscriptIds.Names([0, 2], ["A", "B"]));
    }

    [Fact]
    public void IscriptNamesFillGapsAndOverrunsOfTheList()
    {
        Assert.Equal(["A", "*Unused*", "B", "Unnamed Custom Entry"], IscriptIds.Names([0, 2, 3], ["A", "x", "B"]));
        Assert.Equal(["*Unused*", "*Unused*", "B"], IscriptIds.Names([2], ["A", "x", "B"]));
        Assert.Empty(IscriptIds.Names([], ["A"]));
    }

    [Fact]
    public void IscriptIdsReadATableThatRunsToTheEnd()
    {
        Assert.Equal([1, 3], IscriptIds.Read(Bin(2, 3, 9, 1, 9)));
        Assert.Empty(IscriptIds.Read(Bin(2)));
        Assert.Empty(IscriptIds.Read([]));
    }

    [Fact]
    public void ListsAndHintsLoadFromFixtures()
    {
        var lists = DatLists.Load(Pydat);
        Assert.Equal(228, lists["Units"].Count);
        Assert.Equal("Terran Marine", lists["Units"][0]);
        Assert.StartsWith("Unit Hit Points", lists.Hint("UnitHP"));
        Assert.Null(lists.Hint("NoSuchHint"));
        Assert.Empty(lists["NoSuchList"]);
    }

    [Fact]
    public void EveryListTheEditorReadsIsInFixtures()
    {
        foreach (string name in DatLists.Names) Assert.True(File.Exists(Path.Combine(Pydat, name + ".txt")), name);
        Assert.True(File.Exists(Path.Combine(Pydat, "Hints.txt")));
        Assert.True(File.Exists(Path.Combine(Pydat, "LICENSE.md")));
    }

    [Fact]
    public void ALoadOfAMissingFolderIsEmpty()
    {
        var lists = DatLists.Load(Path.Combine(Pydat, "nowhere"));
        Assert.Empty(lists["Units"]); Assert.Null(lists.Hint("UnitHP"));
    }

    [Fact]
    public void FromTakesTheListsAsGiven()
    {
        var lists = DatLists.From(new Dictionary<string, string[]> { ["Units"] = ["a", "b"] }, new Dictionary<string, string> { ["K"] = "v" });
        Assert.Equal(["a", "b"], lists["Units"]); Assert.Equal("v", lists.Hint("K")); Assert.Empty(lists["Weapons"]);
    }

    [Fact]
    public void EntrySearchMatchesNamesAndIds()
    {
        string[] names = ["Marine", "Ghost", "Firebat"];
        Assert.Equal([1], EntrySearch.Filter(names, "gh"));
        Assert.Equal([2], EntrySearch.Filter(names, "0x2"));
        Assert.Equal([0, 1, 2], EntrySearch.Filter(names, ""));
        Assert.Equal([0, 1, 2], EntrySearch.Filter(names, "  "));
        Assert.Equal([1], EntrySearch.Filter(names, " GHOST "));
        Assert.Equal([2], EntrySearch.Filter(names, "2"));
        Assert.Empty(EntrySearch.Filter(names, "7"));
    }
}
