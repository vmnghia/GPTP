using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class SessionTests
{
    const string FgpPath = "Firegraft\\SCManifold.fgp";

    static FakeArchive ModExe(bool withFgp = true) =>
        new("SCManifold.exe", withFgp ? new() { [FgpPath] = Fixtures.Bytes("SCManifold.fgp") } : null);

    static EditorSession.Opened Open(FakeArchive exe) =>
        EditorSession.Open(exe, FgpPath, Make.EmptySets(), Fixtures.Conditions(), Fixtures.Actions());

    [Fact]
    public void First_open_is_vanilla_plus_FireGrafts_sets()
    {
        var opened = Open(ModExe());
        Assert.Contains($"Imported 18 sets from {FgpPath}", opened.Status);
        Assert.Equal(18, Enumerable.Range(0, Card.SetCount).Count(opened.Document.DiffersFromVanilla));
        Assert.False(opened.Document.IsDirty);
    }

    [Fact]
    public void After_a_save_the_file_is_the_source()
    {
        var exe = ModExe();
        var document = Open(exe).Document;
        document.Apply(11, s => CardEdits.MoveCell(s, 1, 12), "move");
        Assert.Null(EditorSession.Save(exe, document));
        Assert.False(document.IsDirty);
        Assert.Equal(1, exe.Writes);

        exe.Files.Remove(FgpPath);
        var reopened = Open(exe);
        Assert.Contains($"Button sets from {ButtonSetFile.ArchivePath}", reopened.Status);
        Assert.Equal(12, reopened.Document[11].Buttons[0].Position);
    }

    [Fact]
    public void A_busy_exe_keeps_the_edits_unsaved()
    {
        var exe = ModExe();
        var document = Open(exe).Document;
        document.Apply(11, s => CardEdits.Delete(s, 0), "delete");
        exe.Busy = true;
        Assert.Equal("the exe is open in another program (FireGraft?)", EditorSession.Save(exe, document));
        Assert.True(document.IsDirty);
        Assert.False(exe.Files.ContainsKey(ButtonSetFile.ArchivePath));
    }

    [Fact]
    public void A_bad_file_is_reported_and_falls_back()
    {
        var exe = ModExe();
        exe.Files[ButtonSetFile.ArchivePath] = "MBTS"u8.ToArray();
        var opened = Open(exe);
        Assert.Contains(opened.Status, s => s.StartsWith(ButtonSetFile.ArchivePath + ": not a button set file"));
        Assert.Contains($"Imported 18 sets from {FgpPath}", opened.Status);
    }

    [Fact]
    public void No_project_means_vanilla()
    {
        var opened = Open(ModExe(withFgp: false));
        Assert.Contains($"No {FgpPath}: vanilla sets", opened.Status);
        Assert.Equal(0, Enumerable.Range(0, Card.SetCount).Count(opened.Document.DiffersFromVanilla));
    }
}
