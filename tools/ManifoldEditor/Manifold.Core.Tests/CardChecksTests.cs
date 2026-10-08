using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class CardChecksTests
{
    [Fact]
    public void Checks_find_hotkey_clashes_and_buttons_replays_hide()
    {
        var strings = new Dictionary<ushort, string> { [1] = "o\u0001Siege", [2] = "o\u0001Other", [3] = "t\u0001Tank" };
        var set = Make.Set(
            Make.Button(1, enabledString: 1),
            Make.Button(1, enabledString: 2),    // same position: an alternative, no clash
            Make.Button(12, enabledString: 2),   // O again at another position: a clash
            Make.Button(3, enabledString: 3));
        var clash = Assert.Single(CardChecks.Clashes(set, id => strings.GetValueOrDefault(id)));
        Assert.Equal('O', clash.Hotkey);
        Assert.Equal(new[] { 0, 1, 2 }, clash.ButtonIndexes);
        Assert.Equal(new[] { 2 }, CardChecks.HiddenInReplays(set));
    }
}
