using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using static Manifold.Core.Editor.RequirementEdits;

namespace Manifold.Core.Tests;

/// <summary>Requirement edits as undo steps, the marks, and opening and saving requirements.bin (requirements spec §4, §6, §7).</summary>
public class ReqDocumentTests
{
    const string FgpPath = "Firegraft\\SCManifold.fgp";

    static Requirements Vanilla() => VanillaRequirements.FromBlock(Fixtures.Bytes("requirement-tables.bin"));
    static Requirements Project() =>
        RequirementsImport.FromProject(FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp")), Vanilla(), new List<string>());

    static (ButtonSetDocument Doc, EditorState State) Open(string? problem = null)
    {
        var project = Project();
        var set = new RequirementSet(project, problem is null ? project : null, Vanilla(), problem);
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), requirements: set);
        return (doc, new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()));
    }

    static ReqRecord Record(ButtonSetDocument doc, ReqTable table, int id) => doc.Requirements![table].Find(id)!;

    [Fact]
    public void EditIsOneLabelledStep()
    {
        var (doc, state) = Open();
        var before = doc.Requirements!;
        Assert.True(state.EditRequirement(ReqTable.Units, 0, r => Insert(r, new ReqLineAt(0, 3), new ReqLine(0x70, null)), "Marine"));
        Assert.Equal("Edit Marine's requirements", doc.UndoLabel);
        Assert.True(doc.RequirementsDirty);
        Assert.True(doc.IsDirty);
        Assert.Equal(new ReqLine(0x70, null), Record(doc, ReqTable.Units, 0).Lines[3]);
        Assert.True(state.Undo());
        Assert.True(doc.Requirements!.SameAs(before));
        Assert.Equal(new ReqPlace(ReqTable.Units, 0), state.LastReqPlace);
        Assert.False(doc.RequirementsDirty);
    }

    [Fact]
    public void UsedAndRecordSteps()
    {
        var (doc, state) = Open();
        Assert.True(state.SetRequirementUsed(ReqTable.Units, 0, false, "Marine"));
        Assert.Equal("Turn off Marine's requirements", doc.UndoLabel);
        Assert.False(Record(doc, ReqTable.Units, 0).Used);
        Assert.True(state.SetRequirementUsed(ReqTable.Units, 0, true, "Marine"));
        Assert.Equal("Turn on Marine's requirements", doc.UndoLabel);

        Assert.True(state.AddRequirementRecord(ReqTable.Units, 91, "Viking"));
        Assert.Equal("Add Viking's requirements record", doc.UndoLabel);
        Assert.Equal([(ushort)0xFFFF], Record(doc, ReqTable.Units, 91).ToWords());
        Assert.False(state.AddRequirementRecord(ReqTable.Units, 91, "Viking"));   // already there

        Assert.True(state.DeleteRequirementRecord(ReqTable.Units, 91, "Viking"));
        Assert.Equal("Delete Viking's requirements record", doc.UndoLabel);
        Assert.Null(doc.Requirements![ReqTable.Units].Find(91));

        Assert.True(state.RevertRequirementToVanilla(ReqTable.TechUse, 24, "Healing"));
        Assert.Equal("Revert Healing's requirements to vanilla", doc.UndoLabel);
        Assert.True(Record(doc, ReqTable.TechUse, 24).SameAs(Vanilla()[ReqTable.TechUse].Find(24)!));
    }

    [Fact]
    public void VanillaAndSavedMarks()
    {
        var (doc, state) = Open();
        Assert.True(doc.RecordDiffersFromVanilla(ReqTable.TechUse, 24));
        Assert.False(doc.RecordDiffersFromVanilla(ReqTable.Units, 0));
        Assert.False(doc.RecordChangedSinceSave(ReqTable.Units, 0));
        state.EditRequirement(ReqTable.Units, 0, r => Remove(r, new ReqLineAt(0, 2)), "Marine");
        Assert.True(doc.RecordChangedSinceSave(ReqTable.Units, 0));
        Assert.True(doc.RecordDiffersFromVanilla(ReqTable.Units, 0));
        doc.MarkSaved();
        Assert.False(doc.RecordChangedSinceSave(ReqTable.Units, 0));
        Assert.False(doc.RequirementsDirty);
        state.Undo();
        Assert.True(doc.RecordChangedSinceSave(ReqTable.Units, 0));
        Assert.True(doc.RequirementsDirty);
    }

    [Fact]
    public void ProblemBlocksEdits()
    {
        var (doc, state) = Open(problem: "section UntR cut short");
        Assert.Equal("section UntR cut short", doc.RequirementsProblem);
        Assert.False(doc.RequirementsDirty);
        Assert.False(state.SetRequirementUsed(ReqTable.Units, 0, false, "Marine"));
        doc.ReplaceBadRequirements();
        Assert.Null(doc.RequirementsProblem);
        Assert.True(doc.RequirementsDirty);
        Assert.False(doc.CanUndo);   // not an undo step
        Assert.True(state.SetRequirementUsed(ReqTable.Units, 0, false, "Marine"));
    }

    static FakeArchive Exe(bool fgp = true, byte[]? file = null)
    {
        var files = new Dictionary<string, byte[]>();
        if (fgp) files[FgpPath] = Fixtures.Bytes("SCManifold.fgp");
        if (file is not null) files[RequirementsFile.ArchivePath] = file;
        return new FakeArchive("SCManifold.exe", files);
    }

    [Fact]
    public void OpenPrefersTheFile()
    {
        var mine = Vanilla();
        mine = mine.With(mine[ReqTable.Units].Without(0));
        var status = new List<string>();
        var set = EditorSession.OpenRequirements(Exe(file: RequirementsFile.Write(mine)), FgpPath, Vanilla(), status);
        Assert.True(set.Opened.SameAs(mine));
        Assert.Same(set.Opened, set.Saved);
        Assert.Null(set.Problem);
        Assert.Contains($"Requirements from {RequirementsFile.ArchivePath}", status);
    }

    [Fact]
    public void OpenImportsTheProject()
    {
        var status = new List<string>();
        var set = EditorSession.OpenRequirements(Exe(), FgpPath, Vanilla(), status);
        Assert.True(set.Opened.SameAs(Project()));
        Assert.Null(set.Saved);
        Assert.Contains($"Imported FireGraft's requirements from {FgpPath}", status);
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), requirements: set);
        Assert.True(doc.RequirementsDirty);
    }

    [Fact]
    public void OpenWithNothingIsVanilla()
    {
        var status = new List<string>();
        var set = EditorSession.OpenRequirements(Exe(fgp: false), FgpPath, Vanilla(), status);
        Assert.True(set.Opened.SameAs(Vanilla()));
        Assert.Same(set.Opened, set.Saved);
    }

    [Fact]
    public void OpenBadFileSetsProblem()
    {
        var status = new List<string>();
        var set = EditorSession.OpenRequirements(Exe(file: "FgPa"u8.ToArray()), FgpPath, Vanilla(), status);
        Assert.NotNull(set.Problem);
        Assert.Null(set.Saved);
        Assert.True(set.Opened.SameAs(Project()));
        Assert.Contains(status, s => s.StartsWith(RequirementsFile.ArchivePath));
    }

    [Fact]
    public void OpenBadProjectIsVanilla()
    {
        var exe = Exe(fgp: false);
        exe.Files[FgpPath] = "FgPa\x07\0\0\01.16.1"u8.ToArray();   // the game version never ends
        var status = new List<string>();
        var set = EditorSession.OpenRequirements(exe, FgpPath, Vanilla(), status);
        Assert.True(set.Opened.SameAs(Vanilla()));
        Assert.Same(set.Opened, set.Saved);
        Assert.Contains(status, s => s.Contains(FgpPath) && s.Contains("no game version"));
    }

    [Fact]
    public void SaveWritesTheFileWhenDirty()
    {
        var (doc, state) = Open();
        var exe = Exe();
        Assert.Null(EditorSession.Save(exe, doc));
        Assert.False(exe.Files.ContainsKey(RequirementsFile.ArchivePath));

        state.SetRequirementUsed(ReqTable.Units, 0, false, "Marine");
        Assert.Null(EditorSession.Save(exe, doc));
        var written = RequirementsFile.Read(exe.Files[RequirementsFile.ArchivePath], Vanilla(), new List<string>());
        Assert.False(written[ReqTable.Units].Find(0)!.Used);
        Assert.False(doc.RequirementsDirty);

        var lines = Enumerable.Repeat(new ReqLine(0xFF05, null), 255).ToArray();
        state.EditRequirement(ReqTable.Units, 0, r => new ReqRecord(r.Id, r.Used, lines, []), "Marine");
        int writes = exe.Writes;
        var refused = EditorSession.Save(exe, doc);
        Assert.StartsWith($"{RequirementsFile.ArchivePath}: ", refused);
        Assert.EndsWith(". Nothing was saved.", refused);
        Assert.Equal(writes, exe.Writes);
        Assert.True(doc.RequirementsDirty);
    }

    [Fact]
    public void RowsAndLevelEdits()
    {
        var upgrade = Vanilla()[ReqTable.Upgrades].Find(0)!;
        var rows = Rows(upgrade);
        Assert.Equal(3 + 3 + 0 + 1 + 1, rows.Count);
        Assert.Equal([null, null, null, 1, 2, null, 3, null], rows.Select(r => r.LevelHeader));
        Assert.Equal(new ReqLineAt(2, 0), rows[5].At);
        Assert.Throws<InvalidOperationException>(() => AddLevel(upgrade));
        var two = RemoveLastLevel(upgrade);
        Assert.Equal(2, two.Levels.Count);
        Assert.Equal(3, AddLevel(two).Levels[^1].Level);
        // A line moves within its own lines only.
        Assert.True(Move(upgrade, new ReqLineAt(0, 2), +1).SameAs(upgrade));
        Assert.Equal(new ReqLine(0xFF07, null), Move(upgrade, new ReqLineAt(0, 2), -1).Lines[1]);
        Assert.Equal(new ReqLine(0xFF09, null), Replace(upgrade, new ReqLineAt(3, 0), new ReqLine(0xFF09, null)).Levels[2].Lines[0]);
    }
}
