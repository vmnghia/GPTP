using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, Advanced (PyDAT's AdvancedUnitsTab): the special ability flags in PyDAT's three
/// columns, then infestation, the two subunits, the requirements index and the unknown flags.
/// Jump buttons are left out for now.
/// </summary>
sealed class UnitsAdvancedTab : DatTabBase
{
    IReadOnlyList<string>? unitsFrom, unitsWithNone;

    public UnitsAdvancedTab(DatContext context) : base(DatTabId.UnitsAdvanced, context)
    {
        // One field per column, all of the one property: the grid fills row by row, PyDAT's
        // columns run top to bottom.
        string flags = P("special_ability_flags");
        var columns = Row(
            FlagsField(this, File, flags, new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Flyer", 1UL << 2, "UnitAdvFlyer"),
                ("Hero", 1UL << 6, "UnitAdvHero"),
                ("Regenerate", 1UL << 7, "UnitAdvRegenerate"),
                ("Spellcaster", 1UL << 21, "UnitAdvSpellcaster"),
                ("Permanently Cloaked", 1UL << 22, "UnitAdvPermaCloak"),
                ("Invincible", 1UL << 29, "UnitAdvInvincible"),
                ("Organic", 1UL << 16, "UnitAdvOrganic"),
                ("Mechanical", 1UL << 30, "UnitAdvMechanical"),
                ("Robotic", 1UL << 14, "UnitAdvRobotic"),
                ("Detector", 1UL << 15, "UnitAdvDetector"),
                ("Subunit", 1UL << 4, "UnitAdvSubunit"),
            }, 1),
            FlagsField(this, File, flags, new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Resource Container", 1UL << 13, "UnitAdvResContainer"),
                ("Resource Depot", 1UL << 12, "UnitAdvResDepot"),
                ("Resource Miner", 1UL << 3, "UnitAdvWorker"),
                ("Requires Psi", 1UL << 19, "UnitAdvReqPsi"),
                ("Requires Creep", 1UL << 17, "UnitAdvReqCreep"),
                ("Two Units in One Egg", 1UL << 10, "UnitAdvTwoInEgg"),
                ("Single Entity", 1UL << 11, "UnitAdvSingleEntity"),
                ("Burrowable", 1UL << 20, "UnitAdvBurrow"),
                ("Cloakable", 1UL << 9, "UnitAdvCloak"),
                ("Battle Reactions", 1UL << 27, "UnitAdvBattleReactions"),
                ("Full Auto-Attack", 1UL << 28, "UnitAdvAutoAttack"),
            }, 1),
            FlagsField(this, File, flags, new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Building", 1UL << 0, "UnitAdvBuilding"),
                ("Addon", 1UL << 1, "UnitAdvAddon"),
                ("Flying Building", 1UL << 5, "UnitAdvFlyBuilding"),
                ("Use Medium Overlays", 1UL << 25, "UnitAdvOverlayMed"),
                ("Use Large Overlays", 1UL << 26, "UnitAdvOverlayLarge"),
                ("Ignore Supply Check", 1UL << 24, "UnitAdvIgnoreSupply"),
                ("Produces Units(?)", 1UL << 31, "UnitAdvProducesUnits"),
                ("Animated Overlay", 1UL << 8, "UnitAdvAnimIdle"),
                ("Carryable", 1UL << 23, "UnitAdvPickup"),
                ("Unknown", 1UL << 18, "UnitAdvUnused"),
            }, 1));
        columns.Spacing = 24;

        var unknown = FlagsField(this, File, P("unknown_flags"), new (string Label, ulong Mask, string? HintKey)[]
        {
            ("0x01", 0x01, "UnitMov01"), ("0x02", 0x02, "UnitMov02"), ("0x04", 0x04, "UnitMov04"), ("0x08", 0x08, "UnitMov08"),
            ("0x10", 0x10, "UnitMov10"), ("0x20", 0x20, "UnitMov20"), ("0x40", 0x40, "UnitMov40"), ("0x80", 0x80, "UnitMov80"),
        }, 4);

        Panel.Children.Add(Group("Advanced Properties", columns));
        Panel.Children.Add(Group("Other Properties",
            ReferenceField(this, File, P("infestation"), "Infestation", "UnitInfestation", UnitsWithNone, jump: DatId.Units),
            ReferenceField(this, File, P("subunit1"), "Subunit 1", "UnitSub1", UnitsWithNone, jump: DatId.Units),
            ReferenceField(this, File, P("subunit2"), "Subunit 2", "UnitSub2", UnitsWithNone, jump: DatId.Units),
            Row(Titled(this, NumberField(this, File, P("requirements"), "ReqIndex", null, 64), "Requirements Index", "UnitReq"), unknown)));
    }

    /// <summary>
    /// The unit names with "None" as the entry after the last vanilla unit (228): PyDAT puts it
    /// there, over the first reserved unit of an expanded file. Cached while the names are.
    /// </summary>
    IReadOnlyList<string> UnitsWithNone()
    {
        var names = Context.Names(DatId.Units);
        if (unitsWithNone is not null && ReferenceEquals(names, unitsFrom)) return unitsWithNone;
        int none = DatFormats.Of(DatId.Units).Entries;
        var list = names.ToList();
        if (list.Count > none) list[none] = "None";
        else if (list.Count == none) list.Add("None");
        unitsFrom = names;
        unitsWithNone = list;
        return list;
    }

}
