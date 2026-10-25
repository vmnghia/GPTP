using System.Text;

namespace Manifold.Core.Data;

/// <summary>How PyMS writes a value in its text: as the number, or split into named lines.</summary>
public enum DatCoderKind
{
    /// <summary>The number.</summary>
    Plain,
    /// <summary>DATBoolCoder: False for 0, True for 1, the number for anything else.</summary>
    Bool,
    /// <summary>DATFlagsCoder: a True/False line for every bit below <see cref="DatCoder.Bits"/>, named by <see cref="DatCoder.FlagNames"/> or flag_0xNN.</summary>
    Flags,
    /// <summary>.whole and .fraction (24.8 fixed point).</summary>
    HitPoints,
    /// <summary>.whole and .half (True when the odd bit is set).</summary>
    Supply,
    /// <summary>.width and .height.</summary>
    Size,
    /// <summary>.x and .y.</summary>
    Position,
    /// <summary>.left, .up, .right and .down.</summary>
    Extents,
}

/// <param name="Bits">Flags only: how many bits are listed.</param>
/// <param name="FlagNames">Flags only: the name of each flag, by its value (1 &lt;&lt; bit); a bit with none is flag_0xNN.</param>
public sealed record DatCoder(DatCoderKind Kind, int Bits = 0, IReadOnlyDictionary<ulong, string>? FlagNames = null);

/// <summary>One name in PyMS's text: the format property it writes and the coder that writes it.</summary>
/// <param name="Property">The format property (<see cref="DatProperty.Name"/>).</param>
/// <param name="Key">PyMS's name for it in the text; for a portrait, idle.portrait_file and the like (the dot is in the key, the coder is Plain).</param>
public sealed record DatTextKey(string Property, string Key, DatCoder Coder);

/// <summary>
/// PyMS's text form of the .dat files (AbstractDAT.export_entries, PyMS/FileFormats/DAT, MIT):
/// the keys of each file in PyMS's order and the coder of each, and the export itself. The goldens
/// are PyMS's own output on the same bytes (fixtures/pydat-txt).
/// </summary>
public static class DatText
{
    static readonly DatCoder plain = new(DatCoderKind.Plain), boolean = new(DatCoderKind.Bool);

    static DatTextKey K(string property) => new(property, property, plain);
    static DatTextKey B(string property) => new(property, property, boolean);
    static DatTextKey C(string property, DatCoderKind kind) => new(property, property, new DatCoder(kind));

    /// <summary>A flags key: <paramref name="names"/> are the flags from bit 0 up, null where PyMS names none.</summary>
    static DatTextKey F(string property, int bits, params string?[] names)
    {
        var map = new Dictionary<ulong, string>();
        for (int i = 0; i < names.Length; i++)
            if (names[i] is { } name) map[1UL << i] = name;
        return new(property, property, new DatCoder(DatCoderKind.Flags, bits, map));
    }

    static IEnumerable<DatTextKey> Plains(params string[] properties) => properties.Select(K);

    static readonly IReadOnlyList<DatTextKey> units =
    [
        K("graphics"), K("subunit1"), K("subunit2"), K("infestation"), K("construction_animation"),
        K("unit_direction"), K("shield_enabled"), K("shield_amount"), C("hit_points", DatCoderKind.HitPoints),
        K("elevation_level"), F("unknown_flags", 8), K("sublabel"), K("comp_ai_idle"), K("human_ai_idle"),
        K("return_to_idle"), K("attack_unit"), K("attack_move"), K("ground_weapon"), K("max_ground_hits"),
        K("air_weapon"), K("max_air_hits"), K("ai_internal"),
        F("special_ability_flags", 32, "building", "addon", "flyer", "resource_miner", "subunit",
            "flying_building", "hero", "regenerate", "animated_idle", "cloakable", "two_units_in_one_egg",
            "single_entity", "resource_depot", "resource_container", "robotic", "detector", "organic",
            "requires_creep", "unused", "requires_psi", "burrowable", "spellcaster", "permanent_cloak",
            "pickup_item", "ignores_supply_check", "use_medium_overlays", "use_large_overlays",
            "battle_reactions", "full_auto_attack", "invincible", "mechanical", "produces_units"),
        K("target_acquisition_range"), K("sight_range"), K("armor_upgrade"), K("unit_size"), K("armor"),
        K("right_click_action"), K("ready_sound"), K("what_sound_start"), K("what_sound_end"),
        K("pissed_sound_start"), K("pissed_sound_end"), K("yes_sound_start"), K("yes_sound_end"),
        C("staredit_placement_size", DatCoderKind.Size), C("addon_position", DatCoderKind.Position),
        C("unit_extents", DatCoderKind.Extents), K("portrait"), K("mineral_cost"), K("vespene_cost"),
        K("build_time"), K("requirements"),
        F("staredit_group_flags", 8, "zerg", "terran", "protoss", "men", "building", "factory", "independent", "neutral"),
        C("supply_provided", DatCoderKind.Supply), C("supply_required", DatCoderKind.Supply),
        K("space_required"), K("space_provided"), K("build_score"), K("destroy_score"), K("unit_map_string"),
        K("broodwar_unit_flag"),
        F("staredit_availability_flags", 10, "non_neutral", "unit_listing", "mission_briefing", "player_settings",
            "all_races", "set_doodad_state", "non_location_triggers", "unit_hero_settings", "location_triggers",
            "broodwar_only"),
    ];

