using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>A button's two string fields: 0x10, shown when enabled (a hotkey string), and 0x12, shown when disabled.</summary>
public enum StringField { Enabled, Disabled }

/// <summary>One button field pointing at a string.</summary>
public readonly record struct ButtonRef(int SetId, int Index, StringField Field);

/// <summary>
/// What points at a string id, as far as the editor knows (spec §2): button fields in the
/// 250 sets, and unit names (id = unit id + 1). .dat labels and ids in the exe are not
/// tracked.
/// </summary>
public static class StringUses
{
    public const int UnitNameCount = 228;

    public static ushort IdOf(Button button, StringField field) =>
        field == StringField.Enabled ? button.EnabledString : button.DisabledString;

    public static Button WithId(Button button, StringField field, ushort id) =>
        field == StringField.Enabled ? button with { EnabledString = id } : button with { DisabledString = id };

    /// <summary>Every button field, grouped by the string id it points at (0 left out).</summary>
    public static ILookup<int, ButtonRef> All(IReadOnlyList<ButtonSet> sets) =>
        (from setId in Enumerable.Range(0, sets.Count)
         let buttons = sets[setId].Buttons
         from index in Enumerable.Range(0, buttons.Count)
         from field in new[] { StringField.Enabled, StringField.Disabled }
         let id = IdOf(buttons[index], field)
         where id != 0
         select (id, new ButtonRef(setId, index, field))).ToLookup(p => (int)p.id, p => p.Item2);

    public static IReadOnlyList<ButtonRef> Buttons(IReadOnlyList<ButtonSet> sets, int id) => All(sets)[id].ToArray();

    /// <summary>The unit whose name the string is, or null.</summary>
    public static int? UnitOf(int id) => id is >= 1 and <= UnitNameCount ? id - 1 : null;
}
