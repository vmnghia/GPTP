using Manifold.Core.Data;
using Manifold.Core.Editor;
using static ManifoldEditor.App.Dat.DatFields;

namespace ManifoldEditor.App.Dat.Tabs;

/// <summary>units.dat, Sounds (PyDAT's SoundsUnitsTab): the seven sound ranges, each a sound effect. Jump buttons are left out for now.</summary>
sealed class UnitsSoundsTab : DatTabBase
{
    public UnitsSoundsTab(DatContext context) : base(DatTabId.UnitsSounds, context)
    {
        Panel.Children.Add(Group("Sounds",
            Sound("ready_sound", "Ready", "UnitSndReady"),
            Sound("yes_sound_start", "Yes (First)", "UnitSndYesStart"),
            Sound("yes_sound_end", "Yes (Last)", "UnitSndYesEnd"),
            Sound("what_sound_start", "What (First)", "UnitSndWhatStart"),
            Sound("what_sound_end", "What (Last)", "UnitSndWhatEnd"),
            Sound("pissed_sound_start", "Annoyed (First)", "UnitSndAnnStart"),
            Sound("pissed_sound_end", "Annoyed (Last)", "UnitSndAnnEnd")));
    }

    Microsoft.UI.Xaml.FrameworkElement Sound(string property, string label, string hintKey) =>
        ReferenceField(this, File, P(property), label, hintKey, () => Context.Names(DatId.Sfxdata), null, 300);
}
