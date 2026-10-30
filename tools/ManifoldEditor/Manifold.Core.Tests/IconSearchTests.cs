using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class IconSearchTests
{
    static readonly string[] Names = ["Marine", "Ghost", "Vulture", "Goliath"];
    static readonly IReadOnlySet<int> None = new HashSet<int>();

    [Fact]
    public void By_number_exactly() => Assert.Equal([2], IconSearch.Filter(6, Names, " 2 ", false, None));

    [Fact]
    public void By_name_any_case() => Assert.Equal([0], IconSearch.Filter(6, Names, "mar", false, None));

    [Fact]
    public void Blank_lists_all_and_frames_past_the_names_only_by_number()
    {
        Assert.Equal([5], IconSearch.Filter(6, Names, "5", false, None));
        Assert.Equal([0, 1, 2, 3, 4, 5], IconSearch.Filter(6, Names, "  ", false, None));
    }

    [Fact]
    public void Unused_counts_unsaved_edits()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, icon: 1), Make.Button(2, icon: 3));
        sets[9] = Make.Set(Make.Button(1, icon: 3));
        Assert.Equal([0, 2, 4, 5], IconSearch.Filter(6, Names, "", true, IconSearch.Used(sets)));
        Assert.Equal([2], IconSearch.Filter(6, Names, "vul", true, IconSearch.Used(sets)));
    }
}
