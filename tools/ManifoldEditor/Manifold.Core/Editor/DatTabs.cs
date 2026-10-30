using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>PyDAT's tabs, in its order: the units file has six, every other file one.</summary>
public enum DatTabId
{
    UnitsBasic, UnitsAdvanced, UnitsSounds, UnitsGraphics, UnitsStarEdit, UnitsAiActions,
    Weapons, Flingy, Sprites, Images, Upgrades, Techdata, Sounds, Portraits, Campaign, Orders,
}

/// <summary>One tab: the file it edits, PyDAT's name for it, and the format properties it shows.</summary>
public sealed record DatTab(DatTabId Id, DatId File, string Name, IReadOnlyList<string> Properties);

/// <summary>
/// Which tab shows which property of which file, from each PyDAT tab's load_entry and
/// save_entry (PyMS/PyDAT/*Tab.py, MIT). Names are the format's (<see cref="DatFormats"/>); where a
/// tab file reads an attribute under another name, the format property it stands for is
/// listed (OrdersTab's weapon_targeting is orders.dat's targeting; PortraitsTab's
/// idle and talking are the idle_ and talking_ portrait properties). A few properties show on
/// two tabs: the weapons on Basic and AI Actions, the placement size on Graphics and StarEdit,
/// the group flags on Basic (the race) and StarEdit.
/// </summary>
public static class DatTabs
{
    static DatTab T(DatTabId id, DatId file, string name, params string[] properties) =>
        new(id, file, name, properties);

    public static IReadOnlyList<DatTab> All { get; } =
    [
        T(DatTabId.UnitsBasic, DatId.Units, "Basic",
            "hit_points", "shield_enabled", "shield_amount", "armor", "armor_upgrade", "mineral_cost",
            "vespene_cost", "build_time", "broodwar_unit_flag", "ground_weapon", "max_ground_hits",
            "air_weapon", "max_air_hits", "supply_required", "supply_provided", "staredit_group_flags",
            "space_required", "space_provided", "build_score", "destroy_score", "unit_size",
            "sight_range", "target_acquisition_range"),
        T(DatTabId.UnitsAdvanced, DatId.Units, "Advanced",
            "subunit1", "subunit2", "infestation", "requirements", "special_ability_flags", "unknown_flags"),
        T(DatTabId.UnitsSounds, DatId.Units, "Sounds",
            "ready_sound", "what_sound_start", "what_sound_end", "pissed_sound_start", "pissed_sound_end",
            "yes_sound_start", "yes_sound_end"),
        T(DatTabId.UnitsGraphics, DatId.Units, "Graphics",
            "graphics", "construction_animation", "unit_direction", "elevation_level", "portrait",
            "unit_extents", "addon_position", "staredit_placement_size"),
        T(DatTabId.UnitsStarEdit, DatId.Units, "StarEdit",
            "staredit_group_flags", "staredit_placement_size", "sublabel", "unit_map_string",
            "staredit_availability_flags"),
        T(DatTabId.UnitsAiActions, DatId.Units, "AI Actions",
            "comp_ai_idle", "human_ai_idle", "return_to_idle", "attack_unit", "attack_move",
            "ground_weapon", "air_weapon", "right_click_action", "ai_internal"),
        T(DatTabId.Weapons, DatId.Weapons, "Weapons",
            "label", "graphics", "unused_technology", "target_flags", "minimum_range", "maximum_range",
            "damage_upgrade", "weapon_type", "weapon_behavior", "remove_after", "explosion_type",
            "inner_splash_range", "medium_splash_range", "outer_splash_range", "damage_amount",
            "damage_bonus", "weapon_cooldown", "damage_factor", "attack_angle", "launch_spin",
            "forward_offset", "upward_offset", "target_error_message", "icon"),
        T(DatTabId.Flingy, DatId.Flingy, "Flingy",
            "sprite", "speed", "acceleration", "halt_distance", "turn_radius", "iscript_mask",
            "movement_control"),
        T(DatTabId.Sprites, DatId.Sprites, "Sprites",
            "image", "health_bar", "unused", "is_visible", "selection_circle_image",
            "selection_circle_offset"),
        T(DatTabId.Images, DatId.Images, "Images",
            "grp_file", "gfx_turns", "clickable", "use_full_iscript", "draw_if_cloaked", "draw_function",
            "remapping", "iscript_id", "shield_overlay", "attack_overlay", "damage_overlay",
            "special_overlay", "landing_dust_overlay", "lift_off_dust_overlay"),
        T(DatTabId.Upgrades, DatId.Upgrades, "Upgrades",
            "mineral_cost_base", "mineral_cost_factor", "vespene_cost_base", "vespene_cost_factor",
            "research_time_base", "research_time_factor", "requirements", "icon", "label",
            "staredit_race", "max_repeats", "broodwar_only"),
        T(DatTabId.Techdata, DatId.Techdata, "Techdata",
            "mineral_cost", "vespene_cost", "research_time", "energy_required", "research_requirements",
            "use_requirements", "icon", "label", "staredit_race", "researched", "broodwar_only"),
        T(DatTabId.Sounds, DatId.Sfxdata, "Sound Effects",
            "sound_file", "priority", "flags", "portrait_length_adjust", "minimum_volume"),
        T(DatTabId.Portraits, DatId.Portdata, "Portraits",
            "idle_portrait_file", "idle_smk_change", "idle_unknown",
            "talking_portrait_file", "talking_smk_change", "talking_unknown"),
        T(DatTabId.Campaign, DatId.Mapdata, "Campaign", "map_file"),
        T(DatTabId.Orders, DatId.Orders, "Orders",
            "label", "use_weapon_targeting", "unused_is_secondary", "unused_allow_non_subunits",
            "changes_subunit_order", "unused_allow_subunits", "interruptable", "waypoints_slowdown",
            "queueable", "disabled_maintain_unit_target", "obstructable", "flee_unreturnable_damage",
            "unused_requires_movable_unit", "targeting", "technology_energy", "iscript_animation",
            "highlight_icon", "requirements", "obscured_order"),
    ];

    public static DatTab Of(DatTabId id) => All[(int)id];

    /// <summary>The tabs of a file, in PyDAT's order.</summary>
    public static IReadOnlyList<DatTab> For(DatId file) => All.Where(t => t.File == file).ToArray();

    /// <summary>
    /// The tab to show a property on (a jump from a list): <paramref name="current"/> when it
    /// lists the property, else the first tab of the file that does.
    /// </summary>
    public static DatTabId TabFor(DatId file, string property, DatTabId? current)
    {
        if (current is DatTabId c && Of(c).File == file && Of(c).Properties.Contains(property)) return c;
        foreach (var tab in All)
            if (tab.File == file && tab.Properties.Contains(property)) return tab.Id;
        throw new ArgumentException($"{DatFormats.Of(file).FileName} has no tab showing \"{property}\"", nameof(property));
    }

    /// <summary>The property's name, checked against the tab (the App binds through it).</summary>
    public static string Property(DatTabId tab, string name) =>
        Of(tab).Properties.Contains(name)
            ? name
            : throw new ArgumentException($"the {Of(tab).Name} tab does not show \"{name}\"", nameof(name));
}
