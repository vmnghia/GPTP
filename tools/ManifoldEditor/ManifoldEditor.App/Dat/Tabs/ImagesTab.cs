using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml.Controls;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// images.dat (PyDAT's ImagesTab): the GRP (an images.tbl string, "None" at 0) and the iscript id (the
/// ids iscript.bin defines), the general and drawing properties, and the extra overlay placements (images.tbl
/// strings too) with the shield size. The preview and the "Check" buttons are left out for now.
/// </summary>
sealed class ImagesTab : DatTabBase
{
    /// <summary>The shield overlay each of PyDAT's sizes (None, Small, Medium, Large) writes.</summary>
    static readonly ulong[] ShieldOverlays = [0, 133, 2, 184];

    readonly TblChoices imagesTbl;

    IReadOnlyList<int>? iscriptFrom;
    IReadOnlyList<string>? iscriptListFrom;
    IReadOnlyList<string>? iscriptNames;

    public ImagesTab(DatContext context) : base(DatTabId.Images, context)
    {
        imagesTbl = new TblChoices(() => Context.Opened()?.Resources.ImagesTbl);

        var image = Group("Image",
            Titled(this, ReferenceField(this, File, P("grp_file"), "GRP", null, imagesTbl.WithNone, null, 300), "GRP File", "ImgGRP"),
            Titled(this, ReferenceField(this, File, P("iscript_id"), "Iscript ID", null, IscriptNames, null, 300), "Iscript ID", "ImgIscriptID"));

        var general = Group("General Properties",
            Row(
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        CheckField(this, File, P("gfx_turns"), "Graphics Turns", "ImgGfxTurns"),
                        CheckField(this, File, P("draw_if_cloaked"), "Draw If Cloaked", "ImgDrawCloaked"),
                    },
                },
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        CheckField(this, File, P("clickable"), "Clickable", "ImgClickable"),
                        CheckField(this, File, P("use_full_iscript"), "Use Full Iscript", "ImgUseFullIscript"),
                    },
                }));

        var drawing = Group("Drawing Properties",
            Titled(this, ChoiceField(this, File, P("draw_function"), "Function", null, "DrawList", 180), "Drawing Function", "ImgDrawFunction"),
            ChoiceField(this, File, P("remapping"), "Remapping", "ImgRemap", "Remapping", 180));

        var overlays = Group("Extra Overlay Placements",
            Overlay("attack_overlay", "Attack", "ImgOL1"),
            Overlay("damage_overlay", "Damage", "ImgOL2"),
            Overlay("special_overlay", "Special", "ImgOL3"),
            Overlay("landing_dust_overlay", "Landing Dust", "ImgOL4"),
            Overlay("lift_off_dust_overlay", "Lift-Off Dust", "ImgOL5"),
            Overlay("shield_overlay", "Shield", "ImgShield"),
            Titled(this, MappedChoiceField(this, File, P("shield_overlay"), "", null, () => Context.Lists["ShieldSize"], ShieldOverlays, 120),
                "Shield Overlay", "ImgShield"));

        Panel.Children.Add(image);
        Panel.Children.Add(Row(general, drawing));
        Panel.Children.Add(overlays);
    }

    /// <summary>One overlay placement: an images.tbl string, titled "Attack Overlay" in the tooltip.</summary>
    Microsoft.UI.Xaml.FrameworkElement Overlay(string property, string label, string hintKey) =>
        Titled(this, ReferenceField(this, File, P(property), label, null, imagesTbl.WithNone, null, 260), label + " Overlay", hintKey);

    /// <summary>
    /// One name per iscript id the bin defines (<see cref="IscriptIds.Names"/>): a gap is "*Unused*", an id
    /// past PyDAT's list "Unnamed Custom Entry". Empty without iscript.bin. Built again only when the
    /// ids or the list are other ones.
    /// </summary>
    IReadOnlyList<string> IscriptNames()
    {
        var ids = Context.Opened()?.Resources.IscriptIds;
        var list = Context.Lists["IscriptIDList"];
        if (iscriptNames is not null && ReferenceEquals(ids, iscriptFrom) && ReferenceEquals(list, iscriptListFrom)) return iscriptNames;
        iscriptFrom = ids;
        iscriptListFrom = list;
        iscriptNames = ids is null ? Array.Empty<string>() : IscriptIds.Names(ids, list);
        return iscriptNames;
    }
}
