using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// flingy.dat (PyDAT's FlingyTab): the sprite, top speed and halt distance (each also shown in
/// pixels), acceleration, turn radius, move control and the iscript mask. The sprite has PyDAT's Jump button.
/// </summary>
sealed class FlingyTab : DatTabBase
{
    public FlingyTab(DatContext context) : base(DatTabId.Flingy, context)
    {
        Panel.Children.Add(Group("Flingy Properties",
            ReferenceField(this, File, P("sprite"), "Sprite", "FlingySprite", () => Context.Names(DatId.Sprites), null, 300, jump: DatId.Sprites),
            Row(
                NumberField(this, File, P("speed"), "Top Speed", "FlingySpeed", 100),
                DerivedText(this, e => Speed(ValueOf(Context, File, e, P("speed"))))),
            NumberField(this, File, P("acceleration"), "Acceleration", "FlingyAcceleration", 100),
            Row(
                NumberField(this, File, P("halt_distance"), "Halt Distance", "FlingyHaltDist", 100),
                DerivedText(this, e => HaltDistance(ValueOf(Context, File, e, P("halt_distance"))))),
            NumberField(this, File, P("turn_radius"), "Turn Radius", "FlingyTurnRad", 100),
            ChoiceField(this, File, P("movement_control"), "Move Control", "FlingyMoveControl", "FlingyControl", 200),
            NumberField(this, File, P("iscript_mask"), "IScript Mask", "FlingyUnused", 100)));
    }
}
