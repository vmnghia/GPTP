using System.Text;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class NameOverridesTests
{
    const string UnitsPath = "Manifold\\names\\units.txt";

    static NameOverrides Parse(string text, List<string>? notes = null) => NameOverrides.Parse(text, notes ?? [], UnitsPath);

    static DatFile Zeros(DatId id) => DatFile.Parse(DatFormats.Of(id), new byte[DatFormats.Of(id).VanillaSize]);

    static DatFile Expanded(DatId id, int entries) =>
        DatFile.Parse(DatFormats.Of(id), new byte[entries * DatFormats.Of(id).ExpandedEntrySize!.Value]);

    static DatSet AllZero() =>
        new(DatFormats.All.ToDictionary(f => f.Id, f => Zeros(f.Id)), DatFormats.All.ToDictionary(f => f.Id, f => Zeros(f.Id)));

    static ButtonSetDocument Document(DatSet? dats = null) => new(Make.EmptySets(), Make.EmptySets(), dats: dats ?? AllZero());

    static EditorState State(ButtonSetDocument doc) =>
        new(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions());

    static StringTable Table(params string[] segments) => StringTable.Parse(Tbl.Build(segments));

    static DatLists Lists(params (string Name, string[] Entries)[] lists) =>
        DatLists.From(lists.ToDictionary(l => l.Name, l => l.Entries), new Dictionary<string, string>());

    static DatNameSources Sources(NameOverrides? units, Func<DatId, DatFile?>? dat = null, StringTable? statTxt = null, DatLists? lists = null) =>
        new(dat ?? (_ => null), statTxt, null, null, null, null, null, lists ?? Lists(), id => id == DatId.Units ? units : null);

    [Fact]
    public void ParseAndWriteRoundTrip()
    {
        var notes = new List<string>();
        var overrides = Parse("12+:(test)\n5:Mature Chrysalis\n", notes);
        Assert.Empty(notes);
        Assert.Equal((false, "Mature Chrysalis"), overrides.Entries[5]);
        Assert.Equal((true, "(test)"), overrides.Entries[12]);
        Assert.Equal("5:Mature Chrysalis\n12+:(test)\n", overrides.Write());
        Assert.True(Parse(overrides.Write()).SameAs(overrides));
        Assert.Equal("", NameOverrides.Empty.Write());
    }

    [Fact]
    public void ParseTakesPyMsSpacingAndCrLf()
    {
        var overrides = Parse("  7 + : x\r\n 8:y\r\n");
        Assert.Equal((true, " x"), overrides.Entries[7]);
        Assert.Equal((false, "y"), overrides.Entries[8]);
    }

    [Fact]
    public void OverrideKeepsColonsAndSpaces()
    {
        var overrides = Parse("3: a:b ");
        Assert.Equal((false, " a:b "), overrides.Entries[3]);
        Assert.Equal("3: a:b \n", overrides.Write());
    }

    [Fact]
    public void EmptyNameRemovesTheOverride()
    {
        var overrides = Parse("1:a\n2+:b\n");
        var without = overrides.With(1, false, "");
        Assert.False(without.Entries.ContainsKey(1));
        Assert.True(without.Entries.ContainsKey(2));
        Assert.True(overrides.Entries.ContainsKey(1));   // immutable
        Assert.True(overrides.With(3, false, "c").Entries.ContainsKey(3));
        Assert.True(overrides.With(2, true, "b").SameAs(overrides));
        Assert.False(overrides.With(2, false, "b").SameAs(overrides));
        Assert.Equal("2+:b\n", Parse("1:a\n2+:b\n1:\n").Write());
    }

    [Fact]
    public void BadLinesAreNotedAndSkipped()
    {
        var notes = new List<string>();
        var overrides = Parse("1:a\nnot one\n123456:too long\n\n4+x:y\n9:ok\n", notes);
        Assert.Equal(["Manifold\\names\\units.txt line 2: not an override",
                      "Manifold\\names\\units.txt line 3: not an override",
                      "Manifold\\names\\units.txt line 5: not an override"], notes);
        Assert.Equal([1, 9], overrides.Entries.Keys);
    }

    [Fact]
    public void ArchivePathIsTheDatsNameWithTxt()
    {
        Assert.Equal("Manifold\\names\\units.txt", NameOverrides.ArchivePath(DatId.Units));
        Assert.Equal("Manifold\\names\\sfxdata.txt", NameOverrides.ArchivePath(DatId.Sfxdata));
    }

    [Theory]
    [InlineData(NamesMode.Basic, "Terran Marine")]
    [InlineData(NamesMode.Tbl, "Marine")]
    [InlineData(NamesMode.Combined, "Terran Marine (Marine)")]
    public void ReplaceAndAppendInEachMode(NamesMode mode, string plain)
    {
        var lists = Lists(("Units", ["Terran Marine"]));
        var table = Table("Marine\0");
        var replace = Parse("0:Rifleman\n");
        var append = Parse("0+:(test)\n1+:(no base)\n");
        Assert.Equal("Rifleman", DatNames.Build(DatId.Units, Sources(replace, statTxt: table, lists: lists), mode, true)[0]);
        var names = DatNames.Build(DatId.Units, Sources(append, statTxt: table, lists: lists), mode, true);
        Assert.Equal(plain + " (test)", names[0]);
        Assert.Equal("Unit #1 (no base)", names[1]);
        Assert.Equal(plain, DatNames.Build(DatId.Units, Sources(null, statTxt: table, lists: lists), mode, true)[0]);
        Assert.Equal(plain, DatNames.Build(DatId.Units, Sources(NameOverrides.Empty, statTxt: table, lists: lists), mode, true)[0]);
    }

    [Fact]
    public void OverridesApplyToTheOtherFilesToo()
    {
        var sources = new DatNameSources(_ => null, null, null, null, null, null, null, Lists(),
            id => id == DatId.Weapons ? Parse("0:Gauss\n1+:II\n") : null);
        var names = DatNames.Build(DatId.Weapons, sources, NamesMode.Basic, false);
        Assert.Equal("Gauss", names[0]);
        Assert.Equal("Weapon #1 II", names[1]);
        Assert.Equal("Weapon #2", names[2]);
    }

    [Fact]
    public void ReservedUnitsIgnoreOverrides()
    {
        var dat = Expanded(DatId.Units, 300);
        var overrides = Parse("228:Nope\n229+:Nope\n230:Nope\n251:Fine\n");
        var names = DatNames.Build(DatId.Units, Sources(overrides, dat: id => id == DatId.Units ? dat : null), NamesMode.Basic, false);
        Assert.Equal("Reserved Unit #228", names[228]);
        Assert.Equal("Reserved Unit #229", names[229]);
        Assert.Equal("Reserved Unit #250", names[250]);
        Assert.Equal("Fine", names[251]);
    }

    [Fact]
    public void OverridesEditIsOneUndoStep()
    {
        var doc = Document();
        var state = State(doc);
        Assert.False(doc.IsDirty);
        var after = NameOverrides.Empty.With(5, false, "Mature Chrysalis");
        Assert.True(state.EditOverrides(DatId.Units, after));
        Assert.True(doc.OverridesDirty(DatId.Units));
        Assert.False(doc.OverridesDirty(DatId.Weapons));
        Assert.True(doc.IsDirty);
        Assert.True(doc.Overrides(DatId.Units).SameAs(after));
        Assert.True(doc.Overrides(DatId.Weapons).IsEmpty);
        Assert.False(state.EditOverrides(DatId.Units, Parse("5:Mature Chrysalis\n")));   // nothing changes
        Assert.True(state.Undo());
        Assert.Equal(new DatPlace(DatId.Units, 0, null), state.LastDatPlace);
        Assert.True(doc.Overrides(DatId.Units).IsEmpty);
        Assert.False(doc.IsDirty);
        Assert.True(state.Redo());
        Assert.True(doc.OverridesDirty(DatId.Units));
        Assert.Equal(new DatPlace(DatId.Units, 0, null), state.LastDatPlace);
    }

    [Fact]
    public void TheStepIsPlacedAtTheEntryGiven()
    {
        var doc = Document();
        Assert.True(doc.ApplyOverrides(DatId.Weapons, NameOverrides.Empty.With(2, true, "x"), 2));
        Assert.Equal(new Place(null, null, new DatPlace(DatId.Weapons, 2, null)), doc.Undo());
        Assert.Equal("Edit name overrides", doc.Redo() is null ? "" : "Edit name overrides");
    }

    [Fact]
    public void OpenedOverridesAreCleanUntilEdited()
    {
        var dats = AllZero() with { Overrides = new Dictionary<DatId, NameOverrides> { [DatId.Units] = Parse("1:a\n") } };
        var doc = Document(dats);
        Assert.False(doc.IsDirty);
        Assert.Equal((false, "a"), doc.Overrides(DatId.Units).Entries[1]);
        Assert.True(State(doc).EditOverrides(DatId.Units, NameOverrides.Empty));
        Assert.True(doc.OverridesDirty(DatId.Units));
        doc.MarkSaved();
        Assert.False(doc.OverridesDirty(DatId.Units));
        Assert.False(doc.IsDirty);
        Assert.True(doc.Undo() is not null);
        Assert.True(doc.OverridesDirty(DatId.Units));   // the exe holds the empty ones now
    }

    [Fact]
    public void LoadOverridesReadsTheExeAndNotesBadLines()
    {
        var exe = new FakeArchive("SCManifold.exe", new()
        {
            [UnitsPath] = Encoding.UTF8.GetBytes("﻿0:Zéro\nbad\n"),
            ["Manifold\\names\\weapons.txt"] = Encoding.UTF8.GetBytes("1+:x\n"),
        });
        var notes = new List<string>();
        var loaded = DatSources.LoadOverrides(exe, notes);
        Assert.Equal(["Manifold\\names\\units.txt line 2: not an override"], notes);
        Assert.Equal((false, "Zéro"), loaded[DatId.Units].Entries[0]);
        Assert.Equal((true, "x"), loaded[DatId.Weapons].Entries[1]);
        Assert.False(loaded.ContainsKey(DatId.Flingy));   // a missing file is no overrides
    }

    [Fact]
    public void SaveWritesOnlyChangedOverrideFiles()
    {
        var exe = new FakeArchive("SCManifold.exe");
        var doc = Document();
        var state = State(doc);

        Assert.Null(EditorSession.Save(exe, doc));
        Assert.DoesNotContain(exe.Files.Keys, k => k.StartsWith("Manifold\\names\\"));

        Assert.True(state.EditOverrides(DatId.Units, NameOverrides.Empty.With(5, false, "Zéro")));
        Assert.Null(EditorSession.Save(exe, doc));
        Assert.Equal("5:Zéro\n", Encoding.UTF8.GetString(exe.Files[UnitsPath]));
        Assert.False(exe.Files[UnitsPath].AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.False(exe.Files.ContainsKey("Manifold\\names\\weapons.txt"));
        Assert.False(doc.IsDirty);

        // Saved: the next save writes none of them.
        exe.Files.Remove(UnitsPath);
        Assert.Null(EditorSession.Save(exe, doc));
        Assert.False(exe.Files.ContainsKey(UnitsPath));

        // Removing them all rewrites the file, empty.
        Assert.True(state.EditOverrides(DatId.Units, NameOverrides.Empty));
        Assert.Null(EditorSession.Save(exe, doc));
        Assert.Empty(exe.Files[UnitsPath]);
    }

    [Fact]
    public void SaveWritesNothingWhenBusy()
    {
        var exe = new FakeArchive("SCManifold.exe") { Busy = true };
        var doc = Document();
        Assert.True(State(doc).EditOverrides(DatId.Units, Parse("1:a\n")));
        Assert.NotNull(EditorSession.Save(exe, doc));
        Assert.True(doc.OverridesDirty(DatId.Units));
    }
}
