using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>
/// sfxdata.dat (PyDAT's SoundsTab): the sound file (a sfxdata.tbl string, "None" at 0), the general
/// properties and the four flags. The page's "Used by" list covers the tab. PyDAT's play button beside the sound
/// file plays sound\&lt;the sfxdata.tbl string&gt; from the exe's MPQ, then vanilla; it is disabled for "No sound"
/// and for a file that isn't found, and a sound Windows can't play says why beside it.
/// </summary>
sealed class SoundsTab : DatTabBase
{
    readonly TblChoices sfxdataTbl;

    public SoundsTab(DatContext context) : base(DatTabId.Sounds, context)
    {
        sfxdataTbl = new TblChoices(() => Context.Opened()?.Resources.SfxdataTbl);

        var sound = (StackPanel)ReferenceField(this, File, P("sound_file"), "Sound File", "SoundFile", sfxdataTbl.WithNone, null, 300);
        var play = new Microsoft.UI.Xaml.Controls.Button { Content = "\u25B6", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        var failure = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Colors.OrangeRed) };
        string? path = null;
        byte[]? wav = null;
        bool listening = false;
        play.Click += async (_, _) =>
        {
            failure.Text = "";
            if (wav is null) return;
            // The player is made on the first play, not when the page is built.
            if (!listening) { Context.Sounds.Failed += reason => failure.Text = $"Can't play {path}: {reason}"; listening = true; }
            try { await Context.Sounds.Play(wav); }
            catch (Exception e) { failure.Text = $"Can't play {path}: {e.Message}"; }
        };
        AddLoader(entry =>
        {
            failure.Text = "";
            var resources = Context.Opened()?.Resources;
            path = Context.File(DatId.Sfxdata) is { } sfxdata ? DatPreview.SoundPath(sfxdata, resources?.SfxdataTbl, entry) : null;
            wav = path is null ? null : resources?.Resolver?.Find(path)?.Data;
            play.IsEnabled = wav is not null;
            ToolTipService.SetToolTip(play, path is null ? "No sound" : wav is null ? $"{path} not found" : path);
        });
        sound.Children.Add(play);
        sound.Children.Add(failure);
        Panel.Children.Add(Group("Sound", sound));

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
