using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class FunctionSearchTests
{
    [Fact]
    public void Empty_or_blank_lists_all()
    {
        var table = Fixtures.Actions();
        Assert.Equal(table.Functions, FunctionSearch.Filter(table, ""));
        Assert.Equal(table.Functions, FunctionSearch.Filter(table, "   "));
    }

    [Fact]
    public void By_name_any_case() =>
        Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), "RALLY").Select(f => f.Name));

    [Fact]
    public void By_address_with_or_without_0x()
    {
        Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), "4244a0").Select(f => f.Name));
        Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), " 0x4244A0 ").Select(f => f.Name));
    }

    [Fact]
    public void Keeps_the_table_order() =>
        Assert.Equal(["Rally Point", "Rally Point - While Upgrading Only"],
            FunctionSearch.Filter(Fixtures.Conditions(), "rally point").Select(f => f.Name));

    [Fact]
    public void No_match_is_empty() => Assert.Empty(FunctionSearch.Filter(Fixtures.Actions(), "zzz"));
}