    static readonly IReadOnlyList<DatTextKey> weapons =
    [
        K("label"), K("graphics"), K("unused_technology"),
        F("target_flags", 9, "air", "ground", "mechanical", "organic", "non_building", "non_robotic", "terrain",
            "organic_or_mechanical", "own"),
        .. Plains("minimum_range", "maximum_range", "damage_upgrade", "weapon_type", "weapon_behavior",
            "remove_after", "explosion_type", "inner_splash_range", "medium_splash_range", "outer_splash_range",
            "damage_amount", "damage_bonus", "weapon_cooldown", "damage_factor", "attack_angle", "launch_spin",
            "forward_offset", "upward_offset", "target_error_message", "icon"),
    ];

    static readonly IReadOnlyList<DatTextKey> flingy =
        [.. Plains("sprite", "speed", "acceleration", "halt_distance", "turn_radius", "iscript_mask", "movement_control")];

    static readonly IReadOnlyList<DatTextKey> sprites =
        [K("image"), K("health_bar"), B("unused"), B("is_visible"), K("selection_circle_image"), K("selection_circle_offset")];

    static readonly IReadOnlyList<DatTextKey> images =
    [
        K("grp_file"), B("gfx_turns"), B("clickable"), B("use_full_iscript"), B("draw_if_cloaked"),
        .. Plains("draw_function", "remapping", "iscript_id", "shield_overlay", "attack_overlay", "damage_overlay",
            "special_overlay", "landing_dust_overlay", "lift_off_dust_overlay"),
    ];

    static readonly IReadOnlyList<DatTextKey> upgrades =
    [
        .. Plains("mineral_cost_base", "mineral_cost_factor", "vespene_cost_base", "vespene_cost_factor",
            "research_time_base", "research_time_factor", "requirements", "icon", "label", "staredit_race", "max_repeats"),
        B("broodwar_only"),
    ];

    static readonly IReadOnlyList<DatTextKey> techdata =
    [
        .. Plains("mineral_cost", "vespene_cost", "research_time", "energy_required", "research_requirements",
            "use_requirements", "icon", "label", "staredit_race"),
        B("researched"), B("broodwar_only"),
    ];

    static readonly IReadOnlyList<DatTextKey> sfxdata =
    [
        K("sound_file"), K("priority"),
        F("flags", 6, "preload", "unit_speech", null, null, "one_at_a_time", "never_preempt"),
        K("portrait_length_adjust"), K("minimum_volume"),
    ];

    // PyMS writes the two portraits as idle and talking, each with its three values.
    static readonly IReadOnlyList<DatTextKey> portdata =
    [
        new("idle_portrait_file", "idle.portrait_file", plain), new("idle_smk_change", "idle.smk_change", plain),
        new("idle_unknown", "idle.unknown", plain),
        new("talking_portrait_file", "talking.portrait_file", plain), new("talking_smk_change", "talking.smk_change", plain),
        new("talking_unknown", "talking.unknown", plain),
    ];

    static readonly IReadOnlyList<DatTextKey> mapdata = [K("map_file")];

