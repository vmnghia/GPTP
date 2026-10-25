using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, Basic (PyDAT's BasicUnitsTab): vital statistics, build cost, weapons, supply,
/// space, score and the rest. The group flags show only the race bits here (the others are
/// on the StarEdit tab). Jump buttons are left out for now.
/// </summary>
sealed class UnitsBasicTab : DatTabBase
{
    public UnitsBasicTab(DatContext context) : base(DatTabId.UnitsBasic, context)
    {
        var vital = Group("Vital Statistics",
            HitPointsField(this, File, P("hit_points"), "Hit Points", "UnitHP", "UnitHPFraction"),
            Row(
                NumberField(this, File, P("shield_amount"), "Shields", "UnitSP", 100),
                Titled(this, CheckField(this, File, P("shield_enabled"), "Enabled", null), "Shields Enabled", "UnitShieldEnable")),
            NumberField(this, File, P("armor"), "Armor", "UnitArmor", 100),
            Titled(this, ReferenceField(this, File, P("armor_upgrade"), "Upgrade", null,
                () => Context.Names(DatId.Upgrades), "None", 200), "Armor Upgrade", "UnitArmorUpgrade"));

        var cost = Group("Build Cost",
            NumberField(this, File, P("mineral_cost"), "Minerals", "UnitMinerals", 64),
            NumberField(this, File, P("vespene_cost"), "Vespene", "UnitVespene", 64),
            Row(
                Titled(this, NumberField(this, File, P("build_time"), "Time", null, 64), "Build Time", "UnitTime"),
                DerivedText(this, e => Seconds(ValueOf(Context, File, e, P("build_time"))))),
            CheckField(this, File, P("broodwar_unit_flag"), "BroodWar", "UnitIsBW"));

        var weapons = Group("Weapons",
            Titled(this, ReferenceField(this, File, P("ground_weapon"), "Ground", null,
                () => Context.Names(DatId.Weapons), "None"), "Ground Weapon", "UnitWeaponGround"),
            Titled(this, NumberField(this, File, P("max_ground_hits"), "Max Hits", null, 44), "Max Ground Hits", "UnitGroundMax"),
            Titled(this, ReferenceField(this, File, P("air_weapon"), "Air", null,
                () => Context.Names(DatId.Weapons), "None"), "Air Weapon", "UnitWeaponAir"),
            Titled(this, NumberField(this, File, P("max_air_hits"), "Max Hits", null, 44), "Max Air Hits", "UnitAirMax"));

        var supply = Group("Supply",
            Titled(this, SupplyField(this, File, P("supply_required"), "Required", null), "Supply Required", "UnitSupReq"),
            Titled(this, SupplyField(this, File, P("supply_provided"), "Provided", null), "Supply Provided", "UnitSupProv"),
            FlagsField(this, File, P("staredit_group_flags"), new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Zerg", 1UL << 0, "UnitSEGroupZerg"),
                ("Terran", 1UL << 1, "UnitSEGroupTerran"),
                ("Protoss", 1UL << 2, "UnitSEGroupProtoss"),
            }, 3));

        var space = Group("Space",
            Titled(this, NumberField(this, File, P("space_required"), "Required", null, 44), "Space Required", "UnitSpaceReq"),
            Titled(this, NumberField(this, File, P("space_provided"), "Provided", null, 44), "Space Provided", "UnitSpaceProv"));

        var score = Group("Score",
            Titled(this, NumberField(this, File, P("build_score"), "Build", null, 64), "Build Score", "UnitScoreBuild"),
            Titled(this, NumberField(this, File, P("destroy_score"), "Destroy", null, 64), "Destroy Score", "UnitScoreDestroy"));

        var other = Group("Other",
            Row(
                ChoiceField(this, File, P("unit_size"), "Unit Size", "UnitSize", "UnitSize", 140),
                Titled(this, NumberField(this, File, P("sight_range"), "Sight", null, 44), "Sight Range", "UnitSight"),
                NumberField(this, File, P("target_acquisition_range"), "Target Acquisition Range", "UnitTAR", 44)));

        Panel.Children.Add(Row(vital, cost));
        Panel.Children.Add(weapons);
        Panel.Children.Add(Row(supply, space, score));
        Panel.Children.Add(other);
    }

}
