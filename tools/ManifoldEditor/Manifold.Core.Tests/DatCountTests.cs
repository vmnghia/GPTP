using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>Entry counts: PyMS's growth rules, the undo steps, and entries changed since save (counts spec §2).</summary>
public class DatCountTests
{
    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);
    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    // -------- The rules (Task 1) --------

    [Fact]
    public void ExpandedCountAsPyMs()
    {
        var units = Zeros(DatId.Units);
        Assert.Equal(252, units.ExpandedCount(229));   // the minimum, 250, rounds up to the step of 12
        Assert.Equal(252, units.ExpandedCount(251));
        Assert.Equal(912, units.ExpandedCount(912));
        Assert.Null(units.ExpandedCount(913));
        Assert.Equal(264, Expanded(DatId.Units, 252).ExpandedCount(253));
        Assert.Equal(131, Zeros(DatId.Weapons).ExpandedCount(131));
        Assert.Null(Zeros(DatId.Weapons).ExpandedCount(256));
        var images = Zeros(DatId.Images);
        Assert.Equal(1000, images.ExpandedCount(1000));
        Assert.Equal(65536, images.ExpandedCount(65536));
        Assert.Null(images.ExpandedCount(65537));
        Assert.Null(Zeros(DatId.Mapdata).ExpandedCount(2));
    }

    [Fact]
    public void ExpandedCountRefusesHugeAndNegative()
    {
        var images = Zeros(DatId.Images);
        Assert.Null(images.ExpandedCount(long.MaxValue));
        Assert.Null(images.ExpandedCount(0));
        Assert.Null(images.ExpandedCount(-5));
    }

    [Fact]
    public void CanGrowAsTheLimits()
    {
        Assert.True(Zeros(DatId.Images).CanGrow);
        Assert.True(Zeros(DatId.Units).CanGrow);
        Assert.True(Zeros(DatId.Weapons).CanGrow);
        Assert.True(Zeros(DatId.Sfxdata).CanGrow);
        Assert.False(Zeros(DatId.Mapdata).CanGrow);
        Assert.False(Expanded(DatId.Weapons, 255).CanGrow);
    }

    [Fact]
    public void FirstGrowthRewritesTheLayout()
    {
        var sprites = Zeros(DatId.Sprites).With(130, "health_bar", 7).With(5, "image", 3);
        var grown = sprites.WithCount(518);
        Assert.True(grown.IsExpanded);
        Assert.Equal(518, grown.Count);
        Assert.Equal(7UL, grown.Get(130, "health_bar"));
        Assert.Equal(3UL, grown.Get(5, "image"));
        Assert.Equal(0UL, grown.Get(5, "health_bar"));
        foreach (var p in grown.Format.Properties) Assert.Equal(0UL, grown.Get(517, p.Name));
    }

    [Fact]
    public void UnitsGraphicsKeepsItsValueAsAShort()
    {
        var grown = Zeros(DatId.Units).With(0, "graphics", 200).WithCount(229);
        Assert.Equal(252, grown.Count);
        Assert.Equal(200UL, grown.Get(0, "graphics"));
        Assert.Equal(300UL, grown.With(0, "graphics", 300).Get(0, "graphics"));
    }

    [Fact]
    public void AGrownFileSavesAndParsesBack()
    {
        foreach (var id in new[] { DatId.Images, DatId.Units, DatId.Sprites })
        {
            var grown = Zeros(id).With(1, DatFormats.Of(id).Properties[0].Name, 1).WithCount(Zeros(id).Count + 1);
            var back = DatFile.Parse(grown.Format, grown.Save());
            Assert.True(back.IsExpanded);
            Assert.Equal(grown.Count, back.Count);
            Assert.True(back.SameAs(grown));
        }
    }

    [Fact]
    public void WithCountRefuses()
    {
        var images = Zeros(DatId.Images);
        Assert.Same(images, images.WithCount(images.Count));
        var fewer = Assert.Throws<ArgumentOutOfRangeException>(() => images.WithCount(images.Count - 1));
        Assert.Contains("images.dat has 999 entries: it can't have fewer", fewer.Message);
        var many = Assert.Throws<ArgumentOutOfRangeException>(() => images.WithCount(70000));
        Assert.Contains("images.dat has at most 65536 entries", many.Message);
    }

    // -------- The undo steps and the unsaved mark (Task 2) --------

    static DatSet Vanilla() =>
        new(DatFormats.All.ToDictionary(f => f.Id, f => Zeros(f.Id)), DatFormats.All.ToDictionary(f => f.Id, f => Zeros(f.Id)));

    static (ButtonSetDocument Doc, EditorState State) Open()
    {
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: Vanilla());
        return (doc, new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()));
    }

    [Fact]
    public void AddEntryIsOneStep()
    {
        var (doc, state) = Open();
        byte[] vanilla = doc.Dat(DatId.Images)!.Save();
        Assert.True(state.AddEntry(DatId.Images, 4));
        Assert.Equal(1000, doc.Dat(DatId.Images)!.Count);
        Assert.Equal("Add entry to images.dat", doc.UndoLabel);
        Assert.True(doc.DatDirty(DatId.Images));
        state.Undo();
        Assert.Equal(vanilla, doc.Dat(DatId.Images)!.Save());
        Assert.False(doc.DatDirty(DatId.Images));
        state.Redo();
        Assert.Equal(1000, doc.Dat(DatId.Images)!.Count);
    }

    [Fact]
    public void SetEntryCountRoundsAndLabels()
    {
        var (doc, state) = Open();
        Assert.Null(state.SetEntryCount(DatId.Units, 251, 0));
        Assert.Equal(252, doc.Dat(DatId.Units)!.Count);
        Assert.Equal("Set units.dat to 252 entries", doc.UndoLabel);
        Assert.Contains("can't have fewer", state.SetEntryCount(DatId.Units, 100, 0));
        Assert.Equal("Set units.dat to 252 entries", doc.UndoLabel);   // nothing recorded
        Assert.Equal("weapons.dat has at most 255 entries", state.SetEntryCount(DatId.Weapons, 300, 0));
    }

    [Fact]
    public void ASameCountRecordsNothing()
    {
        var (doc, state) = Open();
        Assert.Null(state.SetEntryCount(DatId.Images, 999, 0));
        Assert.False(doc.CanUndo);
        Assert.Null(state.SetEntryCount(DatId.Units, 251, 0));
        Assert.Null(state.SetEntryCount(DatId.Units, 252, 0));
        Assert.Equal("Set units.dat to 252 entries", doc.UndoLabel);
        state.Undo();
        Assert.False(doc.CanUndo);   // only the one step
    }

    [Fact]
    public void AddEntryRefusesWhatCantGrow()
    {
        var (doc, state) = Open();
        Assert.False(state.AddEntry(DatId.Mapdata, 0));
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void EntryChangedSinceSave()
    {
        var (doc, state) = Open();
        Assert.True(state.EditDat(DatId.Units, 3, "armor", 2));
        Assert.True(doc.EntryChangedSinceSave(DatId.Units, 3));
        Assert.False(doc.EntryChangedSinceSave(DatId.Units, 4));
        Assert.True(state.AddEntry(DatId.Images, 0));
        Assert.True(doc.EntryChangedSinceSave(DatId.Images, 999));
        Assert.False(doc.EntryChangedSinceSave(DatId.Images, 998));
        Assert.False(doc.EntryChangedSinceSave(DatId.Images, 5000));   // past the current count
        doc.MarkSaved();
        Assert.False(doc.EntryChangedSinceSave(DatId.Units, 3));
        Assert.False(doc.EntryChangedSinceSave(DatId.Images, 999));
        Assert.True(state.EditDat(DatId.Units, 3, "armor", 5));
        state.Undo();
        Assert.False(doc.EntryChangedSinceSave(DatId.Units, 3));
    }

    [Fact]
    public void TheLayoutChangeAloneMarksNoOldEntry()
    {
        var (doc, state) = Open();
        Assert.True(state.AddEntry(DatId.Sprites, 0));
        for (int e = 0; e < 517; e++) Assert.False(doc.EntryChangedSinceSave(DatId.Sprites, e), $"sprite {e}");
        Assert.True(doc.EntryChangedSinceSave(DatId.Sprites, 517));
    }

    [Fact]
    public void UndoAGrowthPastTheSaveIsDirtyWithNoRowMarked()
    {
        var (doc, state) = Open();
        Assert.True(state.AddEntry(DatId.Images, 0));
        doc.MarkSaved();
        state.Undo();
        Assert.True(doc.DatDirty(DatId.Images));
        for (int e = 0; e < 999; e++) Assert.False(doc.EntryChangedSinceSave(DatId.Images, e));
        state.Redo();
        Assert.False(doc.DatDirty(DatId.Images));
    }

    [Fact]
    public void NewEntriesAreUsableEverywhere()
    {
        var (doc, state) = Open();
        Assert.Null(state.SetEntryCount(DatId.Images, 1001, 0));
        Assert.True(state.EditDat(DatId.Images, 1000, "iscript_id", 5));
        string text = state.CopyEntryText(DatId.Images, 0, null);
        Assert.Null(state.PasteEntry(DatId.Images, 1000, text));
        Assert.Null(state.ImportText(DatId.Images, text.Replace("Image(0):", "Image(1000):"), "x.txt"));
        var palette = Enumerable.Repeat(0xFF000000u, 256).ToArray();
        var preview = new DatPreview(_ => null, new PreviewPalettes(palette, palette, palette, palette, palette));
        Assert.Equal("image 1000 has no GRP file", preview.Image(doc.Dat(DatId.Images)!, null, 1000).Reason);
    }

    [Fact]
    public void GrownNames()
    {
        var units = Zeros(DatId.Units).WithCount(251);
        var images = Zeros(DatId.Images).WithCount(1000);
        var lists = DatLists.From(new Dictionary<string, string[]> { ["Units"] = [], ["Images"] = [] }, new Dictionary<string, string>());
        DatFile? Dat(DatId id) => id == DatId.Units ? units : id == DatId.Images ? images : null;
        var sources = new DatNameSources(Dat, null, null, null, null, null, null, lists);
        var unitNames = DatNames.Build(DatId.Units, sources, NamesMode.Basic, false);
        Assert.Equal(252, unitNames.Count);
        for (int id = 228; id <= 250; id++) Assert.Equal($"Reserved Unit #{id}", unitNames[id]);
        Assert.Equal("Expanded Unit #251", unitNames[251]);
        Assert.Equal("Expanded Image #999", DatNames.Build(DatId.Images, sources, NamesMode.Basic, false)[999]);
    }

    // -------- Final review: values the first growth made available --------

    [Fact]
    public void AnEditOnANewlyCoveredValueIsMarked()
    {
        // The saved file is vanilla's layout: sprite 5 has no health bar there, 0 after the growth.
        var (doc2, state2) = Open();
        Assert.True(state2.AddEntry(DatId.Sprites, 0));
        Assert.True(state2.EditDat(DatId.Sprites, 5, "health_bar", 7));
        Assert.True(doc2.EntryChangedSinceSave(DatId.Sprites, 5));
        Assert.True(state2.EditDat(DatId.Sprites, 5, "health_bar", 0));
        Assert.False(doc2.EntryChangedSinceSave(DatId.Sprites, 5));

        Assert.True(state2.AddEntry(DatId.Units, 0));
        Assert.True(state2.EditDat(DatId.Units, 0, "infestation", 3));
        Assert.True(doc2.EntryChangedSinceSave(DatId.Units, 0));
    }

    [Fact]
    public void RevertEntryPutsBackANewlyCoveredValue()
    {
        var (doc, state) = Open();
        Assert.True(state.AddEntry(DatId.Sprites, 0));
        Assert.True(state.EditDat(DatId.Sprites, 5, "health_bar", 7));
        Assert.True(state.RevertEntry(DatId.Sprites, 5, toVanilla: false));
        Assert.Equal(0UL, doc.Dat(DatId.Sprites)!.Get(5, "health_bar"));
    }
}
