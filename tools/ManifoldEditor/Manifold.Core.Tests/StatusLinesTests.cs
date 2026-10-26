using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class StatusLinesTests
{
    [Fact]
    public void Save_as_renames_the_lines_that_name_the_exe_as_a_source()
    {
        string[] lines =
        [
            "Button sets from Manifold\\buttonsets.bin",
            "rez\\stat_txt.tbl: SCManifold.exe",
            "unit\\cmdbtns\\cmdicons.grp: patch_rt.mpq",
            "arr\\units.dat: scmanifold.EXE",
            "Imported FireGraft's button sets for 18 sets from Firegraft\\SCManifold.fgp",
            "game\\tfontgam.pcx: SCManifold.exe.bak",
        ];
        Assert.Equal(
        [
            "Button sets from Manifold\\buttonsets.bin",
            "rez\\stat_txt.tbl: SCManifold-test.exe",
            "unit\\cmdbtns\\cmdicons.grp: patch_rt.mpq",
            "arr\\units.dat: SCManifold-test.exe",
            "Imported FireGraft's button sets for 18 sets from Firegraft\\SCManifold.fgp",
            "game\\tfontgam.pcx: SCManifold.exe.bak",
        ], StatusLines.Renamed(lines, "SCManifold.exe", "SCManifold-test.exe"));
    }
}
