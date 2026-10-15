using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

/// <summary>StormLib's part, faked: the MPQ's files are a dictionary; added files join it.</summary>
sealed class FakeRepackHost(Dictionary<string, byte[]> exeFiles) : IRepackHost
{
    public List<(string File, IReadOnlyList<(string Path, byte[] Data)> Files)> Adds { get; } = new();
    public bool FailAdd { get; set; }
    /// <summary>Replaces what the work copy reads back (the files after the add).</summary>
    public Func<Dictionary<string, byte[]>, Dictionary<string, byte[]>>? AfterAdd { get; set; }

    public void AddFiles(string file, IReadOnlyList<(string Path, byte[] Data)> files)
    {
        if (FailAdd) throw new IOException("StormLib: disk full");
        Adds.Add((file, files));
    }

    public IArchive Open(string file)
    {
        var files = new Dictionary<string, byte[]>(exeFiles);
        if (file.EndsWith(".repacking"))
        {
            foreach (var (_, added) in Adds)
                foreach (var (path, data) in added) files[path] = data;
            if (AfterAdd is not null) files = AfterAdd(files);
        }
        return new FakeArchive(file, files);
    }
}

public sealed class RepackRunTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("repack-run-").FullName;
    readonly string exePath;
    readonly byte[] exe = Make.FireGraftExe(Make.Dll(extra: 100));
    readonly FakeRepackHost host = new(new() { [ButtonSetFile.ArchivePath] = [1, 2, 3], [StringTable.ArchivePath] = [4, 5] });

    public RepackRunTests()
    {
        exePath = Path.Combine(root, "SCManifold.exe");
        File.WriteAllBytes(exePath, exe);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    void AssertUnchanged(string? result, string step)
    {
        Assert.NotNull(result);
        Assert.StartsWith(step, result);
        Assert.Equal(exe, File.ReadAllBytes(exePath));
        Assert.False(File.Exists(exePath + ".bak"));
        Assert.False(File.Exists(exePath + ".repacking"));
    }

    [Fact]
    public void Packs_the_plugin_and_keeps_a_bak()
    {
        var plugin = Make.Dll(extra: 3000, fill: 0xAB);
        Assert.Null(Repack.Run(exePath, [], plugin, host));
        var result = File.ReadAllBytes(exePath);
        Assert.Equal(plugin, FireGraftExe.Parse(result)!.Plugin(result));
        Assert.Equal(exe, File.ReadAllBytes(exePath + ".bak"));
        Assert.False(File.Exists(exePath + ".repacking"));
    }

    [Fact]
    public void Adds_the_files_to_the_copy_not_the_exe()
    {
        (string, byte[])[] files = [("rez\\statdata.bin", [7, 7])];
        Assert.Null(Repack.Run(exePath, files, null, host));
        var add = Assert.Single(host.Adds);
        Assert.Equal(exePath + ".repacking", add.File);
        Assert.Equal(files, add.Files);
        Assert.Equal(exe, File.ReadAllBytes(exePath + ".bak"));
    }

    [Fact]
    public void A_failing_add_leaves_the_exe_unchanged()
    {
        host.FailAdd = true;
        AssertUnchanged(Repack.Run(exePath, [("rez\\a.bin", [1])], null, host), "Adding the files:");
    }

    [Fact]
    public void A_check_that_fails_leaves_the_exe_unchanged()
    {
        host.AfterAdd = files => { files.Remove("rez\\a.bin"); return files; };
        AssertUnchanged(Repack.Run(exePath, [("rez\\a.bin", [1])], null, host), "Checking the result:");
    }

    [Fact]
    public void The_editors_files_must_read_back_the_same()
    {
        host.AfterAdd = files => { files[ButtonSetFile.ArchivePath] = [9]; return files; };
        AssertUnchanged(Repack.Run(exePath, [("rez\\a.bin", [1])], null, host), "Checking the result:");
    }

    [Fact]
    public void A_plugin_for_an_exe_without_the_header_is_refused()
    {
        byte[] plain = [.. PeImageTests.MinimalExe((_, _) => { }), .. Make.Mpq()];
        File.WriteAllBytes(exePath, plain);
        var result = Repack.Run(exePath, [], Make.Dll(), host);
        Assert.NotNull(result);
        Assert.StartsWith("Packing the plugin:", result);
        Assert.Equal(plain, File.ReadAllBytes(exePath));
        Assert.False(File.Exists(exePath + ".bak"));
        Assert.False(File.Exists(exePath + ".repacking"));
    }

    [Fact]
    public void A_leftover_work_copy_is_replaced()
    {
        File.WriteAllBytes(exePath + ".repacking", [0xDE, 0xAD]);
        var plugin = Make.Dll(extra: 50, fill: 0x11);
        Assert.Null(Repack.Run(exePath, [], plugin, host));
        var result = File.ReadAllBytes(exePath);
        Assert.Equal(plugin, FireGraftExe.Parse(result)!.Plugin(result));
        Assert.False(File.Exists(exePath + ".repacking"));
    }

    [Fact]
    public void Nothing_to_do_changes_nothing()
    {
        Assert.Null(Repack.Run(exePath, [], null, host));
        Assert.Equal(exe, File.ReadAllBytes(exePath));
        Assert.False(File.Exists(exePath + ".bak"));
        Assert.Empty(host.Adds);
    }
}
