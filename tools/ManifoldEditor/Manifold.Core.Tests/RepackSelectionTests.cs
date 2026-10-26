using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class RepackSelectionTests
{
    static RepackSelection Selection() => new(
    [
        new RepackFile("rez\\a.bin", [1], RepackState.New, null),
        new RepackFile("rez\\b.bin", [2], RepackState.Changed, null),
        new RepackFile("unit\\c.grp", [3], RepackState.Same, null),
        new RepackFile(StringTable.ArchivePath, [4], RepackState.Changed, "would replace the strings saved by the editor"),
    ]);

    static bool[] Ticks(RepackSelection s) => Enumerable.Range(0, s.Files.Count).Select(s.IsTicked).ToArray();

    [Fact]
    public void Starts_with_the_default_ticks()
    {
        var s = Selection();
        Assert.Equal([true, true, false, false], Ticks(s));
        Assert.Equal("2 of 4 ticked", s.CountText);
        Assert.Equal(["rez\\a.bin", "rez\\b.bin"], s.Ticked.Select(f => f.MpqPath));
        Assert.Equal([0, 1, 2, 3], s.Visible);
    }

    [Fact]
    public void Filter_hides_rows_and_keeps_their_ticks()
    {
        var s = Selection();
        s.Filter = " UNIT ";
        Assert.Equal([2], s.Visible);
        Assert.True(s.IsTicked(0));
        Assert.Equal("2 of 4 ticked", s.CountText);
    }

    [Fact]
    public void Select_all_skips_warned_and_hidden_rows()
    {
        var s = Selection();
        Assert.Null(s.SelectAllState);
        s.ClickSelectAll();
        Assert.Equal([true, true, true, false], Ticks(s));
        Assert.True(s.SelectAllState);

        s.SetTicked(3, true);
        s.Filter = "stat";
        Assert.False(s.CanSelectAll);
        Assert.Equal("4 of 4 ticked", s.CountText);

        s.Filter = "rez";
        s.ClickSelectAll();                 // all of a, b ticked: unticks them, not the warned row
        Assert.Equal([false, false, true, true], Ticks(s));
    }

    [Fact]
    public void Select_all_state_has_three_values()
    {
        var s = Selection();
        s.SetTicked(0, false);
        s.SetTicked(1, false);
        Assert.False(s.SelectAllState);
        s.SetTicked(2, true);
        Assert.Null(s.SelectAllState);
        s.ClickSelectAll();
        Assert.True(s.SelectAllState);
        s.ClickSelectAll();
        Assert.False(s.SelectAllState);
        Assert.Equal([false, false, false, false], Ticks(s));
    }
}
