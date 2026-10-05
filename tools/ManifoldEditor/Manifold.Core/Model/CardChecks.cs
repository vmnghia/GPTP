using Manifold.Core.Data;

namespace Manifold.Core.Model;

public sealed record HotkeyClash(char Hotkey, IReadOnlyList<int> ButtonIndexes);

/// <summary>The check box under the card (spec §5.4).</summary>
public static class CardChecks
{
    /// <summary>Each button's hotkey (the enabled string's first character), or null.</summary>
    public static char?[] Hotkeys(ButtonSet set, Func<ushort, string?> strings) =>
        set.Buttons.Select(b => StatTxt.HotkeyOf(strings(b.EnabledString))).ToArray();

    /// <summary>
    /// Hotkeys shared by buttons at different positions. Buttons sharing a position are
    /// alternatives (one is shown), so they don't clash with each other.
    /// </summary>
    public static IReadOnlyList<HotkeyClash> Clashes(ButtonSet set, Func<ushort, string?> strings)
    {
        var keys = Hotkeys(set, strings);
        return Enumerable.Range(0, set.Buttons.Count)
            .Where(i => keys[i] is not null)
            .GroupBy(i => keys[i]!.Value)
            .Where(g => g.Select(i => set.Buttons[i].Position).Distinct().Count() > 1)
            .Select(g => new HotkeyClash(g.Key, g.ToArray()))
            .ToArray();
    }

    /// <summary>Buttons a replay's 3x3 card will not show (positions above 9).</summary>
    public static IReadOnlyList<int> HiddenInReplays(ButtonSet set) =>
        Enumerable.Range(0, set.Buttons.Count)
            .Where(i => set.Buttons[i].Position > Card.ReplayMaxPosition).ToArray();
}
