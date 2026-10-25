using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// units.dat, Graphics (PyDAT's GraphicsUnitsTab): the sprite graphics, the unit dimensions
/// (extents: left, up, right, down) and the addon position. The preview and the jump buttons
/// are left out for now.
/// </summary>
sealed class UnitsGraphicsTab : DatTabBase
{
    public UnitsGraphicsTab(DatContext context) : base(DatTabId.UnitsGraphics, context)
    {
        Panel.Children.Add(Group("Sprite Graphics",
            ReferenceField(this, File, P("graphics"), "Graphics", "UnitGfx", () => Context.Names(DatId.Flingy), null, 300),
            ReferenceField(this, File, P("construction_animation"), "Construction", "UnitConstruction",
                () => Context.Names(DatId.Images), null, 300),
            ReferenceField(this, File, P("portrait"), "Portraits", "UnitPortrait", () => Context.Names(DatId.Portdata), "None", 300, noneValue: 65535),
            ChoiceField(this, File, P("elevation_level"), "Elevation", "UnitElevationLevel", "ElevationLevels", 300),
            NumberField(this, File, P("unit_direction"), "Direction", "UnitDirection", 64)));

        string extents = P("unit_extents");
        string addon = P("addon_position");
        Panel.Children.Add(Row(
            Group("Unit Dimensions",
                Dimension(extents, 0, "Left", "UnitDimLeft"),
                Dimension(extents, 2, "Right", "UnitDimRight"),
                Dimension(extents, 1, "Up", "UnitDimUp"),
                Dimension(extents, 3, "Down", "UnitDimDown")),
            Group("Addon Position",
                Titled(this, PartField(this, File, addon, 0, "Horizontal", null), "Addons Horizontal Position", "UnitAddPosX"),
                Titled(this, PartField(this, File, addon, 1, "Vertical", null), "Addons Vertical Position", "UnitAddPosY"))));
    }

    FrameworkElement Dimension(string property, int part, string label, string hintKey) =>
        Titled(this, PartField(this, File, property, part, label, null), label + " Dimension", hintKey);

}
