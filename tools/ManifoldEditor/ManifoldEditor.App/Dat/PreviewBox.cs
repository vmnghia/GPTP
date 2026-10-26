using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// PyDAT's preview canvas (previews spec §3): the 257x257 black square a <see cref="PreviewScene"/>
/// renders to, with its reasons in small text under it. The window only copies the pixels.
/// </summary>
sealed class PreviewBox
{
    readonly WriteableBitmap bitmap = new(DatPreview.Size, DatPreview.Size);
    readonly TextBlock reasons = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Width = DatPreview.Size };

    public FrameworkElement Root { get; }

    public PreviewBox()
    {
        var image = new Image { Source = bitmap, Stretch = Stretch.None, Width = DatPreview.Size, Height = DatPreview.Size };
        var square = new Border
        {
            Width = DatPreview.Size, Height = DatPreview.Size, Child = image, HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Black),
        };
        Root = new StackPanel { Spacing = 4, Children = { square, reasons } };
        Show(null);
    }

    /// <summary>Draws the scene; null (its Show box off, or nothing open) is the black square alone.</summary>
    public void Show(PreviewScene? scene)
    {
        scene ??= PreviewScene.Empty;
        // Little-endian ARGB words are B, G, R, A bytes: the bitmap's BGRA. Alpha is 0 or 255.
        byte[] bgra = MemoryMarshal.AsBytes(scene.Render().AsSpan()).ToArray();
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(bgra, 0, bgra.Length);
        }
        bitmap.Invalidate();
        reasons.Text = string.Join("\n", scene.Reasons);
        reasons.Visibility = scene.Reasons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One of PyDAT's preview checkboxes, kept in <see cref="EditorSettings.Previews"/>: its state is
    /// loaded on every <see cref="DatTabBase.Show"/>, and a click stores it and calls <paramref name="redraw"/>.
    /// </summary>
    public static CheckBox Toggle(DatTabBase tab, string label, Func<DatPreviewSettings, bool> get,
        Func<DatPreviewSettings, bool, DatPreviewSettings> set, Action redraw)
    {
        var box = new CheckBox { Content = label, MinWidth = 0 };
        box.Click += (_, _) =>
        {
            var settings = tab.Context.Settings;
            tab.Context.SetSettings(settings with { Previews = set(settings.Previews, box.IsChecked == true) });
            redraw();
        };
        tab.AddLoader(_ => box.IsChecked = get(tab.Context.Settings.Previews));
        return box;
    }

    /// <summary>
    /// The add-on preview's parent building (PyDAT's box after "Show Addon Placement"): a unit id,
    /// 0-65535, kept in <see cref="DatPreviewSettings.AddonParent"/>; Enter or leaving the box
    /// commits, and a value that isn't a number turns it red.
    /// </summary>
    public static TextBox ParentBox(DatTabBase tab, Action redraw)
    {
        var box = new TextBox { Width = 56, MinWidth = 0, FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        void Commit()
        {
            if (DatValues.ParseNumber(box.Text, DatType.Short) is not { } id)
            {
                box.BorderBrush = new SolidColorBrush(Colors.Red);
                ToolTipService.SetToolTip(box, "0-65535");
                return;
            }
            box.ClearValue(Control.BorderBrushProperty);
            ToolTipService.SetToolTip(box, null);
            var settings = tab.Context.Settings;
            if (settings.Previews.AddonParent == (int)id) return;
            tab.Context.SetSettings(settings with { Previews = settings.Previews with { AddonParent = (int)id } });
            redraw();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            Commit();
        };
        tab.AddLoader(_ =>
        {
            if (box.FocusState != FocusState.Unfocused) return;   // being typed in: leave it
            box.ClearValue(Control.BorderBrushProperty);
            box.Text = tab.Context.Settings.Previews.AddonParent.ToString(CultureInfo.InvariantCulture);
        });
        return box;
    }
}
