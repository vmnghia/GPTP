using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// sprites.dat (PyDAT's SpritesTab): the image, the two flags, the selection circle (stored
/// as the image's number minus 561, so the list is PyDAT's sizes), the health bar and the circle's
/// vertical position. The health bar and the circle are disabled for the first 130 sprites of a
/// vanilla file, which have none. PyDAT's preview shows the sprite's image over its selection
/// circle and health bar (where the file has them).
/// </summary>
sealed class SpritesTab : DatTabBase
{
    public SpritesTab(DatContext context) : base(DatTabId.Sprites, context)
    {
        Panel.Children.Add(Group("Sprite Properties",
            ReferenceField(this, File, P("image"), "Image", "SpriteImage", () => Context.Names(DatId.Images), null, 300, jump: DatId.Images),
            Row(
                CheckField(this, File, P("is_visible"), "Is Visible", "SpriteVisible"),
                CheckField(this, File, P("unused"), "Unused", "SpriteUnused")),
            Titled(this, ChoiceField(this, File, P("selection_circle_image"), "Sel. Circle", null, "SelCircleSize", 240, jump: DatId.Images, jumpOffset: 561),
                "Selection Circle", "SpriteSelCircle"),
            Row(
                NumberField(this, File, P("health_bar"), "Health Bar", "SpriteHPBar", 64),
                DerivedText(this, e => Boxes(ValueOf(Context, File, e, P("health_bar"))))),
            Titled(this, NumberField(this, File, P("selection_circle_offset"), "Vert. Position", null, 64),
                "Vertical Position", "SpriteCircleOff")));

        var preview = new PreviewBox();
        void Redraw() => preview.Show(Context.Settings.Previews.Sprite && Entry >= 0
            ? Context.Preview?.SpriteScene(Context.File, Context.ImagesTbl, Entry)
            : null);
        Panel.Children.Add(Group("Preview",
            preview.Root,
            PreviewBox.Toggle(this, "Show Preview", s => s.Sprite, (s, on) => s with { Sprite = on }, Redraw)));
        AddLoader(_ => Redraw());
    }

    /// <summary>PyDAT's boxes next to the health bar: (length - 1) / 3 rounded down, at least 1 ("= 4 boxes").</summary>
    static string Boxes(ulong? length) =>
        length is { } n ? $"= {(n <= 3 ? 1 : (n - 1) / 3)} boxes" : "";
}
