using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class DatDocumentTests
{
    static DatFile Zero(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatSet Vanilla() =>
        new(DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id)), DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id)));

    static ButtonSetDocument Document(DatSet? dats = null) =>
        new(Make.EmptySets(), Make.EmptySets(), dats: dats ?? Vanilla());

    static EditorState State(ButtonSetDocument doc) =>
        new(doc, "test.exe", FunctionTable.Parse(Fixtures.Text("FireGraftConFunc.txt")), FunctionTable.Parse(Fixtures.Text("FireGraftActFunc.txt")));

    [Fact]
    public void EditIsOneStepAndDirtiesTheFile()
    {
        var doc = Document();
        var state = State(doc);
        Assert.False(doc.IsDirty);
        Assert.True(state.EditDat(DatId.Units, 0, "armor", 3));
        Assert.Equal(3UL, doc.Dat(DatId.Units)!.Get(0, "armor"));
        Assert.True(doc.DatDirty(DatId.Units));
        Assert.False(doc.DatDirty(DatId.Weapons));
        Assert.True(doc.IsDirty);
        Assert.True(doc.CanUndo);
    }

    [Fact]
    public void UnchangedEditRecordsNothing()
    {
        var doc = Document();
        Assert.False(State(doc).EditDat(DatId.Units, 0, "armor", 0));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void RefusedEditsRecordNothing()
    {
        var doc = Document();
        var state = State(doc);
        Assert.False(state.EditDat(DatId.Units, 0, "armor", 256));       // a byte
        Assert.False(state.EditDat(DatId.Units, 500, "armor", 1));       // no such entry
        Assert.False(state.EditDat(DatId.Units, 0, "infestation", 1));   // not covered on entry 0
        Assert.False(doc.CanUndo);
        var without = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets());
        Assert.False(State(without).EditDat(DatId.Units, 0, "armor", 1));
    }

    [Fact]
    public void UndoReturnsTheDatPlace()
    {
        var doc = Document();
        var state = State(doc);
        state.EditDat(DatId.Units, 5, "armor", 1);
        state.EditDat(DatId.Weapons, 2, "damage_amount", 9);
        Assert.True(state.Undo());
        Assert.Equal(new DatPlace(DatId.Weapons, 2, "damage_amount"), state.LastDatPlace);
        Assert.Equal(0UL, doc.Dat(DatId.Weapons)!.Get(2, "damage_amount"));
        Assert.True(state.Undo());
        Assert.Equal(new DatPlace(DatId.Units, 5, "armor"), state.LastDatPlace);
        Assert.True(state.Redo());
        Assert.Equal(new DatPlace(DatId.Units, 5, "armor"), state.LastDatPlace);
        Assert.Equal(1UL, doc.Dat(DatId.Units)!.Get(5, "armor"));
    }

    [Fact]
    public void ASetStepHasNoDatPlace()
    {
        var doc = Document();
        var state = State(doc);
        state.EditDat(DatId.Units, 5, "armor", 1);
        Assert.True(doc.Apply(3, s => Make.Set(Make.Button(1)), "set"));
        state.Undo();
        Assert.Null(state.LastDatPlace);
        Assert.Equal(3, state.SelectedSetId);
    }

    [Fact]
    public void UndoneByHandIsClean()
    {
        var doc = Document();
        var state = State(doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        state.EditDat(DatId.Units, 0, "armor", 0);
        Assert.False(doc.DatDirty(DatId.Units));
    }

    [Fact]
    public void RevertEntryToOpenedAndVanilla()
    {
        var vanilla = Vanilla();
        var opened = new Dictionary<DatId, DatFile>(vanilla.Opened)
        {
            [DatId.Units] = vanilla.Opened[DatId.Units].With(0, "armor", 1),
        };
        var doc = Document(new DatSet(opened, vanilla.Vanilla));
        var state = State(doc);
        state.EditDat(DatId.Units, 0, "armor", 5);
        Assert.True(doc.EntryDiffersFromVanilla(DatId.Units, 0));

        Assert.True(state.RevertEntry(DatId.Units, 0, toVanilla: false));
        Assert.Equal(1UL, doc.Dat(DatId.Units)!.Get(0, "armor"));
        Assert.False(state.RevertEntry(DatId.Units, 0, toVanilla: false));   // matches
        Assert.True(state.RevertEntry(DatId.Units, 0, toVanilla: true));
        Assert.Equal(0UL, doc.Dat(DatId.Units)!.Get(0, "armor"));

        Assert.True(state.Undo());
        Assert.Equal(1UL, doc.Dat(DatId.Units)!.Get(0, "armor"));
        Assert.Equal(new DatPlace(DatId.Units, 0, null), state.LastDatPlace);
        Assert.True(state.Undo());
        Assert.Equal(5UL, doc.Dat(DatId.Units)!.Get(0, "armor"));
    }

    [Fact]
    public void RevertToVanillaRefusedPastVanillaCount()
    {
        var format = DatFormats.Of(DatId.Images);
        var expanded = DatFile.Parse(format, new byte[1001 * format.ExpandedEntrySize!.Value]);
        var vanilla = Vanilla();
        var opened = new Dictionary<DatId, DatFile>(vanilla.Opened) { [DatId.Images] = expanded };
        var doc = Document(new DatSet(opened, vanilla.Vanilla));
        var state = State(doc);
        Assert.True(state.EditDat(DatId.Images, 1000, "grp_file", 4));
        Assert.False(state.RevertEntry(DatId.Images, 1000, toVanilla: true));
        Assert.False(doc.EntryDiffersFromVanilla(DatId.Images, 1000));
        Assert.True(state.RevertEntry(DatId.Images, 1000, toVanilla: false));
    }

    [Fact]
    public void UseOpenedDatCountsAsUnsaved()
    {
        var doc = Document();
        var copy = doc.Dat(DatId.Units)!.With(1, "armor", 2);
        doc.UseOpenedDat(DatId.Units, copy);
        Assert.Same(copy, doc.OpenedDat(DatId.Units));
        Assert.Same(copy, doc.Dat(DatId.Units));
        Assert.True(doc.DatDirty(DatId.Units));
        Assert.True(doc.IsDirty);
        doc.MarkSaved();
        Assert.False(doc.DatDirty(DatId.Units));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void UseOpenedDatOnlyBeforeAnyEdit()
    {
        var doc = Document();
        State(doc).EditDat(DatId.Units, 0, "armor", 1);
        Assert.Throws<InvalidOperationException>(() => doc.UseOpenedDat(DatId.Units, doc.Dat(DatId.Units)!));
    }

    [Fact]
    public void MarkSavedMakesTheEditedFilesTheOpenedOnes()
    {
        var doc = Document();
        var state = State(doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        doc.MarkSaved();
        Assert.False(doc.DatDirty(DatId.Units));
        Assert.Equal(3UL, doc.OpenedDat(DatId.Units)!.Get(0, "armor"));
        Assert.Equal(0UL, doc.VanillaDat(DatId.Units)!.Get(0, "armor"));
        state.EditDat(DatId.Units, 0, "armor", 4);
        Assert.True(state.RevertEntry(DatId.Units, 0, toVanilla: false));
        Assert.Equal(3UL, doc.Dat(DatId.Units)!.Get(0, "armor"));
    }

    [Fact]
    public void EntryDiffersFromVanilla()
    {
        var doc = Document();
        State(doc).EditDat(DatId.Units, 3, "armor", 2);
        for (int e = 0; e < 228; e++) Assert.Equal(e == 3, doc.EntryDiffersFromVanilla(DatId.Units, e));
        Assert.False(doc.EntryDiffersFromVanilla(DatId.Units, 9999));
    }

    [Fact]
    public void AFileMissingEverywhereIsAbsent()
    {
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: new DatSet(
            new Dictionary<DatId, DatFile>(), new Dictionary<DatId, DatFile>()));
        Assert.Null(doc.Dat(DatId.Units));
        Assert.Null(doc.OpenedDat(DatId.Units));
        Assert.Null(doc.VanillaDat(DatId.Units));
        Assert.False(doc.DatDirty(DatId.Units));
        Assert.False(doc.EntryDiffersFromVanilla(DatId.Units, 0));
        Assert.False(State(doc).RevertEntry(DatId.Units, 0, true));
    }

    [Fact]
    public void UnitsViewReadsTheEditedFile()
    {
        var doc = Document();
        Assert.Equal(Race.None, UnitsDat.Of(doc.Dat(DatId.Units)!).RaceOf(0));
        State(doc).EditDat(DatId.Units, 0, "staredit_group_flags", 1);
        Assert.Equal(Race.Zerg, UnitsDat.Of(doc.Dat(DatId.Units)!).RaceOf(0));
    }
}
