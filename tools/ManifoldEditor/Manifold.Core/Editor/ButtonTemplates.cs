using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>A button Add new button can start from (its slot 0), its name, and how many of the exe's sets use it.</summary>
public sealed record ButtonTemplate(Button Button, string Name, int Uses);

/// <summary>Add new button's list (QoL spec §3): Blank, then every distinct button of the exe and vanilla.</summary>
public static class ButtonTemplates
{
    /// <summary>Icon 0, condition Always, action Move, no vars, no strings.</summary>
    public static readonly Button Blank = new(0, 0, 0x4282D0, 0x424440, 0, 0, 0, 0);

    /// <summary>
    /// Blank first; then the buttons of <paramref name="sets"/> and <paramref name="vanilla"/>,
    /// two being one when all but the slot match, sorted by name (then action, icon). Uses
    /// counts the sets of <paramref name="sets"/> holding the button.
    /// </summary>
    public static IReadOnlyList<ButtonTemplate> Build(IReadOnlyList<ButtonSet> sets, IReadOnlyList<ButtonSet> vanilla, Func<ushort, string?> text)
    {
        var uses = new Dictionary<Button, int>();
        foreach (var set in sets)
            foreach (var button in set.Buttons.Select(b => b with { Position = 0 }).Distinct())
                uses[button] = uses.GetValueOrDefault(button) + 1;
        foreach (var button in vanilla.SelectMany(s => s.Buttons).Select(b => b with { Position = 0 }))
            uses.TryAdd(button, 0);
        uses.Remove(Blank);
        var rows = uses.Select(u => new ButtonTemplate(u.Key, NameOf(u.Key, text), u.Value))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Button.Action).ThenBy(t => t.Button.Icon);
        return [new ButtonTemplate(Blank, "Blank", 0), .. rows];
    }

    static string NameOf(Button button, Func<ushort, string?> text) =>
        ButtonText.Display(text(button.EnabledString)) switch { "" => $"(no tooltip) icon {button.Icon}", var name => name };

    /// <summary>
    /// Blank, then the rows whose name, action or condition name, or icon name holds the
    /// query (case-insensitive), or whose icon number it is. A blank query keeps all.
    /// </summary>
    public static IReadOnlyList<ButtonTemplate> Filter(IReadOnlyList<ButtonTemplate> all, string query,
        FunctionTable conditions, FunctionTable actions, IReadOnlyList<string> iconNames)
    {
        string q = query.Trim();
        if (q.Length == 0) return all;
        bool number = q.All(char.IsAsciiDigit);
        bool Has(string? s) => s is not null && s.Contains(q, StringComparison.OrdinalIgnoreCase);
        bool Matches(ButtonTemplate t) =>
            Has(t.Name) || Has(actions.ByAddress(t.Button.Action)?.Name) || Has(conditions.ByAddress(t.Button.Condition)?.Name)
            || t.Button.Icon < iconNames.Count && Has(iconNames[t.Button.Icon])
            || number && int.TryParse(q, out int n) && n == t.Button.Icon;
        return [.. all.Where(t => t.Button == Blank), .. all.Where(t => t.Button != Blank && Matches(t))];
    }
}
