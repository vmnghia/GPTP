using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml.Controls;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// images.dat (PyDAT's ImagesTab): the GRP (an images.tbl string, "None" at 0) and the iscript id (the
/// ids iscript.bin defines), the general and drawing properties, and the extra overlay placements (images.tbl
/// strings too) with the shield size. The "Check" buttons beside GRP File and Iscript ID list the other images
/// with the same value in "Used by". PyDAT's preview shows the image's frame 0 with its drawing function and remapping.
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
            WithCheck(Titled(this, ReferenceField(this, File, P("grp_file"), "GRP", null, imagesTbl.WithNone, null, 300), "GRP File", "ImgGRP"),
                "grp_file", "GRP File"),
            WithCheck(Titled(this, ReferenceField(this, File, P("iscript_id"), "Iscript ID", null, IscriptNames, null, 300), "Iscript ID", "ImgIscriptID"),
                "iscript_id", "IScript ID"));

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

        var preview = new PreviewBox();
        void Redraw() => preview.Show(Context.Settings.Previews.Image && Entry >= 0
            ? Context.Preview?.ImageScene(Context.File, Context.ImagesTbl, Entry)
            : null);
        var previewGroup = Group("Preview",
            preview.Root,
            PreviewBox.Toggle(this, "Show Preview", s => s.Image, (s, on) => s with { Image = on }, Redraw));
        AddLoader(_ => Redraw());

        Panel.Children.Add(image);
        Panel.Children.Add(Row(general, drawing));
        Panel.Children.Add(Row(overlays, previewGroup));
    }

    /// <summary>
    /// PyDAT's "Check" button after a field's line: fills "Used by" with the other images whose
    /// <paramref name="property"/> equals the shown one's (<see cref="DatRefs.SameValue"/>), and opens it.
    /// The field is a <see cref="DatFields.Line"/> (the button joins it).
    /// </summary>
    Microsoft.UI.Xaml.FrameworkElement WithCheck(Microsoft.UI.Xaml.FrameworkElement field, string property, string label)
    {
        string name = P(property);
        var button = new Microsoft.UI.Xaml.Controls.Button
        {
            Content = "Check", VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Padding = new Microsoft.UI.Xaml.Thickness(8, 3, 8, 3),
        };
        ToolTipService.SetToolTip(button, $"List the other images with this {label}");
        button.Click += (_, _) =>
        {
            // A box still being typed in commits first: the value checked is the one shown.
            CommitPending();
            if (Entry >= 0 && Context.File(File) is { } images && Entry < images.Count)
                Context.ShowUsedBy(DatRefs.SameValue(images, Entry, name, label));
        };
        ((StackPanel)field).Children.Add(button);
        return field;
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
