using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml.Controls;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// orders.dat (PyDAT's OrdersTab): the weapon, technology and order the order uses, its label, iscript
/// animation, highlight icon (a cmdicons frame, 65535 none) and requirements, and the twelve flags (PyDAT's
/// "Unused" ones included). The Jump buttons, the icon preview and picker and the "Used by" list are left
/// out for now. The weapon is the format's <c>targeting</c> (PyDAT's weapon_targeting).
/// </summary>
sealed class OrdersTab : DatTabBase
{
    readonly TblChoices statTxt;

    public OrdersTab(DatContext context) : base(DatTabId.Orders, context)
    {
        statTxt = new TblChoices(() => Context.Opened()?.State.Strings);

        Panel.Children.Add(Group("Order Properties",
            ReferenceField(this, File, P("targeting"), "Targeting", "OrdTargeting", () => Context.Names(DatId.Weapons), "None", 300),
            ReferenceField(this, File, P("technology_energy"), "Energy", "OrdEnergy", () => Context.Names(DatId.Techdata), "None", 300),
            ReferenceField(this, File, P("obscured_order"), "Obscured", "OrdObscured", () => Context.Names(DatId.Orders), "None", 300),
            ReferenceField(this, File, P("label"), "Label", "OrdLabel", statTxt.WithNone, null, 300),
            ChoiceField(this, File, P("iscript_animation"), "Animation", "OrdAnimation", "Animations", 300),
            ReferenceField(this, File, P("highlight_icon"), "Highlight", "OrdHighlight", () => Context.Lists["Icons"], "None", 300, noneValue: 65535),
            NumberField(this, File, P("requirements"), "ReqIndex", "OrdReq", 64)));

        Panel.Children.Add(Group("Flags",
            Row(
                Flags(
                    ("use_weapon_targeting", "Use Weapon Targeting", "OrdWeapTarg"),
                    ("unused_is_secondary", "Secondary Order (Unused)", "OrdSecondaryOrder"),
                    ("unused_allow_non_subunits", "Allow Non-Subunits (Unused)", "OrdAllowNonSubunits"),
                    ("changes_subunit_order", "Changes Subunit Order", "OrdChangesSubunitOrder")),
                Flags(
                    ("unused_allow_subunits", "Allow Subunits (Unused)", "OrdAllowSubunits"),
                    ("interruptable", "Can be Interrupted", "OrdInterrupt"),
                    ("waypoints_slowdown", "Waypoints Slowdown", "OrdWaypointsSlowdown"),
                    ("queueable", "Can be Queued", "OrdQueue")),
                Flags(
                    ("disabled_maintain_unit_target", "Disabled Maintain Unit Target", "OrdDisabledMaintainUnitTarget"),
                    ("obstructable", "Obstructable", "OrdObstructable"),
                    ("flee_unreturnable_damage", "Flee Unreturnable Damage", "OrdFleeUnreturnableDamage"),
                    ("unused_requires_movable_unit", "Requires Movable Unit (Unused)", "OrdRequiresMovableUnit")))));
    }

    /// <summary>One column of PyDAT's flags: a checkbox per 0/1 byte, tipped with its own label.</summary>
    StackPanel Flags(params (string Property, string Label, string HintKey)[] checks)
    {
        var column = new StackPanel { Spacing = 6 };
        foreach (var (property, label, hintKey) in checks)
            column.Children.Add(CheckField(this, File, P(property), label, hintKey));
        return column;
    }
}
