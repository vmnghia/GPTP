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

public static class CardLayout
{
    public const int Columns = 5;
    public const int Rows = 3;

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
