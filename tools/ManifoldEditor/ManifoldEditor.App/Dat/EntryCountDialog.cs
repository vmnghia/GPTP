using System.Globalization;
using Manifold.Core.Data;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// PyDAT's EntryCountDialog (counts spec §3): how many entries the file should have, its
/// format's constraints, and the count PyMS's rounding gives. OK refuses fewer entries than
/// now (PyDAT's message, the box set back) and a count past the maximum, in red in the dialog
/// (a second dialog can't open over it).
/// </summary>
sealed class EntryCountDialog
{
    readonly ContentDialog dialog;
    readonly DatFile dat;
    readonly TextBox box;
    readonly TextBlock resulting = new();
    readonly TextBlock refusal = new() { Foreground = new SolidColorBrush(Colors.Red), TextWrapping = TextWrapping.Wrap };
    long? result;
    // The text OK put back in the box: its TextChanged comes later and must keep the refusal shown.
    string? putBack;

    public EntryCountDialog(XamlRoot root, DatFile dat)
    {
        this.dat = dat;
        var format = dat.Format;
        box = new TextBox { Text = Text(dat.Count), FontFamily = new FontFamily("Consolas"), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var constraints = new List<string>();
        if (format.ExpandedMin is { } min) constraints.Add($" - Minimum expanded entries: {Text(min)}");
        if (format.ExpandedMax is { } max) constraints.Add($" - Maximum expanded entries: {Text(max)}");
        if (format.ExpandedMultiple is { } step) constraints.Add($" - Expanded entries multiple: {Text(step)}");
        if (format.Reserved is { Count: > 0 }) constraints.Add(" - Some entries are reserved");

        var content = new StackPanel { Spacing = 8, MinWidth = 340 };
        content.Children.Add(new TextBlock { Text = $"How many entries for {format.FileName}?" });
        content.Children.Add(box);
        if (constraints.Count > 0)
            content.Children.Add(new TextBlock { Text = "Constraints:\n" + string.Join("\n", constraints), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(resulting);
        content.Children.Add(refusal);

        dialog = new ContentDialog
        {
            XamlRoot = root, Title = "How many entries?", Content = content,
            PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        box.TextChanged += (_, _) => ShowResulting();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (Parse(box.Text) is not { } wanted)
            {
                args.Cancel = true;
                refusal.Text = "The entry count is a whole number.";
                return;
            }
            if (dat.CountRefusal(wanted) is { } refused)
            {
                args.Cancel = true;
                bool fewer = wanted < dat.Count;
                if (fewer) box.Text = putBack = Text(dat.Count);
                refusal.Text = fewer ? $"The entry count can't be set to less than the current entries ({Text(dat.Count)})." : refused + ".";
                return;
            }
            result = wanted;
        };
        ShowResulting();
    }

    /// <summary>The count asked for (OK), or null (Cancel).</summary>
    public async Task<long?> ShowAsync()
    {
        await dialog.ShowAsync();
        return result;
    }

    void ShowResulting()
    {
        if (box.Text != putBack) refusal.Text = "";
        putBack = null;
        if (Parse(box.Text) is not { } n) { resulting.Text = "Resulting entry count: -"; return; }
        resulting.Text = n <= dat.Count ? $"Resulting entry count: {Text(dat.Count)}"
            : dat.ExpandedCount(n) is { } count ? $"Resulting entry count: {Text(count)}"
            : $"Resulting entry count: past the maximum ({Text(dat.Format.ExpandedMax ?? dat.Count)})";
    }

    /// <summary>A count typed in decimal digits; a number too long for a long is past any maximum (long.MaxValue). Null for other text.</summary>
    static long? Parse(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || !text.All(char.IsAsciiDigit)) return null;
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : long.MaxValue;
    }

    static string Text(long n) => n.ToString(CultureInfo.InvariantCulture);
}