    // PyMS calls orders.dat's targeting weapon_targeting.
    static readonly IReadOnlyList<DatTextKey> orders =
    [
        K("label"), B("use_weapon_targeting"), B("unused_is_secondary"), B("unused_allow_non_subunits"),
        B("changes_subunit_order"), B("unused_allow_subunits"), B("interruptable"), B("waypoints_slowdown"),
        B("queueable"), B("disabled_maintain_unit_target"), B("obstructable"), B("flee_unreturnable_damage"),
        B("unused_requires_movable_unit"), new("targeting", "weapon_targeting", plain),
        .. Plains("technology_energy", "iscript_animation", "highlight_icon", "requirements", "obscured_order"),
    ];

    static readonly IReadOnlyList<DatTextKey>[] keys =
        [units, weapons, flingy, sprites, images, upgrades, techdata, sfxdata, portdata, mapdata, orders];

    static readonly string[] exportNames =
        ["Unit", "Weapon", "Flingy", "Sprite", "Image", "Upgrade", "Technology", "Sound", "Portraits", "Map", "Order"];

    /// <summary>The name that heads each entry in PyMS's text: "Unit(0):".</summary>
    public static string ExportName(DatId id) => exportNames[(int)id];

    /// <summary>The file's keys in PyMS's order; every format property is the property of at least one.</summary>
    public static IReadOnlyList<DatTextKey> Keys(DatId id) => keys[(int)id];

    /// <summary>
    /// PyMS's text for the entries, as AbstractDAT.export_entries writes it: a header, a tab and
    /// "name value" line for each value the entry has (a property the vanilla layout does not
    /// cover on it is left out), and a blank line between entries (PyMS joins every entry's
    /// lines, each entry ending in an empty one, with newlines: the text ends after the last value's newline). <paramref name="properties"/> are format
    /// property names (a sub-tab's list); null or empty means all.
    /// </summary>
    public static string Export(DatFile file, IEnumerable<int> entries, IReadOnlyCollection<string>? properties = null)
    {
        var id = file.Format.Id;
        string name = ExportName(id);
        bool all = properties is null || properties.Count == 0;
        var sb = new StringBuilder();
        bool first = true;
        foreach (int entry in entries)
        {
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(name).Append('(').Append(entry).Append("):\n");
            foreach (var key in Keys(id))
            {
                if (!all && !properties!.Contains(key.Property)) continue;
                if (file.Get(entry, key.Property) is not { } value) continue;
                Write(sb, key, value);
            }
        }
        return sb.ToString();
    }

    static void Write(StringBuilder sb, DatTextKey key, ulong value)
    {
        void Line(string suffix, string text) =>
            sb.Append('\t').Append(key.Key).Append(suffix).Append(' ').Append(text).Append('\n');
        static string Word(bool b) => b ? "True" : "False";

        switch (key.Coder.Kind)
        {
            case DatCoderKind.Plain:
                Line("", value.ToString());
                break;
            case DatCoderKind.Bool:
                Line("", value switch { 0 => "False", 1 => "True", _ => value.ToString() });
                break;
            case DatCoderKind.Flags:
                for (int bit = 0; bit < key.Coder.Bits; bit++)
                {
                    ulong flag = 1UL << bit;
                    string flagName = key.Coder.FlagNames is { } names && names.TryGetValue(flag, out var n)
                        ? n : $"flag_0x{flag:X2}";
                    Line("." + flagName, Word((value & flag) == flag));
                }
                break;
            case DatCoderKind.HitPoints:
                var (whole, fraction) = DatValues.HitPoints(value);
                Line(".whole", whole.ToString());
                Line(".fraction", fraction.ToString());
                break;
            case DatCoderKind.Supply:
                var (supply, half) = DatValues.Supply(value);
                Line(".whole", supply.ToString());
                Line(".half", Word(half));
                break;
            case DatCoderKind.Size:
                Line(".width", DatValues.Part(value, 0).ToString());
                Line(".height", DatValues.Part(value, 1).ToString());
                break;
            case DatCoderKind.Position:
                Line(".x", DatValues.Part(value, 0).ToString());
                Line(".y", DatValues.Part(value, 1).ToString());
                break;
            case DatCoderKind.Extents:
                Line(".left", DatValues.Part(value, 0).ToString());
                Line(".up", DatValues.Part(value, 1).ToString());
                Line(".right", DatValues.Part(value, 2).ToString());
                Line(".down", DatValues.Part(value, 3).ToString());
                break;
        }
    }
}
