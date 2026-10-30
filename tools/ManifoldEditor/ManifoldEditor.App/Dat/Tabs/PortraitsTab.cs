using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// portdata.dat (PyDAT's PortraitsTab): the idle and the talking portrait, each an SMK directory (a
/// portdata.tbl string, "None" at 0) with its SMK change and an unknown byte. The page's "Used by" list
/// covers the tab. The properties are the format's idle_ and talking_ names (PyDAT's entry.idle.* and entry.talking.*).
/// </summary>
sealed class PortraitsTab : DatTabBase
{
    readonly TblChoices portdataTbl;

    public PortraitsTab(DatContext context) : base(DatTabId.Portraits, context)
    {
        portdataTbl = new TblChoices(() => Context.Opened()?.Resources.PortdataTbl);

        Panel.Children.Add(Portrait("Idle Portrait", "idle"));
        Panel.Children.Add(Portrait("Talking Portrait", "talking"));
    }

    FrameworkElement Portrait(string title, string kind) => Group(title,
        ReferenceField(this, File, P(kind + "_portrait_file"), "SMK Dir", "PortFile", portdataTbl.WithNone, null, 300),
        Row(
            NumberField(this, File, P(kind + "_smk_change"), "SMK Change", "PortSMKChange", 64),
            NumberField(this, File, P(kind + "_unknown"), "Unknown", "PortUnk1", 64)));
}
