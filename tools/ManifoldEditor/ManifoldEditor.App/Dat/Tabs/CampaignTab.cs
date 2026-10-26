using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>mapdata.dat (PyDAT's MapsTab, shown as Campaign): the mission directory, a mapdata.tbl string ("None" at 0).</summary>
sealed class CampaignTab : DatTabBase
{
    readonly TblChoices mapdataTbl;

    public CampaignTab(DatContext context) : base(DatTabId.Campaign, context)
    {
        mapdataTbl = new TblChoices(() => Context.Opened()?.Resources.MapdataTbl);

        Panel.Children.Add(Group("Campaign Properties",
            ReferenceField(this, File, P("map_file"), "Mission Dir", "MapFile", mapdataTbl.WithNone, null, 300)));
    }
}
