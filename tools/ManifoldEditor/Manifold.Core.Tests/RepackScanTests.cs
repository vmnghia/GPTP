using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public sealed class RepackScanTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("repack-scan-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    string Put(string relative, byte[] data)
    {
        string path = Path.Combine([root, .. relative.Split('\\')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void Takes_subfolders_only_and_skips_bak()
    {
        Put("make_x.py", [1]);
        Put("rez\\statdata.bin", [2]);
        Put("rez\\stat_txt.tbl.bak", [3]);
        Put("unit\\terran\\a.grp", [4]);
        var files = Repack.Scan(root, new FakeArchive("exe"));
        Assert.Equal(["rez\\statdata.bin", "unit\\terran\\a.grp"], files.Select(f => f.MpqPath));
        Assert.Equal([2], files[0].Data);
    }

    [Fact]
    public void States_against_the_exe()
    {
        var exe = new FakeArchive("exe", new() { ["rez\\a.bin"] = [1], ["rez\\b.bin"] = [2] });
        Put("rez\\a.bin", [1]);
        Put("rez\\b.bin", [3]);
        Put("rez\\c.bin", [4]);
        var files = Repack.Scan(root, exe);
        Assert.Equal([RepackState.Same, RepackState.Changed, RepackState.New], files.Select(f => f.State));
        Assert.Equal([false, true, true], files.Select(f => f.TickedByDefault));
    }

    [Fact]
    public void Protected_files_that_differ_are_unticked_with_their_warning()
    {
        var exe = new FakeArchive("exe", new() { [StringTable.ArchivePath] = [1], [ButtonSetFile.ArchivePath] = [2] });
        Put(StringTable.ArchivePath, [9]);
        Put(ButtonSetFile.ArchivePath, [8]);
        var files = Repack.Scan(root, exe).ToDictionary(f => f.MpqPath);
        Assert.Equal("would replace the strings saved by the editor", files[StringTable.ArchivePath].Warning);
        Assert.Equal("would replace the editor's button sets", files[ButtonSetFile.ArchivePath].Warning);
        Assert.All(files.Values, f => Assert.False(f.TickedByDefault));

        Put(StringTable.ArchivePath, [1]);
        Assert.Null(Repack.Scan(root, exe).Single(f => f.MpqPath == StringTable.ArchivePath).Warning);
    }

    [Fact]
    public void A_missing_folder_is_an_empty_list() =>
        Assert.Empty(Repack.Scan(Path.Combine(root, "nowhere"), new FakeArchive("exe")));

    [Fact]
    public void LoadPlugin_reports_a_missing_file_and_a_non_dll()
    {
        var missing = Repack.LoadPlugin(Path.Combine(root, "GPTP.qdp"));
        Assert.Null(missing.Bytes);
        Assert.Contains("not found", missing.Problem);

        var exeNotDll = Make.Dll();
        BitConverter.TryWriteBytes(exeNotDll.AsSpan(BitConverter.ToInt32(exeNotDll, 0x3C) + 22), (ushort)0x0102);
        var notDll = Repack.LoadPlugin(Put("GPTP.qdp", exeNotDll));
        Assert.Null(notDll.Bytes);
        Assert.Contains("DLL", notDll.Problem);

        var dll = Make.Dll(extra: 10);
        var good = Repack.LoadPlugin(Put("good.qdp", dll));
        Assert.Equal(dll, good.Bytes);
        Assert.Null(good.Problem);
    }

    [Fact]
    public void PluginStatus_says_same_differs_missing_or_no_header()
    {
        var plugin = Make.Dll(extra: 100);
        var exe = Make.FireGraftExe(plugin);
        string exePath = Put("SCManifold.exe", exe);
        string pluginPath = Path.Combine(root, "GPTP.qdp");

        Assert.Contains("not found", Repack.PluginStatus(exe, exePath, pluginPath));
        File.WriteAllBytes(pluginPath, plugin);
        Assert.Contains("the same as", Repack.PluginStatus(exe, exePath, pluginPath));
        File.WriteAllBytes(pluginPath, Make.Dll(extra: 200));
        Assert.Contains("differs", Repack.PluginStatus(exe, exePath, pluginPath));

        byte[] plain = [.. PeImageTests.MinimalExe((_, _) => { }), .. Make.Mpq()];
        Assert.Contains("No FireGraft plugin header", Repack.PluginStatus(plain, exePath, pluginPath));
        Assert.Contains("not understood", Repack.PluginStatus(Make.FireGraftExe(plugin, count: 2), exePath, pluginPath));
    }
}
