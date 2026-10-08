using System.Text;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class StringSaveTests
{
    static (FakeArchive Exe, ButtonSetDocument Document) Opened()
    {
        var tbl = Tbl.Build("o\0Siege Mode\0", "t\0Tank Mode\0");
        var exe = new FakeArchive("SCManifold.exe", new() { [StringTable.ArchivePath] = tbl });
        var opened = EditorSession.Open(exe, "Firegraft\\SCManifold.fgp", Make.EmptySets(),
            Fixtures.Conditions(), Fixtures.Actions(), StringTable.Parse(tbl));
        return (exe, opened.Document);
    }

    [Fact]
    public void Changed_strings_are_saved_with_the_sets_in_one_write()
    {
        var (exe, document) = Opened();
        document.ApplyStrings(t => t.With(2, Tbl.Latin1("t\0Tank Mode!")), 2, "edit");
        Assert.Null(EditorSession.Save(exe, document, out var written));
        Assert.Equal(1, exe.Writes);
        Assert.Equal(written, exe.Files[StringTable.ArchivePath]);
        Assert.True(exe.Files.ContainsKey(ButtonSetFile.ArchivePath));
        Assert.False(document.IsDirty);
        Assert.Equal("t\0Tank Mode!\0", Encoding.Latin1.GetString(StringTable.Parse(written!).Segment(2)));
    }

    [Fact]
    public void Unchanged_strings_are_not_written()
    {
        var (exe, document) = Opened();
        var before = exe.Files[StringTable.ArchivePath];
        document.Apply(3, s => CardEdits.Paste(s, Make.Button(1), 1), "paste");
        Assert.Null(EditorSession.Save(exe, document, out var written));
        Assert.Null(written);
        Assert.Same(before, exe.Files[StringTable.ArchivePath]);
    }

    [Fact]
    public void A_busy_exe_or_a_table_that_doesnt_fit_saves_nothing()
    {
        var (exe, document) = Opened();
        document.ApplyStrings(t => t.With(1, Tbl.Latin1("o\0Siege")), 1, "edit");
        exe.Busy = true;
        Assert.NotNull(EditorSession.Save(exe, document, out var written));
        Assert.Null(written);
        Assert.True(document.StringsDirty);

        exe.Busy = false;
        byte[] Long() => Enumerable.Repeat((byte)'x', 33_000).ToArray();
        document.ApplyStrings(t => t.With(1, Long()).With(2, Long()).Add(Long()).Table, 3, "too much");
        var error = EditorSession.Save(exe, document, out written);
        Assert.Contains("rez\\stat_txt.tbl", error);
        Assert.Contains("Nothing was saved", error);
        Assert.Equal(0, exe.Writes);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void The_to_repack_copy_keeps_a_bak_and_compares_with_the_exe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "manifold-mirror-" + Guid.NewGuid().ToString("N"));
        try
        {
            string exePath = Path.Combine(dir, "SCManifold.exe");
            string path = StringMirror.DefaultPath(exePath);
            Assert.Equal(Path.Combine(dir, "to-repack", "rez", "stat_txt.tbl"), path);
            Assert.Equal(StringMirror.State.Missing, StringMirror.Compare([1], path));

            StringMirror.Write(path, [1, 2]);
            Assert.Equal(StringMirror.State.Same, StringMirror.Compare([1, 2], path));
            Assert.Equal(StringMirror.State.Differs, StringMirror.Compare([1, 3], path));
            Assert.Equal(StringMirror.State.Differs, StringMirror.Compare(null, path));
            Assert.False(File.Exists(path + ".bak"));

            StringMirror.Write(path, [3]);
            Assert.Equal([3], File.ReadAllBytes(path));
            Assert.Equal([1, 2], File.ReadAllBytes(path + ".bak"));
            Assert.False(File.Exists(path + ".saving"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_copy_is_remembered_per_exe()
    {
        string file = Path.Combine(Path.GetTempPath(), "manifold-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new EditorSettings();
            Assert.Null(settings.MirrorFor(@"D:\SC\SCManifold.exe"));
            settings = settings.WithMirror(@"D:\SC\SCManifold.exe", @"D:\SC\to-repack\rez\stat_txt.tbl")
                .WithMirror(@"D:\SC\SCManifold - Copy.exe", "");
            settings.Save(file);
            var loaded = EditorSettings.Load(file);
            Assert.Equal(@"D:\SC\to-repack\rez\stat_txt.tbl", loaded.MirrorFor(@"D:\SC\SCManifold.exe"));
            Assert.Equal(@"D:\SC\to-repack\rez\stat_txt.tbl", loaded.MirrorFor(@"D:\SC\scmanifold.EXE"));
            Assert.Equal("", loaded.MirrorFor(@"D:\SC\SCManifold - Copy.exe"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Choosing_the_to_repack_copy_makes_it_the_unsaved_strings()
    {
        var (exe, document) = Opened();
        var repack = StringTable.Parse(Tbl.Build("o\0Siege Mode\0", "t\0Tank Mode (PyTBL)\0"));
        document.UseOpenedStrings(repack);
        Assert.True(document.StringsDirty);
        Assert.Same(repack, document.OpenedStrings);
        Assert.Null(EditorSession.Save(exe, document, out var written));
        Assert.Equal(repack.Write(), written);
        document.ApplyStrings(t => t.With(1, [1]), 1, "edit");
        Assert.Throws<InvalidOperationException>(() => document.UseOpenedStrings(repack));
    }
}
