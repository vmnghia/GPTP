using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>Copy, paste and import of PyDAT text in the document: each a single undo step (links spec §3).</summary>
public sealed class DatTextEditTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("dat-text-edit-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    static DatFile Zero(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatSet Vanilla() =>
        new(DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id)), DatFormats.All.ToDictionary(f => f.Id, f => Zero(f.Id)));

    static EditorState State(out ButtonSetDocument doc)
    {
        doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: Vanilla());
        return new EditorState(doc, "test.exe",
            FunctionTable.Parse(Fixtures.Text("FireGraftConFunc.txt")), FunctionTable.Parse(Fixtures.Text("FireGraftActFunc.txt")));
    }

    [Fact]
    public void CopyThenPasteIsOneUndoStep()
    {
        var state = State(out var doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        state.EditDat(DatId.Units, 0, "mineral_cost", 50);
        string text = state.CopyEntryText(DatId.Units, 0, null);
        Assert.StartsWith("Unit(0):\n", text);

        Assert.Null(state.PasteEntry(DatId.Units, 7, text));
        var units = doc.Dat(DatId.Units)!;
        Assert.Equal(3UL, units.Get(7, "armor"));
        Assert.Equal(50UL, units.Get(7, "mineral_cost"));
        Assert.Equal(0UL, units.Get(8, "armor"));

        Assert.True(state.Undo());
        Assert.Equal(0UL, doc.Dat(DatId.Units)!.Get(7, "armor"));
        Assert.Equal(new DatPlace(DatId.Units, 7, null), state.LastDatPlace);
        Assert.Equal(3UL, doc.Dat(DatId.Units)!.Get(0, "armor"));   // the earlier edits stay: one step undone
        Assert.True(state.Redo());
        Assert.Equal(3UL, doc.Dat(DatId.Units)!.Get(7, "armor"));
    }

    [Fact]
    public void PasteLabelIsPasteEntry()
    {
        var state = State(out var doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        Assert.Null(state.PasteEntry(DatId.Units, 1, state.CopyEntryText(DatId.Units, 0, null)));
        Assert.Equal("Paste entry", doc.UndoLabel);
    }

    [Fact]
    public void PasteOfTheSameTextRecordsNothingAndIsNotRefused()
    {
        var state = State(out var doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        string text = state.CopyEntryText(DatId.Units, 0, null);
        Assert.Null(state.PasteEntry(DatId.Units, 0, text));
        Assert.Equal("Edit armor", doc.UndoLabel);
    }

    [Fact]
    public void PasteOfTheWrongTypeChangesNothing()
    {
        var state = State(out var doc);
        state.EditDat(DatId.Weapons, 0, "damage_amount", 9);
        string weapon = state.CopyEntryText(DatId.Weapons, 0, null);
        var before = doc.Dat(DatId.Units)!;

        string? refused = state.PasteEntry(DatId.Units, 1, weapon);
        Assert.NotNull(refused);
        Assert.Contains("Invalid type", refused);
        Assert.Same(before, doc.Dat(DatId.Units));
        Assert.Equal("Edit damage_amount", doc.UndoLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("# nothing but a comment\n")]
    public void EmptyClipboardIsRefused(string? clipboard)
    {
        var state = State(out var doc);
        Assert.Equal("The clipboard holds no PyDAT entry", state.PasteEntry(DatId.Units, 0, clipboard));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void SubTabCopyHoldsOnlyThatTabsProperties()
    {
        var state = State(out _);
        string basic = state.CopyEntryText(DatId.Units, 0, DatTabId.UnitsBasic);
        Assert.Contains("armor ", basic);
        Assert.DoesNotContain("ready_sound", basic);
        string sounds = state.CopyEntryText(DatId.Units, 0, DatTabId.UnitsSounds);
        Assert.Contains("ready_sound ", sounds);
        Assert.DoesNotContain("armor ", sounds);
        // a tab of another file doesn't narrow; no tab means all
        Assert.Equal(state.CopyEntryText(DatId.Units, 0, null), state.CopyEntryText(DatId.Units, 0, DatTabId.Weapons));
    }

    [Fact]
    public void OutsideUnitsTheCopyIsTheWholeEntry()
    {
        var state = State(out _);
        Assert.Equal(state.CopyEntryText(DatId.Weapons, 0, null), state.CopyEntryText(DatId.Weapons, 0, DatTabId.Weapons));
        Assert.Equal(state.CopyEntryText(DatId.Flingy, 0, null), state.CopyEntryText(DatId.Flingy, 0, DatTabId.Flingy));
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("hello 5")]
    [InlineData("\tarmor 4\n")]
    public void TextWithNoHeaderIsNotAnEntry(string clipboard)
    {
        var state = State(out var doc);
        Assert.Equal("The clipboard holds no PyDAT entry", state.PasteEntry(DatId.Units, 0, clipboard));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void AHeaderWithABadLineKeepsItsLine()
    {
        var state = State(out var doc);
        string? refused = state.PasteEntry(DatId.Units, 0, "Unit(0):\n\tarmor 4\n\tarmor banana\n");
        Assert.StartsWith("line 3: ", refused);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void SubTabPasteTouchesOnlyWhatTheTextHolds()
    {
        var state = State(out var doc);
        state.EditDat(DatId.Units, 0, "armor", 3);
        state.EditDat(DatId.Units, 0, "ready_sound", 5);
        state.EditDat(DatId.Units, 1, "armor", 9);
        Assert.Null(state.PasteEntry(DatId.Units, 1, state.CopyEntryText(DatId.Units, 0, DatTabId.UnitsSounds)));
        Assert.Equal(5UL, doc.Dat(DatId.Units)!.Get(1, "ready_sound"));
        Assert.Equal(9UL, doc.Dat(DatId.Units)!.Get(1, "armor"));
    }

    [Fact]
    public void ImportLabelCountsEntries()
    {
        var state = State(out var doc);
        Assert.Null(state.ImportText(DatId.Units, "Unit(2):\n\tarmor 4\n\nUnit(5):\n\tarmor 6\n", "units.txt"));
        Assert.Equal("Import units.txt (2 entries)", doc.UndoLabel);

        // the count is the entries the text lists, not the ones that changed
        Assert.Null(state.ImportText(DatId.Units, "Unit(2):\n\tarmor 4\n\nUnit(3):\n\tarmor 0\n\nUnit(4):\n\tarmor 1\n", "three.txt"));
        Assert.Equal("Import three.txt (3 entries)", doc.UndoLabel);
        Assert.Equal(4UL, doc.Dat(DatId.Units)!.Get(2, "armor"));
        Assert.Equal(6UL, doc.Dat(DatId.Units)!.Get(5, "armor"));

        Assert.Null(state.ImportText(DatId.Units, "Unit(9):\n\tarmor 1\n", "one.txt"));
        Assert.Equal("Import one.txt (1 entry)", doc.UndoLabel);
    }

    [Fact]
    public void ImportIsOneStep()
    {
        var state = State(out var doc);
        Assert.Null(state.ImportText(DatId.Units, "Unit(2):\n\tarmor 4\n\nUnit(5):\n\tarmor 6\n", "units.txt"));
        Assert.True(state.Undo());
        Assert.Equal(0UL, doc.Dat(DatId.Units)!.Get(2, "armor"));
        Assert.Equal(0UL, doc.Dat(DatId.Units)!.Get(5, "armor"));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void ImportRefusalNamesTheLine()
    {
        var state = State(out var doc);
        string? refused = state.ImportText(DatId.Units, "Unit(2):\n\tarmor 4\n\nUnit(3):\n\tarmor banana\n", "bad.txt");
        Assert.NotNull(refused);
        Assert.StartsWith("line 5: ", refused);
        Assert.False(doc.CanUndo);

        // a refusal from Apply carries its entry's line too, and nothing at all is applied
        refused = state.ImportText(DatId.Units, "Unit(2):\n\tarmor 4\n\nWeapon(3):\n\tdamage_amount 1\n", "mixed.txt");
        Assert.StartsWith("line 4: Invalid type", refused);
        Assert.Equal(0UL, doc.Dat(DatId.Units)!.Get(2, "armor"));

        // no line: the message alone
        Assert.Equal("No entries found", state.ImportText(DatId.Units, "", "empty.txt"));
    }

    [Fact]
    public void UndoOfImportShowsTheFirstChangedEntry()
    {
        var state = State(out _);
        // entry 4 is listed second but entry 2 is the first to change; entry 1 is listed and unchanged
        Assert.Null(state.ImportText(DatId.Units, "Unit(4):\n\tarmor 4\n\nUnit(1):\n\tarmor 0\n\nUnit(2):\n\tarmor 6\n", "units.txt"));
        Assert.True(state.Undo());
        Assert.Equal(new DatPlace(DatId.Units, 2, null), state.LastDatPlace);
    }

    [Fact]
    public void ImportOfNothingNewRecordsNothing()
    {
        var state = State(out var doc);
        Assert.Null(state.ImportText(DatId.Units, "Unit(2):\n\tarmor 0\n", "units.txt"));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void UsedByOpenDefaultsTrue()
    {
        Assert.True(new EditorSettings().UsedByOpen);
        string path = Path.Combine(root, "settings.json");
        new EditorSettings { UsedByOpen = false }.Save(path);
        Assert.False(EditorSettings.Load(path).UsedByOpen);
        new EditorSettings().Save(path);
        Assert.True(EditorSettings.Load(path).UsedByOpen);
        File.WriteAllText(path, """{ "LastExe": "C:\\x.exe" }""");
        Assert.True(EditorSettings.Load(path).UsedByOpen);
    }
}
