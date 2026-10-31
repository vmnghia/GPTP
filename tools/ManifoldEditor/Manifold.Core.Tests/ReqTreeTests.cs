using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

/// <summary>FireGraft's lists and the Requirements page's tree (requirements UX spec §2).</summary>
public class ReqTreeTests
{
    static IReadOnlyDictionary<int, IReadOnlyList<string>> List(string name) =>
        FireGraftList.Parse(Fixtures.Text($"firegraft-lists/{name}.txt"));

    static IEnumerable<ReqTreeNode> Leaves(IEnumerable<ReqTreeNode> nodes) =>
        nodes.SelectMany(n => n.Id is null ? Leaves(n.Children) : [n]);

    static ReqTreeNode? GroupHolding(IEnumerable<ReqTreeNode> nodes, int id) =>
        nodes.Where(n => n.Id is null).Select(n => n.Children.Any(c => c.Id == id) ? n : GroupHolding(n.Children, id))
            .FirstOrDefault(g => g is not null);

    [Fact]
    public void MarineUnderGroundUnits()
    {
        var units = List("unitlist");
        Assert.Equal(["Terran", "Ground Units"], units[0]);
        Assert.Equal(["Terran", "Buildings", "Addons"], units[107]);
        Assert.Equal(["All Races"], units[101]);
    }

    [Fact]
    public void OrdersAreFlat()
    {
        var orders = List("orderlist");
        Assert.Equal(256, orders.Count);
        Assert.All(orders.Values, path => Assert.Empty(path));
    }

    [Fact]
    public void SetLinesIgnored() =>
        Assert.Equal(["A"], FireGraftList.Parse("0Set=X,Y\r\n0Entry=A,B\r\n")[0]);

    [Fact]
    public void LineEndingsAndBlanks()
    {
        var list = FireGraftList.Parse("1Entry=A,B \n\n2Entry=A,C\r\n");
        Assert.Equal([1, 2], list.Keys.Order());
        Assert.Equal(["A"], list[1]);
        Assert.Equal(["A"], list[2]);
    }

    [Fact]
    public void TreeShape()
    {
        var tree = ReqTree.Build(228, List("unitlist"), id => $"[{id}]", "");
        Assert.Equal(["Terran", "Zerg", "Protoss"], tree.Take(3).Select(n => n.Title));
        var ground = tree[0].Children[0];
        Assert.Equal("Ground Units", ground.Title);
        Assert.Equal([0, 1, 2, 3, 5, 7], ground.Children.Take(6).Select(c => c.Id!.Value));
        Assert.All(Leaves(tree), l => Assert.InRange(l.Id!.Value, 0, 227));
        Assert.Equal(228, Leaves(tree).Count());
        Assert.Equal(ReqTree.Unused, GroupHolding(tree, 91)!.Title);
    }

    [Fact]
    public void MissingIdsGoToUnused()
    {
        var tree = ReqTree.Build(3, FireGraftList.Parse("0Entry=Terran,Marine\r\n"), id => $"[{id}]", "");
        Assert.Equal(["Terran", ReqTree.Unused], tree.Select(n => n.Title));
        Assert.Equal([0], tree[0].Children.Select(c => c.Id!.Value));
        Assert.Equal([1, 2], tree[1].Children.Select(c => c.Id!.Value));
    }

    [Fact]
    public void FindKeepsMatchingLeaves()
    {
        var units = List("unitlist");
        string Title(int id) => id == 0 ? "[0] Marine" : id == 20 ? "[20] Jim Raynor (Marine)" : $"[{id}] Unit";
        var marine = ReqTree.Build(228, units, Title, "marine");
        Assert.Equal([0, 20], Leaves(marine).Select(l => l.Id!.Value).Order());
        Assert.Equal(["Terran"], marine.Select(n => n.Title));
        Assert.Empty(ReqTree.Build(228, units, Title, "Ground Units"));
        Assert.Equal([7], Leaves(ReqTree.Build(228, units, Title, "7")).Select(l => l.Id!.Value));
    }

    [Fact]
    public void FlatView()
    {
        var flat = ReqTree.Build(5, null, id => $"[{id}]", "");
        Assert.Equal([0, 1, 2, 3, 4], flat.Select(n => n.Id!.Value));
        Assert.All(flat, n => Assert.Empty(n.Children));
    }
}
