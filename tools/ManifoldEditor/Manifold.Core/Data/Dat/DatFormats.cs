namespace Manifold.Core.Data;

/// <summary>
/// The eleven .dat files' layouts: PyMS's FORMAT tables (PyMS/FileFormats/DAT/*DAT.py, MIT),
/// the properties in file order with PyMS's names.
/// </summary>
public static class DatFormats
{
    // Short names for the tables below (a member named Byte hides System.Byte inside this class).
    const DatType Byte = DatType.Byte, Short = DatType.Short, Long = DatType.Long, Size = DatType.Size,
        Position = DatType.Position, Extents = DatType.Extents, HitPoints = DatType.HitPoints, Supply = DatType.Supply;

    static DatProperty P(string name, DatType type, DatId? target = null) =>
        new(name, type, Target: target);

    static DatProperty Ranged(string name, DatType type, int first, int count, DatId? target = null) =>
        new(name, type, first, count, Target: target);

    // units.dat has 228 entries but the sounds stop at 105 and the add-on data starts at 106.
    static readonly DatFormat units = new(DatId.Units, "units.dat", 228, new[]
    {
        new DatProperty("graphics", Byte, ExpandedType: Short, Target: DatId.Flingy),
        P("subunit1", Short, DatId.Units),
        P("subunit2", Short, DatId.Units),
        Ranged("infestation", Short, 106, 96, DatId.Units),
        P("construction_animation", Long, DatId.Images),
        P("unit_direction", Byte),
        P("shield_enabled", Byte),
        P("shield_amount", Short),
        P("hit_points", HitPoints),
        P("elevation_level", Byte),
        P("unknown_flags", Byte),
        P("sublabel", Byte),
        P("comp_ai_idle", Byte, DatId.Orders),
        P("human_ai_idle", Byte, DatId.Orders),
        P("return_to_idle", Byte, DatId.Orders),
        P("attack_unit", Byte, DatId.Orders),
        P("attack_move", Byte, DatId.Orders),
        P("ground_weapon", Byte, DatId.Weapons),
        P("max_ground_hits", Byte),
        P("air_weapon", Byte, DatId.Weapons),
        P("max_air_hits", Byte),
        P("ai_internal", Byte),
        P("special_ability_flags", Long),
        P("target_acquisition_range", Byte),
        P("sight_range", Byte),
        P("armor_upgrade", Byte, DatId.Upgrades),
        P("unit_size", Byte),
        P("armor", Byte),
        P("right_click_action", Byte),
        Ranged("ready_sound", Short, 0, 106, DatId.Sfxdata),
        P("what_sound_start", Short, DatId.Sfxdata),
        P("what_sound_end", Short, DatId.Sfxdata),
        Ranged("pissed_sound_start", Short, 0, 106, DatId.Sfxdata),
        Ranged("pissed_sound_end", Short, 0, 106, DatId.Sfxdata),
        Ranged("yes_sound_start", Short, 0, 106, DatId.Sfxdata),
        Ranged("yes_sound_end", Short, 0, 106, DatId.Sfxdata),
        P("staredit_placement_size", Size),
        Ranged("addon_position", Position, 106, 96),
        P("unit_extents", Extents),
        P("portrait", Short, DatId.Portdata),
        P("mineral_cost", Short),
        P("vespene_cost", Short),
        P("build_time", Short),
        P("requirements", Short),
        P("staredit_group_flags", Byte),
        P("supply_provided", Supply),
        P("supply_required", Supply),
        P("space_required", Byte),
        P("space_provided", Byte),
        P("build_score", Short),
        P("destroy_score", Short),
        P("unit_map_string", Short),
        P("broodwar_unit_flag", Byte),
        P("staredit_availability_flags", Short),
    }, ExpandedMin: 250, ExpandedMax: 912, ExpandedMultiple: 12, Reserved: Enumerable.Range(228, 23).ToArray());

    static readonly DatFormat weapons = new(DatId.Weapons, "weapons.dat", 130, new[]
    {
        P("label", Short),
        P("graphics", Long, DatId.Flingy),
        P("unused_technology", Byte, DatId.Techdata),
        P("target_flags", Short),
        P("minimum_range", Long),
        P("maximum_range", Long),
        P("damage_upgrade", Byte, DatId.Upgrades),
        P("weapon_type", Byte),
        P("weapon_behavior", Byte),
        P("remove_after", Byte),
        P("explosion_type", Byte),
        P("inner_splash_range", Short),
        P("medium_splash_range", Short),
        P("outer_splash_range", Short),
        P("damage_amount", Short),
        P("damage_bonus", Short),
        P("weapon_cooldown", Byte),
        P("damage_factor", Byte),
        P("attack_angle", Byte),
        P("launch_spin", Byte),
        P("forward_offset", Byte),
        P("upward_offset", Byte),
        P("target_error_message", Short),
        P("icon", Short),
    }, ExpandedMax: 255);

