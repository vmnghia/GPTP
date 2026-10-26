using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, Graphics (PyDAT's GraphicsUnitsTab): the sprite graphics, the unit dimensions
/// (extents: left, up, right, down) and the addon position, and PyDAT's preview: the unit's
/// picture (graphics, then the flingy's sprite, then its image) with the placement box (red), the
/// dimensions (green) and the add-on on its parent building (yellow), each behind its checkbox.
/// The add-on checkbox and parent box are enabled only for units with an add-on position.
/// </summary>
sealed class UnitsGraphicsTab : DatTabBase
{
    public UnitsGraphicsTab(DatContext context) : base(DatTabId.UnitsGraphics, context)
    {
        Panel.Children.Add(Group("Sprite Graphics",
            ReferenceField(this, File, P("graphics"), "Graphics", "UnitGfx", () => Context.Names(DatId.Flingy), null, 300, jump: DatId.Flingy),
            ReferenceField(this, File, P("construction_animation"), "Construction", "UnitConstruction",
                () => Context.Names(DatId.Images), null, 300, jump: DatId.Images),
            ReferenceField(this, File, P("portrait"), "Portraits", "UnitPortrait", () => Context.Names(DatId.Portdata), "None", 300, jump: DatId.Portdata),
            ChoiceField(this, File, P("elevation_level"), "Elevation", "UnitElevationLevel", "ElevationLevels", 300),
            NumberField(this, File, P("unit_direction"), "Direction", "UnitDirection", 64)));

        string extents = P("unit_extents");
        string addon = P("addon_position");
        var preview = new PreviewBox();
        void Redraw()
        {
            var p = Context.Settings.Previews;
            preview.Show(p.Unit && Entry >= 0
                ? Context.Preview?.UnitGraphicsScene(Context.File, Context.ImagesTbl, Entry,
                    new UnitPreviewOptions(p.UnitPlacement, p.UnitDimensions, p.UnitAddon, p.AddonParent))
                : null);
        }
        var showAddon = PreviewBox.Toggle(this, "Show Addon Placement (Yellow) with parent building:",
            s => s.UnitAddon, (s, on) => s with { UnitAddon = on }, Redraw);
        var parent = PreviewBox.ParentBox(this, Redraw);
        var previewGroup = Group("Preview",
            preview.Root,
            PreviewBox.Toggle(this, "Show Preview", s => s.Unit, (s, on) => s with { Unit = on }, Redraw),
            Row(
                PreviewBox.Toggle(this, "Show StarEdit Placement Box (Red)", s => s.UnitPlacement, (s, on) => s with { UnitPlacement = on }, Redraw),
                PreviewBox.Toggle(this, "Show Dimensions Box (Green)", s => s.UnitDimensions, (s, on) => s with { UnitDimensions = on }, Redraw)),
            Row(showAddon, parent));
        AddLoader(entry =>
        {
            bool hasAddon = Context.File(DatId.Units)?.Covers(entry, addon) == true;
            showAddon.IsEnabled = hasAddon;
            parent.IsEnabled = hasAddon;
            Redraw();
        });

        var dimensions = new StackPanel { Spacing = 10 };
        dimensions.Children.Add(Group("Unit Dimensions",
                Dimension(extents, 0, "Left", "UnitDimLeft"),
                Dimension(extents, 2, "Right", "UnitDimRight"),
                Dimension(extents, 1, "Up", "UnitDimUp"),
                Dimension(extents, 3, "Down", "UnitDimDown")));
        dimensions.Children.Add(Group("Addon Position",
                Titled(this, PartField(this, File, addon, 0, "Horizontal", null), "Addons Horizontal Position", "UnitAddPosX"),
                Titled(this, PartField(this, File, addon, 1, "Vertical", null), "Addons Vertical Position", "UnitAddPosY")));
        Panel.Children.Add(Row(dimensions, previewGroup));
    }

    FrameworkElement Dimension(string property, int part, string label, string hintKey) =>
        Titled(this, PartField(this, File, property, part, label, null), label + " Dimension", hintKey);

}
