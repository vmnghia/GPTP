using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// techdata.dat (PyDAT's TechnologyTab): the icon and label, the cost with the research time also in
/// seconds, and the properties. The icon preview and picker and the "Used by" lists are left out for now.
/// </summary>
sealed class TechdataTab : DatTabBase
{
    readonly TblChoices statTxt;

    public TechdataTab(DatContext context) : base(DatTabId.Techdata, context)
    {
        statTxt = new TblChoices(() => Context.Opened()?.State.Strings);

        Panel.Children.Add(Group("Technology Display",
            Titled(this, ReferenceField(this, File, P("icon"), "Icon", null, () => Context.Lists["Icons"], null, 300),
                "Technology Icon", "TechIcon"),
            Titled(this, ReferenceField(this, File, P("label"), "Label", null, statTxt.WithNone, null, 300),
                "Technology Label", "TechLabel")));

        var cost = Group("Technology Cost",
            Titled(this, NumberField(this, File, P("mineral_cost"), "Minerals", null, 64), "Mineral Cost", "TechMinerals"),
            Titled(this, NumberField(this, File, P("vespene_cost"), "Vespene", null, 64), "Vespene Cost", "TechVespene"),
            Row(
                Titled(this, NumberField(this, File, P("research_time"), "Time", null, 64), "Build Time", "TechTime"),
                DerivedText(this, e => Seconds(ValueOf(Context, File, e, P("research_time"))))),
            // PyDAT tips this one with TechVespene (the vespene hint); TechEnergy is the energy one.
            Titled(this, NumberField(this, File, P("energy_required"), "Energy", null, 64), "Energy Cost", "TechEnergy"));

        var properties = Group("Technology Properties",
            Titled(this, NumberField(this, File, P("research_requirements"), "ResearchReq", null, 80), "Research Requirements", "TechReq"),
            Titled(this, NumberField(this, File, P("use_requirements"), "UseReq", null, 80), "Usage Requirements", "TechUseReq"),
            ChoiceField(this, File, P("staredit_race"), "Race", "TechRace", "Races", 140),
            Row(
                CheckField(this, File, P("researched"), "Researched", "TechUnused"),
                CheckField(this, File, P("broodwar_only"), "BroodWar", "TechBW")));
        Panel.Children.Add(Row(cost, properties));
    }
}
