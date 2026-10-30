using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public sealed class RepackSettingsTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("repack-settings-").FullName;
    string Exe => Path.Combine(root, "SC", "SCManifold.exe");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Defaults_are_beside_the_exe()
    {
        var settings = new EditorSettings();
        Assert.Equal(Path.Combine(root, "SC", "to-repack"), settings.RepackFolderFor(Exe));
        Assert.Equal(Path.Combine(root, "SC", "GPTP.qdp"), settings.PluginPathFor(Exe));
        Assert.True(settings.IsDefaultRepackFolder(Exe));
        Assert.True(settings.IsDefaultPluginPath(Exe));
    }

    [Fact]
    public void An_override_and_back_to_the_default()
    {
        string pack = Path.Combine(root, "pack"), plugin = Path.Combine(root, "Release", "GPTP.qdp");
        var settings = new EditorSettings().WithRepackFolder(Exe, pack).WithPluginPath(Exe, plugin);
        Assert.Equal(pack, settings.RepackFolderFor(Exe));
        Assert.Equal(plugin, settings.PluginPathFor(Exe));
        Assert.False(settings.IsDefaultRepackFolder(Exe));
        Assert.False(settings.IsDefaultPluginPath(Exe));
        Assert.Equal(Path.Combine(root, "SC", "to-repack"), new EditorSettings().RepackFolderFor(Path.Combine(root, "other", "..", "SC", "SCManifold.exe")));

        settings = settings.WithRepackFolder(Exe, null).WithPluginPath(Exe, null);
        Assert.Equal(Path.Combine(root, "SC", "to-repack"), settings.RepackFolderFor(Exe));
        Assert.True(settings.IsDefaultRepackFolder(Exe));
        Assert.True(settings.IsDefaultPluginPath(Exe));
    }

    [Fact]
    public void Overrides_survive_save_and_load()
    {
        string pack = Path.Combine(root, "pack"), file = Path.Combine(root, "settings.json");
        new EditorSettings().WithRepackFolder(Exe, pack).Save(file);
        var loaded = EditorSettings.Load(file);
        Assert.Equal(pack, loaded.RepackFolderFor(Exe));
        Assert.True(loaded.IsDefaultPluginPath(Exe));
    }

    [Fact]
    public void The_mirror_path_follows_the_folder()
    {
        string pack = Path.Combine(root, "pack");
        Assert.Equal(Path.Combine(pack, "rez", "stat_txt.tbl"), StringMirror.PathIn(pack));
        Assert.Equal(StringMirror.PathIn(new EditorSettings().RepackFolderFor(Exe)), StringMirror.DefaultPath(Exe));
    }
}
