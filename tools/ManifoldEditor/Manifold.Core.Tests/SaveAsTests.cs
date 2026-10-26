using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public sealed class SaveAsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("save-as-").FullName;
    readonly FakeRepackHost host = new(new());
    readonly ButtonSetDocument doc = new(Make.EmptySets(), Make.EmptySets());
    string Source => Path.Combine(root, "SCManifold.exe");
    string Target => Path.Combine(root, "SCManifold-test.exe");

    public SaveAsTests()
    {
        File.WriteAllBytes(Source, [1, 2, 3]);
        doc.Apply(5, _ => Make.Set(Make.Button(1)), "Edit");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Saves_into_a_new_exe_and_moves_the_saved_mark()
    {
        Assert.Null(EditorSession.SaveAs(Source, Target, doc, host, out var strings));
        Assert.Null(strings);
        var add = Assert.Single(host.Adds);
        Assert.Equal(Target + ".saving", add.File);
        Assert.Contains(add.Files, f => f.Path == ButtonSetFile.ArchivePath);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Target));   // the fake adds nothing to the bytes
        Assert.False(doc.IsDirty);
        Assert.False(File.Exists(Target + ".bak"));
        Assert.False(File.Exists(Target + ".saving"));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Source));
        Assert.False(File.Exists(Source + ".bak"));
    }

    [Fact]
    public void An_existing_target_is_kept_as_bak()
    {
        File.WriteAllBytes(Target, [9]);
        Assert.Null(EditorSession.SaveAs(Source, Target, doc, host, out _));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Target));
        Assert.Equal([9], File.ReadAllBytes(Target + ".bak"));
    }

    [Fact]
    public void An_empty_target_from_the_picker_counts_as_new()
    {
        File.WriteAllBytes(Target, []);
        Assert.Null(EditorSession.SaveAs(Source, Target, doc, host, out _));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Target));
        Assert.False(File.Exists(Target + ".bak"));
    }

    [Fact]
    public void A_failed_add_leaves_an_existing_target_as_it_was()
    {
        File.WriteAllBytes(Target, [9]);
        host.FailAdd = true;
        var error = EditorSession.SaveAs(Source, Target, doc, host, out var strings);
        Assert.StartsWith("Saving SCManifold-test.exe: ", error);
        Assert.Null(strings);
        Assert.Equal([9], File.ReadAllBytes(Target));
        Assert.False(File.Exists(Target + ".saving"));
        Assert.False(File.Exists(Target + ".bak"));
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void A_failed_add_removes_a_new_or_empty_target()
    {
        File.WriteAllBytes(Target, []);
        host.FailAdd = true;
        Assert.NotNull(EditorSession.SaveAs(Source, Target, doc, host, out _));
        Assert.False(File.Exists(Target));
        Assert.False(File.Exists(Target + ".saving"));
    }

    [Fact]
    public void A_busy_target_gives_the_busy_message()
    {
        File.WriteAllBytes(Target, [9]);
        host.OnAdd = _ => throw new UnauthorizedAccessException("denied");
        Assert.Equal("SCManifold-test.exe is in use by another program. Nothing was saved.",
            EditorSession.SaveAs(Source, Target, doc, host, out _));
        Assert.Equal([9], File.ReadAllBytes(Target));
    }

    [Fact]
    public void The_open_exe_and_its_bak_are_refused()
    {
        Assert.Equal("That is the open exe: use Save.", EditorSession.SaveAsProblem(Source, Source.ToUpperInvariant()));
        Assert.Equal("That is the open exe's backup.", EditorSession.SaveAsProblem(Source, Source + ".bak"));
        Assert.Null(EditorSession.SaveAsProblem(Source, Target));
        Assert.Equal("That is the open exe: use Save.", EditorSession.SaveAs(Source, Source, doc, host, out _));
        Assert.Empty(host.Adds);
    }
}
