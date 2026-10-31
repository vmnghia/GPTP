using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

/// <summary>The requirements model: records as lines, upgrade levels, and the opcodes (requirements spec §3).</summary>
public class ReqModelTests
{
    static ReqRecord Parse(ReqTable table, int id, params ushort[] words) => ReqRecord.FromWords(table, id, true, words);

    [Fact]
    public void MarineRoundTrips()
    {
        ushort[] words = [0xFF02, 0x006F, 0xFF08, 0xFF05, 0xFFFF];
        var r = Parse(ReqTable.Units, 0, words);
        Assert.Equal([new ReqLine(0xFF02, 0x6F), new ReqLine(0xFF08, null), new ReqLine(0xFF05, null)], r.Lines);
        Assert.Empty(r.Levels);
        Assert.Equal(words, r.ToWords());
    }

    [Fact]
    public void GhostBareIdsAreMustHave()
    {
        var r = Parse(ReqTable.Units, 1, 0xFF02, 0x006F, 0xFF08, 0xFF05, 0x0075, 0x0070, 0xFFFF);
        Assert.Equal(new ReqLine(0x75, null), r.Lines[3]);
        Assert.Equal(new ReqLine(0x70, null), r.Lines[4]);
    }

    [Fact]
    public void UpgradeLevelsRoundTrip()
    {
        ushort[] words = [0xFF02, 0x007A, 0xFF05, 0xFF07, 0xFF1F, 0xFFFF, 0xFF20, 0x0074, 0xFFFF, 0xFF21, 0x0074, 0xFFFF];
        var r = Parse(ReqTable.Upgrades, 0, words);
        Assert.Equal(3, r.Lines.Count);
        Assert.Equal([1, 2, 3], r.Levels.Select(l => l.Level));
        Assert.Empty(r.Levels[0].Lines);
        Assert.Equal([new ReqLine(0x74, null)], r.Levels[1].Lines);
        Assert.Equal([new ReqLine(0x74, null)], r.Levels[2].Lines);
        Assert.Equal(words, r.ToWords());
    }

    [Fact]
    public void EmptyRecordIsOneTerminator()
    {
        Assert.Equal([(ushort)0xFFFF], new ReqRecord(5, true, [], []).ToWords());
        var r = Parse(ReqTable.Units, 5, 0xFFFF);
        Assert.Empty(r.Lines);
        Assert.Empty(r.Levels);
    }

    [Fact]
    public void TechParameterOnUnitsOnly()
    {
        var lurker = Parse(ReqTable.Units, 103, 0xFF24, 0xFF02, 0x0026, 0xFF25, 0x0020, 0xFFFF);
        Assert.Equal(new ReqLine(0xFF25, 0x20), lurker.Lines[2]);
        var order = Parse(ReqTable.Orders, 0, 0xFF25, 0xFFFF);
        Assert.Equal([new ReqLine(0xFF25, null)], order.Lines);
    }

    [Fact]
    public void WordsAfterTheEndRefused() =>
        Assert.Throws<InvalidDataException>(() => Parse(ReqTable.Orders, 0, 0xFF1E, 0xFFFF, 0x0001));

    [Fact]
    public void MissingTerminatorRefused() =>
        Assert.Throws<InvalidDataException>(() => Parse(ReqTable.Units, 0, 0xFF02, 0x006F));

    [Fact]
    public void OfferedLists()
    {
        var units = ReqOpcodes.Offered(ReqTable.Units).Select(o => o.Word).ToArray();
        var orders = ReqOpcodes.Offered(ReqTable.Orders).Select(o => o.Word).ToArray();
        Assert.Contains((ushort)0xFF04, units);
        Assert.Contains((ushort)0xFF25, units);
        Assert.DoesNotContain((ushort)0xFF12, units);
        Assert.Contains((ushort)0xFF1E, orders);
        Assert.Contains((ushort)0xFF12, orders);
        Assert.DoesNotContain((ushort)0xFF04, orders);
        Assert.DoesNotContain((ushort)0xFF1F, units);
        Assert.DoesNotContain((ushort)0xFF1F, orders);
        Assert.Equal(ReqOpcodes.MustHave, units[0]);
        Assert.Equal(ReqOpcodes.MustHave, orders[0]);
    }

    [Fact]
    public void DescribeLines()
    {
        string Unit(int u) => u == 0x6F ? "Terran Barracks" : u == 0x70 ? "Terran Academy" : "?";
        string Tech(int t) => t == 32 ? "Lurker Aspect" : "?";
        Assert.Equal("Current unit is Terran Barracks", ReqOpcodes.Describe(ReqTable.Units, new ReqLine(0xFF02, 0x6F), Unit, Tech));
        Assert.Equal("Must have Terran Academy", ReqOpcodes.Describe(ReqTable.Units, new ReqLine(0x70, null), Unit, Tech));
        Assert.Equal("or", ReqOpcodes.Describe(ReqTable.Orders, new ReqLine(0xFF01, null), Unit, Tech));
        Assert.Equal("Unknown 0xFF12", ReqOpcodes.Describe(ReqTable.Units, new ReqLine(0xFF12, null), Unit, Tech));
        Assert.Equal("Has researched Lurker Aspect", ReqOpcodes.Describe(ReqTable.Units, new ReqLine(0xFF25, 0x20), Unit, Tech));
    }

    [Fact]
    public void TableWithReplacesInPlace()
    {
        var records = new[] { 0, 1, 2 }.Select(id => new ReqRecord(id, true, [], [])).ToArray();
        var table = new RequirementTable(ReqTable.Units, records);
        var changed = new ReqRecord(1, true, [new ReqLine(0xFF05, null)], []);
        var replaced = table.With(changed);
        Assert.Equal([0, 1, 2], replaced.Records.Select(r => r.Id));
        Assert.True(replaced.Find(1)!.SameAs(changed));
        Assert.Equal([0, 1, 2, 7], table.With(new ReqRecord(7, true, [], [])).Records.Select(r => r.Id));
        Assert.Equal([0, 2], table.Without(1).Records.Select(r => r.Id));
        Assert.Null(table.Find(9));
    }
}