    static readonly DatFormat flingy = new(DatId.Flingy, "flingy.dat", 209, new[]
    {
        P("sprite", Short, DatId.Sprites),
        P("speed", Long),
        P("acceleration", Short),
        P("halt_distance", Long),
        P("turn_radius", Byte),
        P("iscript_mask", Byte),
        P("movement_control", Byte),
    }, ExpandedMax: 65536);

    // sprites.dat: the first 130 sprites are the ones with no health bar or selection circle.
    static readonly DatFormat sprites = new(DatId.Sprites, "sprites.dat", 517, new[]
    {
        P("image", Short, DatId.Images),
        Ranged("health_bar", Byte, 130, 387),
        P("unused", Byte),
        P("is_visible", Byte),
        Ranged("selection_circle_image", Byte, 130, 387),
        Ranged("selection_circle_offset", Byte, 130, 387),
    }, ExpandedMax: 65536);

    static readonly DatFormat images = new(DatId.Images, "images.dat", 999, new[]
    {
        P("grp_file", Long),
        P("gfx_turns", Byte),
        P("clickable", Byte),
        P("use_full_iscript", Byte),
        P("draw_if_cloaked", Byte),
        P("draw_function", Byte),
        P("remapping", Byte),
        P("iscript_id", Long),
        P("shield_overlay", Long),
        P("attack_overlay", Long),
        P("damage_overlay", Long),
        P("special_overlay", Long),
        P("landing_dust_overlay", Long),
        P("lift_off_dust_overlay", Long),
    }, ExpandedMax: 65536);

    static readonly DatFormat upgrades = new(DatId.Upgrades, "upgrades.dat", 61, new[]
    {
        P("mineral_cost_base", Short),
        P("mineral_cost_factor", Short),
        P("vespene_cost_base", Short),
        P("vespene_cost_factor", Short),
        P("research_time_base", Short),
        P("research_time_factor", Short),
        P("requirements", Short),
        P("icon", Short),
        P("label", Short),
        P("staredit_race", Byte),
        P("max_repeats", Byte),
        P("broodwar_only", Byte),
    }, ExpandedMax: 256);

    static readonly DatFormat techdata = new(DatId.Techdata, "techdata.dat", 44, new[]
    {
        P("mineral_cost", Short),
        P("vespene_cost", Short),
        P("research_time", Short),
        P("energy_required", Short),
        P("research_requirements", Short),
        P("use_requirements", Short),
        P("icon", Short),
        P("label", Short),
        P("staredit_race", Byte),
        P("researched", Byte),
        P("broodwar_only", Byte),
    }, ExpandedMax: 255);

    static readonly DatFormat sfxdata = new(DatId.Sfxdata, "sfxdata.dat", 1144, new[]
    {
        P("sound_file", Long),
        P("priority", Byte),
        P("flags", Byte),
        P("portrait_length_adjust", Short),
        P("minimum_volume", Byte),
    }, ExpandedMax: 65536);

    static readonly DatFormat portdata = new(DatId.Portdata, "portdata.dat", 110, new[]
    {
        P("idle_portrait_file", Long),
        P("talking_portrait_file", Long),
        P("idle_smk_change", Byte),
        P("talking_smk_change", Byte),
        P("idle_unknown", Byte),
        P("talking_unknown", Byte),
    }, ExpandedMax: 65535);

    static readonly DatFormat mapdata = new(DatId.Mapdata, "mapdata.dat", 65, new[]
    {
        P("map_file", Long),
    });

    static readonly DatFormat orders = new(DatId.Orders, "orders.dat", 189, new[]
    {
        P("label", Short),
        P("use_weapon_targeting", Byte),
        P("unused_is_secondary", Byte),
        P("unused_allow_non_subunits", Byte),
        P("changes_subunit_order", Byte),
        P("unused_allow_subunits", Byte),
        P("interruptable", Byte),
        P("waypoints_slowdown", Byte),
        P("queueable", Byte),
        P("disabled_maintain_unit_target", Byte),
        P("obstructable", Byte),
        P("flee_unreturnable_damage", Byte),
        P("unused_requires_movable_unit", Byte),
        P("targeting", Byte, DatId.Weapons),
        P("technology_energy", Byte, DatId.Techdata),
        P("iscript_animation", Byte),
        P("highlight_icon", Short),
        P("requirements", Short),
        P("obscured_order", Byte, DatId.Orders),
    }, ExpandedMax: 255);

    /// <summary>All eleven, in <see cref="DatId"/> order.</summary>
    public static IReadOnlyList<DatFormat> All { get; } =
        new[] { units, weapons, flingy, sprites, images, upgrades, techdata, sfxdata, portdata, mapdata, orders };

    public static DatFormat Of(DatId id) => All[(int)id];

    /// <summary>Whether GPTP's plugin reads this file's expanded layout (hooks/dat_ext): the others are the exe's.</summary>
    public static bool PluginExtends(DatId id) => id is DatId.Images or DatId.Sprites or DatId.Flingy;
}
