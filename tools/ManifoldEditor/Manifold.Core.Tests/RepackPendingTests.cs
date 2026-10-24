using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public sealed class RepackPendingTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("repack-pending-").FullName;
    readonly byte[] plugin = Linked(Make.Dll(), 1_761_300_000);
    readonly byte[] exe;
    string ExePath => Path.Combine(root, "SCManifold.exe");
    string PluginPath => Path.Combine(root, "GPTP.qdp");
    string Folder => Path.Combine(root, "to-repack");

    public RepackPendingTests()
    {
        exe = Make.FireGraftExe(plugin);
        File.WriteAllBytes(ExePath, exe);
        File.SetLastWriteTimeUtc(ExePath, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    /// <summary>A DLL with its PE header's link time (TimeDateStamp) set.</summary>
    static byte[] Linked(byte[] dll, uint time)
    {
        BitConverter.TryWriteBytes(dll.AsSpan(BitConverter.ToInt32(dll, 0x3C) + 8), time);
        return dll;
    }

    void WritePlugin(byte[] bytes, int minutes)
    {
        File.WriteAllBytes(PluginPath, bytes);
        File.SetLastWriteTimeUtc(PluginPath, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes));
    }

    RepackPending Pending(IArchive? archive = null) =>
        Repack.Pending(exe, ExePath, PluginPath, Folder, archive ?? new FakeArchive("SCManifold.exe"));

    [Fact]
    public void Same_plugin_and_no_folder_is_nothing_pending()
    {
        WritePlugin(plugin, 5);
        var p = Pending();
        Assert.Equal((false, false, 0), (p.PluginDiffers, p.PluginNewer, p.Files));
        Assert.Contains("the same as GPTP.qdp", p.PluginLine);
    }

    [Fact]
    public void A_plugin_linked_later_is_newer()
    {
        WritePlugin(Linked(Make.Dll(extra: 512), 1_761_300_600), 1);
        var p = Pending();
        Assert.True(p.PluginDiffers);
        Assert.True(p.PluginNewer);
    }

    [Fact]
    public void A_plugin_linked_earlier_is_not_newer()
    {
        WritePlugin(Linked(Make.Dll(extra: 512), 1_761_299_000), 1);
        var p = Pending();
        Assert.True(p.PluginDiffers);
        Assert.False(p.PluginNewer);
    }

    [Fact]
    public void A_build_then_a_save_of_the_exe_still_counts_newer()
    {
        // The daily loop: build the plugin, then save buttons (the exe's file time moves past the plugin's).
        WritePlugin(Linked(Make.Dll(extra: 512), 1_761_300_600), -30);
        Assert.True(Pending().PluginNewer);
    }

    [Fact]
    public void A_missing_plugin_is_not_pending_and_does_not_throw()
    {
        var p = Pending();
        Assert.False(p.PluginDiffers);
        Assert.Contains("not found", p.PluginLine);
    }

    [Fact]
    public void An_exe_without_a_plugin_header_has_no_plugin_pending()
    {
        WritePlugin(Make.Dll(extra: 512), 1);
        var p = Repack.Pending(Make.Mpq(), ExePath, PluginPath, Folder, new FakeArchive("SCManifold.exe"));
        Assert.False(p.PluginDiffers);
    }

    [Fact]
    public void Files_count_leaves_out_same_and_warned()
    {
        foreach (var (path, data) in new (string, byte[])[] { ("rez\\a.bin", [1]), ("rez\\same.bin", [2]), ("rez\\stat_txt.tbl", [3]) })
        {
            string full = Path.Combine([Folder, .. path.Split('\\')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data);
        }
        var archive = new FakeArchive("SCManifold.exe", new() { ["rez\\same.bin"] = [2], [StringTable.ArchivePath] = [9] });
        Assert.Equal(1, Pending(archive).Files);
    }
}
