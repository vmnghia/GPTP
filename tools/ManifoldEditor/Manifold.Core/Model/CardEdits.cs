using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>
/// Edits of one set, as pure functions: each returns the new set and leaves its input
/// alone. Button order is kept: a move only changes positions.
/// </summary>
public static class CardEdits
{
    /// <summary>
    /// Drag a cell onto another: every button at <paramref name="from"/> goes to
    /// <paramref name="to"/>; if <paramref name="to"/> held buttons, they go to
    /// <paramref name="from"/> (a swap).
    /// </summary>
    public static ButtonSet MoveCell(ButtonSet set, ushort from, ushort to)
    {
        CheckPosition(from); CheckPosition(to);
        if (from == to) return set;
        return set with
        {
            Buttons = set.Buttons.Select(b =>
                b.Position == from ? b with { Position = to } :
                b.Position == to ? b with { Position = from } : b).ToArray()
        };
    }

    /// <summary>Ctrl+drag: the buttons at <paramref name="from"/> are copied to <paramref name="to"/>, after the others.</summary>
    public static ButtonSet CopyCell(ButtonSet set, ushort from, ushort to)
    {
        CheckPosition(from); CheckPosition(to);
        if (from == to) return set;
        var copies = set.Buttons.Where(b => b.Position == from).Select(b => b with { Position = to });
        return set with { Buttons = set.Buttons.Concat(copies).ToArray() };
    }

    /// <summary>
    /// Drag one button onto a slot. With <paramref name="join"/> (Alt) it simply goes there,
    /// sharing the slot with whatever is there. Otherwise a filled slot swaps: its buttons go
    /// where the dragged button was. Order in the set is kept, so the button keeps its index.
    /// </summary>
    public static ButtonSet MoveButton(ButtonSet set, int index, ushort to, bool join)
    {
        CheckIndex(set, index); CheckPosition(to);
        ushort from = set.Buttons[index].Position;
        if (from == to) return set;
        return set with
        {
            Buttons = set.Buttons.Select((b, i) =>
                i == index ? b with { Position = to } :
                !join && b.Position == to ? b with { Position = from } : b).ToArray()
        };
    }

    /// <summary>Paste: the button goes to <paramref name="to"/>, after the set's other buttons.</summary>
    public static ButtonSet Paste(ButtonSet set, Button button, ushort to)
    {
        CheckPosition(to);
        return set with { Buttons = set.Buttons.Append(button with { Position = to }).ToArray() };
    }

    public static ButtonSet Delete(ButtonSet set, int index)
    {
        CheckIndex(set, index);
        return set with { Buttons = set.Buttons.Where((_, i) => i != index).ToArray() };
    }

    /// <summary>A field edit: the button at <paramref name="index"/> becomes <paramref name="button"/>.</summary>
    public static ButtonSet Replace(ButtonSet set, int index, Button button)
    {
        CheckIndex(set, index);
        CheckPosition(button.Position);
        return set with { Buttons = set.Buttons.Select((b, i) => i == index ? button : b).ToArray() };
    }

    /// <summary>Paste set: the target takes the source's buttons and keeps its own connected unit.</summary>
    public static ButtonSet PasteSet(ButtonSet target, ButtonSet source) =>
        target with { Buttons = source.Buttons.ToArray() };

    static void CheckPosition(ushort position)
    {
        if (position < 1 || position > Card.MaxPosition)
            throw new ArgumentOutOfRangeException(nameof(position), position, "positions are 1-15");
    }

    static void CheckIndex(ButtonSet set, int index)
    {
        if (index < 0 || index >= set.Buttons.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
