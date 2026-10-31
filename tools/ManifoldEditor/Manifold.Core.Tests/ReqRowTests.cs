using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using static Manifold.Core.Editor.RequirementEdits;

namespace Manifold.Core.Tests;

/// <summary>The requirement rows, one per word, and their in-place edits (requirements UX spec §3).</summary>
public class ReqRowTests
{
    static Requirements Vanilla() => VanillaRequirements.FromBlock(Fixtures.Bytes("requirement-tables.bin"));
    static ReqRecord Unit(int id) => Vanilla()[ReqTable.Units].Find(id)!;
    static ReqLineAt At(int index) => new(0, index);

    [Fact]
    public void MarineRows()
    {
        var rows = Rows(Unit(0));
        Assert.Equal([ReqRowKind.Opcode, ReqRowKind.Param, ReqRowKind.Opcode, ReqRowKind.Opcode, ReqRowKind.End], rows.Select(r => r.Kind));
        Assert.Equal(new ReqLine(0xFF02, 0x6F), rows[1].Line);
        Assert.Equal(At(0), rows[1].At);
    }

    [Fact]
    public void GhostRows()
    {
        var rows = Rows(Unit(1));
        Assert.Equal([ReqRowKind.MustHave, ReqRowKind.MustHave, ReqRowKind.End], rows.TakeLast(3).Select(r => r.Kind));
        Assert.Equal([(ushort)0x75, (ushort)0x70], rows.Where(r => r.Kind == ReqRowKind.MustHave).Select(r => r.Line!.Value.Word));
    }

    [Fact]
    public void UpgradeRows()
    {
        var rows = Rows(Vanilla()[ReqTable.Upgrades].Find(0)!);
        Assert.Equal([ReqRowKind.Opcode, ReqRowKind.Param, ReqRowKind.Opcode, ReqRowKind.Opcode, ReqRowKind.End,
            ReqRowKind.LevelHeader, ReqRowKind.End, ReqRowKind.LevelHeader, ReqRowKind.MustHave, ReqRowKind.End,
            ReqRowKind.LevelHeader, ReqRowKind.MustHave, ReqRowKind.End], rows.Select(r => r.Kind));
        Assert.Equal([0, 1, 2, 3], rows.Where(r => r.Kind == ReqRowKind.End).Select(r => r.Level));
    }

    [Fact]
    public void SetOpcodeKeepsOrDropsParam()
    {
        var marine = Unit(0);
        Assert.Equal(new ReqLine(0xFF03, 0x6F), SetOpcode(marine, ReqTable.Units, At(0), 0xFF03).Lines[0]);
        Assert.Equal(new ReqLine(0xFF05, null), SetOpcode(marine, ReqTable.Units, At(0), 0xFF05).Lines[0]);
        Assert.Equal(new ReqLine(0xFF02, 0), SetOpcode(marine, ReqTable.Units, At(1), 0xFF02).Lines[1]);
    }

    [Fact]
    public void SetOpcodeTechToUnit()
    {
        var r = new ReqRecord(103, true, [new ReqLine(0xFF25, 0x20)], []);
        Assert.Equal(new ReqLine(0xFF02, 0), SetOpcode(r, ReqTable.Units, At(0), 0xFF02).Lines[0]);
    }

    [Fact]
    public void SwitchBothWays()
    {
        var bare = ToParameter(Unit(0), ReqTable.Units, At(0));
        Assert.Equal(new ReqLine(0x6F, null), bare.Lines[0]);
        Assert.Equal(new ReqLine(0xFF02, 0x6F), ToOpcode(bare, At(0)).Lines[0]);
        var lurker = new ReqRecord(103, true, [new ReqLine(0xFF25, 0x20)], []);
        Assert.Equal(new ReqLine(0, null), ToParameter(lurker, ReqTable.Units, At(0)).Lines[0]);
        Assert.Equal(new ReqLine(0, null), ToParameter(Unit(0), ReqTable.Units, At(2)).Lines[2]);
    }

    [Fact]
    public void SetValueOnBoth()
    {
        Assert.Equal(new ReqLine(0xFF02, 0x70), SetValue(Unit(0), At(0), 0x70).Lines[0]);
        Assert.Equal(new ReqLine(0x71, null), SetValue(Unit(1), At(4), 0x71).Lines[4]);
    }

    [Fact]
    public void UnknownWordKept()
    {
        var r = new ReqRecord(0, true, [new ReqLine(0xFF15, null), new ReqLine(0xFF02, 0x6F)], []);
        Assert.Equal([ReqRowKind.Opcode, ReqRowKind.Opcode, ReqRowKind.Param, ReqRowKind.End], Rows(r).Select(x => x.Kind));
        Assert.Equal(new ReqLine(0xFF15, null), SetValue(r, At(1), 0x70).Lines[0]);
    }

    [Fact]
    public void NewLines()
    {
        Assert.Equal(new ReqLine(0xFF02, 0), NewLine(ReqTable.Units, 0xFF02, parameter: false));
        Assert.Equal(new ReqLine(0xFF05, null), NewLine(ReqTable.Units, 0xFF05, parameter: false));
        Assert.Equal(new ReqLine(0x70, null), NewLine(ReqTable.Units, 0x70, parameter: true));
    }

    [Fact]
    public void TableDirtyFollowsEdits()
    {
        var project = RequirementsImport.FromProject(FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp")), Vanilla(), new List<string>());
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(),
            requirements: new RequirementSet(project, project, Vanilla(), null));
        var state = new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions());
        Assert.All(ReqTables.All, t => Assert.False(doc.RequirementTableDirty(t)));
        state.EditRequirement(ReqTable.Units, 0, r => SetValue(r, At(0), 0x70), "Marine");
        Assert.Equal([true, false, false, false, false], ReqTables.All.Select(doc.RequirementTableDirty));
        doc.MarkSaved();
        Assert.False(doc.RequirementTableDirty(ReqTable.Units));
        state.Undo();
        Assert.True(doc.RequirementTableDirty(ReqTable.Units));
    }
}
