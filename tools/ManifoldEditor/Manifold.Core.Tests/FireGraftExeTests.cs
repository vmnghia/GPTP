using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class FireGraftExeTests
{
    static readonly byte[] MpqSignature = "MPQ\x1A"u8.ToArray();

    [Fact]
    public void Parses_the_users_layout()
    {
        var plugin = Make.Dll(extra: 1000);
        var exe = Make.FireGraftExe(plugin);
        var layout = FireGraftExe.Parse(exe)!;
        Assert.Equal(0x2400, layout.HeaderOffset);
        Assert.Equal(0x1B4D69B6u, layout.PluginId);
        Assert.Equal(0x2400 + 24, layout.PluginOffset);
        Assert.Equal(plugin.Length, layout.PluginSize);
        Assert.Equal(plugin, layout.Plugin(exe));
        Assert.Equal(0, layout.MpqOffset % 512);
        Assert.Equal(MpqSignature, exe[layout.MpqOffset..(layout.MpqOffset + 4)]);
    }

    [Fact]
    public void A_plain_exe_has_no_header() =>
        Assert.Null(FireGraftExe.Parse([.. PeImageTests.MinimalExe((_, _) => { }), .. Make.Mpq()]));

    [Fact]
    public void Something_that_is_not_an_exe_has_no_header() =>
        Assert.Null(FireGraftExe.Parse(Make.Mpq()));

    [Theory]
    [InlineData("count", 2u, 0u)]
    [InlineData("reserved", 1u, 1u)]
    public void Refuses_other_headers(string what, uint count, uint reserved)
    {
        var e = Assert.Throws<InvalidDataException>(() =>
            FireGraftExe.Parse(Make.FireGraftExe(Make.Dll(), count: count, reserved: reserved)));
        Assert.Contains(what, e.Message);
    }

    [Fact]
    public void Refuses_non_zero_padding()
    {
        var plugin = Make.Dll(extra: 1001);
        var exe = Make.FireGraftExe(plugin);
        int end = 0x2400 + 24 + plugin.Length;
        Assert.NotEqual(0, end % 512);
        exe[end] = 1;
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.Parse(exe));
        Assert.Contains("non-zero", e.Message);
    }

    [Fact]
    public void Refuses_an_MPQ_elsewhere()
    {
        var exe = Make.FireGraftExe(Make.Dll(extra: 1001));
        int mpq = FireGraftExe.Parse(exe)!.MpqOffset;
        byte[] moved = [.. exe[..mpq], .. new byte[512], .. exe[mpq..]];
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.Parse(moved));
        Assert.Contains("MPQ", e.Message);
    }

    [Fact]
    public void Refuses_a_user_data_header()
    {
        var exe = Make.FireGraftExe(Make.Dll());
        int mpq = FireGraftExe.Parse(exe)!.MpqOffset;
        exe[mpq + 3] = 0x1B;
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.Parse(exe));
        Assert.Contains("user-data", e.Message);
    }

    [Theory, InlineData(5000), InlineData(0)]
    public void Packs_a_larger_and_a_smaller_plugin(int extra)
    {
        var exe = Make.FireGraftExe(Make.Dll(extra: 2000));
        var old = FireGraftExe.Parse(exe)!;
        var plugin = Make.Dll(extra: extra, fill: 0xAB);
        var result = FireGraftExe.WithPlugin(exe, plugin);
        var layout = FireGraftExe.Parse(result)!;
        Assert.Equal(plugin, layout.Plugin(result));
        Assert.Equal(exe[..old.HeaderOffset], result[..layout.HeaderOffset]);
        Assert.Equal(exe[old.MpqOffset..], result[layout.MpqOffset..]);
        Assert.Equal(old.PluginId, layout.PluginId);
        Assert.Equal(0, layout.MpqOffset % 512);
    }

    [Fact]
    public void The_same_plugin_gives_the_same_file()
    {
        var exe = Make.FireGraftExe(Make.Dll(extra: 777));
        Assert.Equal(exe, FireGraftExe.WithPlugin(exe, FireGraftExe.Parse(exe)!.Plugin(exe)));
    }

    [Fact]
    public void Refuses_a_plugin_that_is_not_a_dll()
    {
        var plugin = Make.Dll();
        BitConverter.TryWriteBytes(plugin.AsSpan(BitConverter.ToInt32(plugin, 0x3C) + 22), (ushort)0x0102);
        Assert.Contains("DLL", FireGraftExe.DllProblem(plugin));
        Assert.Null(FireGraftExe.DllProblem(Make.Dll()));
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.WithPlugin(Make.FireGraftExe(Make.Dll()), plugin));
        Assert.Contains("DLL", e.Message);
    }

    [Fact]
    public void Refuses_a_truncated_plugin()
    {
        var cut = Make.Dll()[..0x300];   // a build still being copied: its sections claim 0x2400 bytes
        Assert.Contains("cut short", FireGraftExe.DllProblem(cut));
    }

    [Fact]
    public void Refuses_a_plugin_with_an_MPQ_signature_at_a_boundary()
    {
        var exe = Make.FireGraftExe(Make.Dll());
        var plugin = Make.Dll(extra: 2000);
        int pluginOffset = FireGraftExe.Parse(exe)!.PluginOffset;
        int at = 0x2400 + (512 - (pluginOffset + 0x2400) % 512) % 512;   // in the extra bytes, at a 512-byte file offset
        MpqSignature.CopyTo(plugin, at);
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.WithPlugin(exe, plugin));
        Assert.Contains("MPQ", e.Message);
    }

    [Fact]
    public void WithPlugin_refuses_an_exe_without_the_header()
    {
        byte[] plain = [.. PeImageTests.MinimalExe((_, _) => { }), .. Make.Mpq()];
        var e = Assert.Throws<InvalidDataException>(() => FireGraftExe.WithPlugin(plain, Make.Dll()));
        Assert.Contains("no FireGraft plugin header", e.Message);
    }
}
