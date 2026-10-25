using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml.Controls;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// weapons.dat (PyDAT's WeaponsTab): damage properties, the two "Weapon Display" groups (PyDAT
/// titles both so), range, splash radii and the target flags. The cooldown also shows in
/// seconds. The graphics and the damage upgrade have Jump buttons. The icon preview and the icon picker are left out for now.
/// </summary>
sealed class WeaponsTab : DatTabBase
{
    readonly TblChoices statTxt;

    public WeaponsTab(DatContext context) : base(DatTabId.Weapons, context)
    {
        statTxt = new TblChoices(() => Context.Opened()?.State.Strings);

        var damage = Group("Damage Properties",
            Row(
                Titled(this, NumberField(this, File, P("damage_amount"), "Amount", null, 64), "Damage Amount", "WeapDamageAmount"),
                Titled(this, ChoiceField(this, File, P("weapon_type"), "Type", null, "DamTypes", 160), "Damage Type", "WeapDamageType")),
            Row(
                Titled(this, NumberField(this, File, P("damage_bonus"), "Bonus", null, 64), "Damage Bonus", "WeapDamageBonus"),
                Titled(this, ChoiceField(this, File, P("explosion_type"), "Explosion", null, "Explosions", 160), "Explosion", "WeapDamageExplosion")),
            Row(
                Titled(this, NumberField(this, File, P("damage_factor"), "Factor", null, 64), "Damage Factor", "WeapDamageFactor"),
                Titled(this, ReferenceField(this, File, P("unused_technology"), "Unused", null,
                    () => Context.Names(DatId.Techdata), "None", 160), "Unused Technology", "WeapUnused")),
            Row(
                Titled(this, NumberField(this, File, P("weapon_cooldown"), "Cooldown", null, 64), "Cooldown", "WeapDamageCooldown"),
                DerivedText(this, e => Seconds(ValueOf(Context, File, e, P("weapon_cooldown"))))),
            Titled(this, ReferenceField(this, File, P("damage_upgrade"), "Upgrade", null,
                () => Context.Names(DatId.Upgrades), "None", 220, jump: DatId.Upgrades), "Damage Upgrade", "WeapDamageUpgrade"));

        var display = Group("Weapon Display",
            Titled(this, ReferenceField(this, File, P("label"), "Label", null, statTxt.WithNone, null, 300), "Label", "WeapLabel"),
            Titled(this, ReferenceField(this, File, P("target_error_message"), "Error Msg", null, statTxt.WithNone, null, 300),
                "Error Message", "WeapError"));

        var graphics = Group("Weapon Display",
            ChoiceField(this, File, P("weapon_behavior"), "Behaviour", "WeapBehaviour", "Behaviours", 240),
            NumberField(this, File, P("remove_after"), "Remove After", "WeapRemoveAfter", 64),
            ReferenceField(this, File, P("graphics"), "Graphics", "WeapGraphics", () => Context.Names(DatId.Flingy), null, 300, jump: DatId.Flingy),
            ReferenceField(this, File, P("icon"), "Icon", "WeapIcon", () => Context.Lists["Icons"], null, 300),
            Row(
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        NumberField(this, File, P("forward_offset"), "X Offset", "WeapOffsetForward", 64),
                        NumberField(this, File, P("upward_offset"), "Y Offset", "WeapOffsetUpward", 64),
                    },
                },
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        NumberField(this, File, P("attack_angle"), "Attack Angle", "WeapAttackAngle", 64),
                        NumberField(this, File, P("launch_spin"), "Launch Spin", "WeapLaunchSpin", 64),
                    },
                }));

        var range = Group("Weapon Range",
            Titled(this, NumberField(this, File, P("minimum_range"), "Min", null, 100), "Minimum Range", "WeapRangeMin"),
            Titled(this, NumberField(this, File, P("maximum_range"), "Max", null, 100), "Maximum Range", "WeapRangeMax"));

        var splash = Group("Splash Radii",
            Titled(this, NumberField(this, File, P("inner_splash_range"), "Inner", null, 64), "Inner Radius", "WeapSplashInner"),
            Titled(this, NumberField(this, File, P("medium_splash_range"), "Medium", null, 64), "Medium Radius", "WeapSplashMed"),
            Titled(this, NumberField(this, File, P("outer_splash_range"), "Outer", null, 64), "Outer Radius", "WeapSplashOuter"));

        var targets = Group("Target Flags",
            FlagsField(this, File, P("target_flags"), new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Air", 0x001, "WeapTarget001"),
                ("Ground", 0x002, "WeapTarget002"),
                ("Mechanical", 0x004, "WeapTarget004"),
                ("Organic", 0x008, "WeapTarget008"),
                ("Non-Building", 0x010, "WeapTarget010"),
                ("Non-Robotic", 0x020, "WeapTarget020"),
                ("Terrain", 0x040, "WeapTarget040"),
                ("Org. or Mech.", 0x080, "WeapTarget080"),
                ("Own", 0x100, "WeapTarget100"),
            }, 1));

        var left = new StackPanel { Spacing = 10 };
        left.Children.Add(damage);
        left.Children.Add(display);
        left.Children.Add(graphics);
        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(range);
        right.Children.Add(splash);
        right.Children.Add(targets);
        Panel.Children.Add(Row(left, right));
    }
}
