using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// One cell of the 5x3 card: position p sits at row (p-1)/5, column (p-1)%5. It shows its
/// first button's icon, and a count badge when several buttons share the position.
/// </summary>
public sealed record CardCell(ushort Position, int Row, int Column, IReadOnlyList<int> ButtonIndexes,
    int? Icon, bool HiddenInReplays)
{
    public int Count => ButtonIndexes.Count;
}

/// <summary>Where one button's icon is drawn in a cell, as a square: left, top, size.</summary>
public readonly record struct StackIcon(int ButtonIndex, double Left, double Top, double Size);

public static class CardLayout
{
    public const int Columns = 5;
    public const int Rows = 3;
    /// <summary>A stack draws at most this many icons; the count badge tells the rest.</summary>
    public const int MaxStackIcons = 3;

    /// <summary>
    /// A cell's icons. One button fills the cell (less a margin). Several fan out from the
    /// top left, each a step down and right; the first, which the game shows when its
    /// condition passes, is in front, so <see cref="Hit"/> tries it first.
    /// </summary>
    public static IReadOnlyList<StackIcon> StackIcons(CardCell cell, double cellSize, double margin = 6)
    {
        if (cell.Count == 0) return [];
        double room = cellSize - 2 * margin;
        if (cell.Count == 1) return [new StackIcon(cell.ButtonIndexes[0], margin, margin, room)];
        int shown = Math.Min(cell.Count, MaxStackIcons);
        double size = room * 0.68;
        double step = (room - size) / (shown - 1);
        return Enumerable.Range(0, shown)
            .Select(i => new StackIcon(cell.ButtonIndexes[i], margin + i * step, margin + i * step, size)).ToArray();
    }

    /// <summary>The button whose icon is under a point in the cell (front first), or the cell's first button.</summary>
    public static int? Hit(CardCell cell, double cellSize, double x, double y)
    {
        foreach (var icon in StackIcons(cell, cellSize))
            if (x >= icon.Left && x < icon.Left + icon.Size && y >= icon.Top && y < icon.Top + icon.Size)
                return icon.ButtonIndex;
        return cell.Count > 0 ? cell.ButtonIndexes[0] : null;
    }

    public static IReadOnlyList<CardCell> Cells(ButtonSet set)
    {
        var cells = new CardCell[Card.MaxPosition];
        for (ushort position = 1; position <= Card.MaxPosition; position++)
        {
            var indexes = Enumerable.Range(0, set.Buttons.Count)
                .Where(i => set.Buttons[i].Position == position).ToArray();
            cells[position - 1] = new CardCell(position, (position - 1) / Columns, (position - 1) % Columns,
                indexes, indexes.Length > 0 ? set.Buttons[indexes[0]].Icon : null,
                position > Card.ReplayMaxPosition);
        }
        return cells;
    }
}
