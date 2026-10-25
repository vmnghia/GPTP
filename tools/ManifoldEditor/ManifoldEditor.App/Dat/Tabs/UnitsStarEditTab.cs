using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, StarEdit (PyDAT's StarEditUnitsTab): the availability and group flags, the
/// rank/sublabel (a stat_txt string) and map string, and the placement box. The preview is
/// left out for now. The group flags here leave the race bits (Basic's) alone.
/// </summary>
sealed class UnitsStarEditTab : DatTabBase
{
    /// <summary>PyDAT's first sublabel string: stat_txt's index 1302, which is string id 1303.</summary>
    const int FirstSublabel = 1302;

    StringTable? sublabelsFrom;
    IReadOnlyList<string>? sublabels;

    public UnitsStarEditTab(DatContext context) : base(DatTabId.UnitsStarEdit, context)
    {
        string availability = P("staredit_availability_flags");
        var availabilityColumns = Row(
            FlagsField(this, File, availability, new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Non-Neutral", 1UL << 0, "UnitSENonNeutral"),
                ("Unit Listing&Palette", 1UL << 1, "UnitSEListing"),
                ("Mission Briefing", 1UL << 2, "UnitSEBriefing"),
                ("Player Settings", 1UL << 3, "UnitSEPlayerSet"),
                ("All Races", 1UL << 4, "UnitSEAllRaces"),
                ("Set Doodad State", 1UL << 5, "UnitSEDoodadState"),
                ("Non-Location Triggers", 1UL << 6, "UnitSENonLoc"),
                ("Unit&Hero Settings", 1UL << 7, "UnitSEUnitHero"),
            }, 1),
            FlagsField(this, File, availability, new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Location Triggers", 1UL << 8, "UnitSELoc"),
                ("BroodWar Only", 1UL << 9, "UnitSEBW"),
            }, 1));
        availabilityColumns.Spacing = 24;

        var group = FlagsField(this, File, P("staredit_group_flags"), new (string Label, ulong Mask, string? HintKey)[]
        {
            ("Men", 1UL << 3, "UnitSEGroupMen"),
            ("Building", 1UL << 4, "UnitSEGroupBuilding"),
            ("Factory", 1UL << 5, "UnitSEGroupFactory"),
            ("Independent", 1UL << 6, "UnitSEGroupInd"),
            ("Neutral", 1UL << 7, "UnitSEGroupNeutral"),
        }, 1);

        string placement = P("staredit_placement_size");
        Panel.Children.Add(Row(Group("Availability Flags", availabilityColumns), Group("Group Flags", group)));
        Panel.Children.Add(Group("String Properties",
            ReferenceField(this, File, P("sublabel"), "Rank/Sublabel", "UnitRank", Sublabels, null, 300),
            NumberField(this, File, P("unit_map_string"), "Map String", "UnitMapString", 64)));
        Panel.Children.Add(Group("Placement Box (Pixels)",
            Titled(this, PartField(this, File, placement, 0, "Width", null), "Placement Width", "UnitSEPlaceWidth"),
            Titled(this, PartField(this, File, placement, 1, "Height", null), "Placement Height", "UnitSEPlaceHeight")));
    }

    /// <summary>
    /// "No Sublabel", then stat_txt's strings from index 1302 on, at most 255 (PyDAT's ranks).
    /// Built again only when the string table is a new one.
    /// </summary>
    IReadOnlyList<string> Sublabels()
    {
        var table = Context.Opened()?.State.Strings;
        if (sublabels is not null && ReferenceEquals(table, sublabelsFrom)) return sublabels;
        var list = new List<string> { "No Sublabel" };
        int count = table is null ? 0 : Math.Clamp(table.Count - FirstSublabel, 0, 255);
        for (int i = 1; i <= count; i++)
            list.Add(DatNames.TblString(table, (ulong)(FirstSublabel + i)) ?? $"String #{FirstSublabel + i}");
        sublabelsFrom = table;
        sublabels = list;
        return list;
    }

}
