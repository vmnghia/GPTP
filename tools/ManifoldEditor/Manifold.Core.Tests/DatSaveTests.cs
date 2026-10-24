using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public sealed class DatSaveTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("dat-save-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    static byte[] Vanilla(DatId id) => new byte[DatFormats.Of(id).VanillaSize];

    static byte[] Expanded(DatId id, int count) => new byte[count * DatFormats.Of(id).ExpandedEntrySize!.Value];

    static FakeArchive VanillaArchive() =>
        new("StarDat.mpq", DatFormats.All.ToDictionary(f => f.ArchivePath, f => Vanilla(f.Id)));

    static DatSet Load(FakeArchive exe, List<string>? sources = null, List<string>? notes = null)
    {
        var vanilla = VanillaArchive();
        return DatSources.Load(new ResourceResolver([exe, vanilla]), new ResourceResolver([vanilla]),
            sources ?? [], notes ?? []);
    }

    static (FakeArchive Exe, ButtonSetDocument Doc) Open(Dictionary<string, byte[]> exeFiles, List<string>? sources = null, List<string>? notes = null)
    {
        var exe = new FakeArchive("SCManifold.exe", exeFiles);
        var doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), dats: Load(exe, sources, notes));
        return (exe, doc);
    }

    [Fact]
    public void LoadPrefersTheExeAndNamesTheSource()
    {
        var weapons = Vanilla(DatId.Weapons);
        weapons[0] = 7;   // first byte of the first property
        var sources = new List<string>();
        var notes = new List<string>();
        var set = Load(new FakeArchive("SCManifold.exe", new() { ["arr\\weapons.dat"] = weapons }), sources, notes);
        Assert.Contains("arr\\weapons.dat: SCManifold.exe", sources);
        Assert.Contains("arr\\units.dat: StarDat.mpq", sources);
        Assert.Empty(notes);
        Assert.Equal(11, set.Opened.Count);
        Assert.False(set.Opened[DatId.Weapons].SameAs(set.Vanilla[DatId.Weapons]));
        Assert.True(set.Opened[DatId.Units].SameAs(set.Vanilla[DatId.Units]));
    }

    [Fact]
    public void FilesMissingEverywhereAreNotedAndAbsent()
    {
        var vanilla = new FakeArchive("StarDat.mpq");
        var notes = new List<string>();
        var set = DatSources.Load(new ResourceResolver([vanilla]), new ResourceResolver([vanilla]), [], notes);
        Assert.Empty(set.Opened);
        Assert.Empty(set.Vanilla);
        Assert.Contains("arr\\units.dat: not found; its tab is empty", notes);
    }

    [Fact]
    public void RefusedExeFileOpensVanillaAndStaysClean()
    {
        var notes = new List<string>();
        var (exe, doc) = Open(new() { ["arr\\units.dat"] = new byte[100] }, notes: notes);
        Assert.True(doc.Dat(DatId.Units)!.SameAs(doc.VanillaDat(DatId.Units)!));
        string reason = Assert.Throws<InvalidDataException>(() => DatFile.Parse(DatFormats.Of(DatId.Units), new byte[100])).Message;
        Assert.Contains($"arr\\units.dat: the exe's copy was refused ({reason}); vanilla's is shown", notes);
        Assert.False(doc.IsDirty);
        doc.Apply(1, _ => Make.Set(Make.Button(1)), "Edit");
        Assert.Null(EditorSession.Save(exe, doc, out _, out var dats));
        Assert.Empty(dats);
        Assert.Equal(100, exe.Files["arr\\units.dat"].Length);   // the exe's refused copy is left as it was
    }

    [Fact]
    public void ExtendedRefusedTableIsNoted()
    {
        var notes = new List<string>();
        var (_, doc) = Open(new() { ["arr\\units.dat"] = Expanded(DatId.Units, 252) }, notes: notes);
        Assert.Contains("arr\\units.dat is extended (252 entries): the plugin refuses it and the game will show its invalid-file warning", notes);
        Assert.Empty(EditorSession.RefusedExtended(doc));
        new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()).EditDat(DatId.Units, 0, "armor", 3);
        Assert.Equal([DatId.Units], EditorSession.RefusedExtended(doc));
    }

    [Fact]
    public void ExtendedFilesThePluginReadsAreNotRefused()
    {
        var notes = new List<string>();
        var (_, doc) = Open(new() { ["arr\\images.dat"] = Expanded(DatId.Images, 1000) }, notes: notes);
        Assert.DoesNotContain(notes, n => n.Contains("extended"));
        new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()).EditDat(DatId.Images, 0, "iscript_id", 1);
        Assert.Empty(EditorSession.RefusedExtended(doc));
    }

    [Fact]
    public void SaveWritesOnlyDirtyFiles()
    {
        var (exe, doc) = Open([]);
        new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()).EditDat(DatId.Weapons, 0, "damage_amount", 9);
        Assert.True(doc.DatDirty(DatId.Weapons));
        Assert.Null(EditorSession.Save(exe, doc, out var strings, out var dats));
        Assert.Null(strings);
        Assert.Equal([DatId.Weapons], dats.Select(d => d.File));
        Assert.Equal(doc.Dat(DatId.Weapons)!.Save(), dats[0].Bytes);
        Assert.Equal(["Manifold\\buttonsets.bin", "arr\\weapons.dat"], exe.Files.Keys.Order(StringComparer.Ordinal));
        Assert.False(doc.DatDirty(DatId.Weapons));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void ABusyExeKeepsTheFilesDirty()
    {
        var (exe, doc) = Open([]);
        new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()).EditDat(DatId.Weapons, 0, "damage_amount", 9);
        exe.Busy = true;
        Assert.NotNull(EditorSession.Save(exe, doc, out _, out var dats));
        Assert.Empty(dats);
        Assert.True(doc.DatDirty(DatId.Weapons));
    }

    [Fact]
    public void MirrorWriteCreatesArrFolder()
    {
        string pack = Path.Combine(root, "to-repack");
        DatMirror.Write(pack, DatId.Weapons, [1, 2, 3]);
        string path = Path.Combine(pack, "arr", "weapons.dat");
        Assert.Equal(path, DatMirror.PathIn(pack, DatId.Weapons));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        DatMirror.Write(pack, DatId.Weapons, [4]);
        Assert.Equal([4], File.ReadAllBytes(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path + ".bak"));
    }

    [Fact]
    public void DifferingListsChangedCopies()
    {
        var (_, doc) = Open([]);
        string pack = Path.Combine(root, "to-repack"), exePath = Path.Combine(root, "SCManifold.exe");
        File.WriteAllBytes(exePath, [0]);
        File.SetLastWriteTimeUtc(exePath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var notes = new List<string>();

        DatMirror.Write(pack, DatId.Weapons, doc.Dat(DatId.Weapons)!.Save());
        Assert.Empty(DatMirror.Differing(doc, pack, exePath, notes));

        var changed = doc.Dat(DatId.Weapons)!.With(0, "damage_amount", 5);
        DatMirror.Write(pack, DatId.Weapons, changed.Save());
        File.SetLastWriteTimeUtc(DatMirror.PathIn(pack, DatId.Weapons), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var diff = Assert.Single(DatMirror.Differing(doc, pack, exePath, notes));
        Assert.Equal(DatId.Weapons, diff.File);
        Assert.Equal(DatMirror.PathIn(pack, DatId.Weapons), diff.Path);
        Assert.True(diff.Newer);
        Assert.True(diff.Copy.SameAs(changed));

        File.SetLastWriteTimeUtc(diff.Path, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(Assert.Single(DatMirror.Differing(doc, pack, exePath, notes)).Newer);
        Assert.Empty(notes);
    }

    [Fact]
    public void AnUnreadableCopyIsNoted()
    {
        var (_, doc) = Open([]);
        string pack = Path.Combine(root, "to-repack"), exePath = Path.Combine(root, "SCManifold.exe");
        DatMirror.Write(pack, DatId.Units, new byte[100]);
        var notes = new List<string>();
        Assert.Empty(DatMirror.Differing(doc, pack, exePath, notes));
        string reason = Assert.Throws<InvalidDataException>(() => DatFile.Parse(DatFormats.Of(DatId.Units), new byte[100])).Message;
        Assert.Equal([$"to-repack\\arr\\units.dat: refused ({reason})"], notes);
    }

    [Fact]
    public void SaveAsCarriesDirtyDatFiles()
    {
        var (_, doc) = Open([]);
        new EditorState(doc, "test.exe", Fixtures.Conditions(), Fixtures.Actions()).EditDat(DatId.Weapons, 0, "damage_amount", 9);
        string source = Path.Combine(root, "SCManifold.exe"), target = Path.Combine(root, "Other.exe");
        File.WriteAllBytes(source, [1]);
        var host = new FakeRepackHost(new());
        Assert.Null(EditorSession.SaveAs(source, target, doc, host, out _));
        var add = Assert.Single(host.Adds);
        Assert.Contains(add.Files, f => f.Path == "arr\\weapons.dat");
        Assert.DoesNotContain(add.Files, f => f.Path == "arr\\units.dat");
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void ResourcesLoadTheTablesAndIscript()
    {
        var iscript = new byte[8];
        BitConverter.TryWriteBytes(iscript.AsSpan(0), (ushort)4);
        BitConverter.TryWriteBytes(iscript.AsSpan(4), (ushort)0xFFFF);
        var arch = new FakeArchive("StarDat.mpq", new()
        {
            ["arr\\images.tbl"] = Tbl.Build("Marine\0"),
            ["arr\\sfxdata.tbl"] = Tbl.Build("a\0"),
            ["arr\\portdata.tbl"] = Tbl.Build("b\0"),
            ["scripts\\iscript.bin"] = iscript,
        });
        var r = EditorResources.Load(new ResourceResolver([arch]), null);
        Assert.NotNull(r.ImagesTbl);
        Assert.NotNull(r.SfxdataTbl);
        Assert.NotNull(r.PortdataTbl);
        Assert.Null(r.MapdataTbl);
        Assert.Null(r.UnitNames);
        Assert.NotNull(r.IscriptIds);
        Assert.Contains("arr\\images.tbl: StarDat.mpq", r.Sources);
        Assert.Contains("arr\\mapdata.tbl: not found; map names show numbers", r.Notes);
        Assert.DoesNotContain(r.Notes, n => n.Contains("unitnames"));
        var empty = EditorResources.Load(new ResourceResolver([new FakeArchive("x")]), null);
        Assert.Contains("arr\\images.tbl: not found; image names show numbers", empty.Notes);
        Assert.Contains("scripts\\iscript.bin: not found; script ids show numbers", empty.Notes);
    }
}
