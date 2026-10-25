using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// sfxdata.dat (PyDAT's SoundsTab): the sound file (a sfxdata.tbl string, "None" at 0), the general
/// properties and the four flags. The play button and the "Used by" lists are left out for now (sound
/// playback is part 3).
/// </summary>
sealed class SoundsTab : DatTabBase
{
    readonly TblChoices sfxdataTbl;

    public SoundsTab(DatContext context) : base(DatTabId.Sounds, context)
    {
        sfxdataTbl = new TblChoices(() => Context.Opened()?.Resources.SfxdataTbl);

        Panel.Children.Add(Group("Sound",
            ReferenceField(this, File, P("sound_file"), "Sound File", "SoundFile", sfxdataTbl.WithNone, null, 300)));

        var general = Group("General Properties",
            NumberField(this, File, P("priority"), "Priority", "SoundPriority", 64),
            NumberField(this, File, P("portrait_length_adjust"), "Portrait Length Adjust", "SoundPortraitLengthAdjust", 64),
            NumberField(this, File, P("minimum_volume"), "Minimum Volume %", "SoundMinimumVolume", 64));

        var flags = Group("Flags",
            FlagsField(this, File, P("flags"), new (string Label, ulong Mask, string? HintKey)[]
            {
                ("Preload", 1UL << 0, "SoundPreload"),
                ("Unit Speech", 1UL << 1, "SoundUnitSpeech"),
                ("One at a Time", 1UL << 4, "SoundOneAtATime"),
                ("Never Preempt", 1UL << 5, "SoundNeverPreempt"),
            }, 1));
        Panel.Children.Add(Row(general, flags));
    }
}
