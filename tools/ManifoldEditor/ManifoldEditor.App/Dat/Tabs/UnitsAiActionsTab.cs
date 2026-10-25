using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, AI Actions (PyDAT's AIActionsUnitsTab): the five AI orders, the right-click
/// action, the AI internal flags and the AI force values (derived from the unit's weapons,
/// hit points and shields). Jump buttons and the links in the force values are left out for now.
/// </summary>
sealed class UnitsAiActionsTab : DatTabBase
{
    /// <summary>The weapons id that means no weapon; the unit id that means no subunit.</summary>
    const int NoWeapon = 130, NoUnit = 228;

    /// <summary>PyDAT's unit-specific adjustments of the force value; everything else counts 1.</summary>
    static readonly Dictionary<int, double> Reductions = new()
    {
        [7] = 0.25, [41] = 0.25, [64] = 0.25,      // SCV, Drone, Probe
        [13] = 0, [73] = 0, [85] = 0,              // Spider Mine, Interceptor, Scarab
        [32] = 2, [43] = 2, [65] = 2,              // Firebat, Mutalisk, Zealot
        [47] = 1 / 16.0, [50] = 1 / 16.0,          // Scourge, Infested Terran
        [83] = 0.1,                                // Reaver
    };

    const string ForceNotes = "Unit Specific Adjustments:\n"
        + "  - SCV/Drone/Probe: 0.25\n  - Spider Mine/Interceptor/Scarab: 0\n  - Firebat/Mutalisk/Zealot: 2\n"
        + "  - Scourge/Infested Terran: 1/16\n  - Reaver: 0.1\n  - Everything Else: 1\n\n"
        + "Weapons Override:\n  - Carrier/Gantrithor: Uses Interceptor weapons\n"
        + "  - Reaver/Warbringer: Uses Scarab weapons\n  - Has a subunit: Uses subunit weapons";

    public UnitsAiActionsTab(DatContext context) : base(DatTabId.UnitsAiActions, context)
    {
        Panel.Children.Add(Group("AI Actions",
            Order("comp_ai_idle", "Computer Idle", "UnitAICompIdle"),
            Order("human_ai_idle", "Human Idle", "UnitAIHumanIdle"),
            Order("return_to_idle", "Return to Idle", "UnitAIReturn"),
            Order("attack_unit", "Attack Unit", "UnitAIAttackUnit"),
            Order("attack_move", "Attack Move", "UnitAIAttackMove"),
            ChoiceField(this, File, P("right_click_action"), "Right-Click Action", "UnitAIRightClick", "Rightclick", 300),
            FlagsField(this, File, P("ai_internal"), new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Ignore Strategic Suicide missions", 1UL << 0, "UnitAINoSuicide"),
                ("Don't become a guard", 1UL << 1, "UnitAINoGuard"),
            }, 2)));

        var force = DerivedText(this, e => ForceValue(e, air: false) + "\n\n" + ForceValue(e, air: true));
        force.FontFamily = new FontFamily("Consolas");
        force.Opacity = 1;
        force.TextWrapping = TextWrapping.Wrap;
        force.MaxWidth = 720;
        force.IsTextSelectionEnabled = true;
        ToolTipService.SetToolTip(force, ForceNotes);
        Panel.Children.Add(Group("AI Force Values", force));
    }

    FrameworkElement Order(string property, string label, string hintKey) =>
        ReferenceField(this, File, P(property), label, hintKey, () => Context.Names(DatId.Orders), "None", 300, jump: DatId.Orders);

    // -------- AI force values (PyDAT's build_force_value) --------

    ulong? Value(DatId file, int entry, string property) => ValueOf(Context, file, entry, property);

    /// <summary>
    /// The unit whose weapon counts (the Interceptor for a Carrier or Gantrithor, the Scarab for
    /// a Reaver or Warbringer, the subunit when there is one) or null for the unit's own, and
    /// the weapon's id there (130: none).
    /// </summary>
    (int? Override, int Weapon) ForceWeapon(int entry, bool air)
    {
        int unit = entry;
        if (unit is 72 or 82) unit = 73;
        else if (unit is 81 or 83) unit = 85;
        else if (Value(File, entry, "subunit1") is { } sub && sub != NoUnit) unit = (int)sub;
        var weapon = Value(File, unit, P(air ? "air_weapon" : "ground_weapon"));
        return (unit != entry ? unit : null, (int)(weapon ?? NoWeapon));
    }

    string ForceValue(int entry, bool air)
    {
        var (overrideUnit, weaponId) = ForceWeapon(entry, air);
        long range = 0, cooldown = 1, factor = 0, damage = 0;
        if (weaponId != NoWeapon)
        {
            range = (long)(Value(DatId.Weapons, weaponId, "maximum_range") ?? 0);
            cooldown = (long)(Value(DatId.Weapons, weaponId, "weapon_cooldown") ?? 1);
            factor = (long)(Value(DatId.Weapons, weaponId, "damage_factor") ?? 0);
            damage = (long)(Value(DatId.Weapons, weaponId, "damage_amount") ?? 0);
        }
        long hp = (long)(Value(File, entry, "hit_points") ?? 0) >> 8;
        long shields = Value(File, entry, "shield_enabled") is > 0 ? (long)(Value(File, entry, "shield_amount") ?? 0) : 0;
        double reduction = Reductions.GetValueOrDefault(entry, 1.0);

        string name = air ? "Air" : "Ground";
        string text = name + "\n";
        if (cooldown == 0)
            text += " = (the weapon's cooldown is 0, so there is no value)";
        else
        {
            double fd = (double)factor * damage;
            double inner = Math.Floor(Math.Floor((double)range / cooldown) * fd
                + Math.Floor(fd * 2048 / cooldown) * (hp + shields) / 256);
            long value = (long)(Math.Floor(Math.Sqrt(inner) * 7.58) * reduction);
            text += $" = {value}\n"
                + $" = floor(floor(sqrt(floor(floor({range} / {cooldown}) * {factor} * {damage}"
                + $" + (floor(({factor} * {damage} * 2048) / {cooldown}) * ({hp} + {shields})) / 256)) * 7.58) * {FloatText(reduction, 6)})";
        }
        if (overrideUnit is { } unit) text += $"\n\nUsing weapons from Unit: {unit}";
        return text;
    }
}
