using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// upgrades.dat (PyDAT's UpgradesTab): the icon (a cmdicons frame) and label (a stat_txt.tbl string), the
/// base and per-level costs with the times also in seconds, and the misc. properties. The icon preview
/// and picker and the "Used by" lists are left out for now.
/// </summary>
sealed class UpgradesTab : DatTabBase
{
    readonly TblChoices statTxt;

    public UpgradesTab(DatContext context) : base(DatTabId.Upgrades, context)
    {
        statTxt = new TblChoices(() => Context.Opened()?.State.Strings);

        Panel.Children.Add(Group("Upgrade Display",
            Titled(this, ReferenceField(this, File, P("icon"), "Icon", null, () => Context.Lists["Icons"], null, 300),
                "Upgrade Icon", "UpgIcon"),
            Titled(this, ReferenceField(this, File, P("label"), "Label", null, statTxt.WithNone, null, 300),
                "Upgrade Label", "UpgLabel")));

        var baseCost = Group("Base Cost",
            Titled(this, NumberField(this, File, P("mineral_cost_base"), "Minerals", null, 64), "Base Mineral Cost", "UpgMinerals"),
            Titled(this, NumberField(this, File, P("vespene_cost_base"), "Vespene", null, 64), "Base Vespene Cost", "UpgVespene"),
            Row(
                Titled(this, NumberField(this, File, P("research_time_base"), "Time", null, 64), "Base Build Time", "UpgTime"),
                DerivedText(this, e => Seconds(ValueOf(Context, File, e, P("research_time_base"))))));

        var factorCost = Group("Factor Cost",
            Titled(this, NumberField(this, File, P("mineral_cost_factor"), "Minerals", null, 64), "Mineral Cost Factor", "UpgFactorMinerals"),
            Titled(this, NumberField(this, File, P("vespene_cost_factor"), "Vespene", null, 64), "Vespene Cost Factor", "UpgFactorVespene"),
            Row(
                Titled(this, NumberField(this, File, P("research_time_factor"), "Time", null, 64), "Build Time Factor", "UpgFactorTime"),
                DerivedText(this, e => Seconds(ValueOf(Context, File, e, P("research_time_factor"))))));
        Panel.Children.Add(Row(baseCost, factorCost));

        Panel.Children.Add(Group("Misc.",
            NumberField(this, File, P("max_repeats"), "Max Repeats", "UpgRepeats", 64),
            Titled(this, NumberField(this, File, P("requirements"), "ReqIndex", null, 64), "Requirements Index", "UpgReq"),
            ChoiceField(this, File, P("staredit_race"), "Race", "UpgRace", "Races", 140),
            CheckField(this, File, P("broodwar_only"), "BroodWar", "UpgIsBW")));
    }
}
