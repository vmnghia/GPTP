using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>
/// The fields of one string (spec §4), used by the button panel and the Strings page: hotkey,
/// tooltip type and text for a hotkey string, text alone otherwise, in PyTBL's &lt;N&gt; form;
/// the code list; the preview; and the lock on a string other buttons share (spec §5). It
/// decides nothing: a commit hands the new bytes to its owner, which edits through EditorState.
/// </summary>
sealed class StringEditorView
{
    public StackPanel Root { get; } = new() { Spacing = 6 };

    /// <summary>The string's new bytes, NUL-terminated, after an edit is committed.</summary>
    public event Action<byte[]>? Committed;
    public event Action? EditForAll;
    public event Action? MakeCopy;

    readonly TextBlock header = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly StackPanel sharedRow = new() { Spacing = 4 };
    readonly TextBlock sharedNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly StackPanel hotkeyRow = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly TextBox hotkey = new() { Header = "Hotkey", Width = 70 };
    readonly ComboBox type = new() { Header = "Tooltip type", MinWidth = 220 };
    readonly TextBox text = new() { Header = "Text (PyTBL form: <3> yellow, <10> newline)", TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
    readonly DropDownButton insert = new() { Content = "Insert code" };
    readonly TextBlock error = new() { Foreground = new SolidColorBrush(Colors.OrangeRed), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly StackPanel preview = new() { Spacing = 0 };

    uint[] colours = TextColors.Default;
    bool hotkeyMode;
    bool updating;
    string committedText = "", committedHotkey = "";
    int committedType = -1;

    readonly bool offerCopy;

    /// <param name="offerCopy">Offer Make a separate copy (for a button's field, not on the Strings page).</param>
    public StringEditorView(bool offerCopy)
    {
        this.offerCopy = offerCopy;
        var editForAll = new Button { Content = "Edit for all" };
        editForAll.Click += (_, _) => EditForAll?.Invoke();
        var makeCopy = new Button { Content = "Make a separate copy", Tag = "copy" };
        makeCopy.Click += (_, _) => MakeCopy?.Invoke();
        sharedRow.Children.Add(sharedNote);
        sharedRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { editForAll, makeCopy } });

        foreach (byte t in Enumerable.Range(0, HotkeyString.TypeLabels.Count)) type.Items.Add(HotkeyString.TypeLabel(t));
        hotkeyRow.Children.Add(hotkey);
        hotkeyRow.Children.Add(type);

        var codes = new MenuFlyout();
        foreach (var code in StringList.Codes)
        {
            var item = new MenuFlyoutItem { Text = code.ToString() };
            item.Click += (_, _) => Insert(code.Text);
            codes.Items.Add(item);
        }
        codes.Items.Add(new MenuFlyoutItem { Text = StringList.OverpowerNote, IsEnabled = false });
        insert.Flyout = codes;

        hotkey.LostFocus += (_, _) => Commit();
        text.LostFocus += (_, _) => Commit();
        hotkey.KeyDown += CommitOnEnter;
        text.KeyDown += CommitOnEnter;
        type.SelectionChanged += (_, _) => Commit();
        text.TextChanged += (_, _) => { if (!updating) DrawPreview(); };
        hotkey.TextChanged += (_, _) => { if (!updating) DrawPreview(); };

        Root.Children.Add(header);
        Root.Children.Add(sharedRow);
        Root.Children.Add(hotkeyRow);
        Root.Children.Add(text);
        Root.Children.Add(insert);
        Root.Children.Add(error);
        Root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Colors.Black), Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(4), Child = preview, MinHeight = 28,
        });
    }

    /// <summary>Nothing to edit (no string, or no stat_txt.tbl): only the message shows.</summary>
    public void ShowNone(string message)
    {
        header.Text = message;
        foreach (var child in Root.Children.Skip(1)) child.Visibility = Visibility.Collapsed;
    }

    /// <param name="sharedWith">Who else uses the string ("" when nobody).</param>
    /// <param name="locked">Shared and not yet unlocked: the fields are read-only.</param>
    public void Show(string title, byte[] segment, bool asHotkey, string sharedWith, bool locked, uint[] textColours)
    {
        updating = true;
        colours = textColours;
        header.Text = title;
        foreach (var child in Root.Children) child.Visibility = Visibility.Visible;
        sharedRow.Visibility = sharedWith.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        sharedNote.Text = locked ? $"Also used by {sharedWith}. Editing it changes them too." : $"Also used by {sharedWith}.";
        foreach (var button in sharedRow.Children.OfType<StackPanel>().SelectMany(p => p.Children).OfType<Button>())
            button.Visibility = locked && (offerCopy || button.Tag is not "copy") ? Visibility.Visible : Visibility.Collapsed;

        hotkeyMode = asHotkey && HotkeyString.Split(segment) is not null;
        hotkeyRow.Visibility = hotkeyMode ? Visibility.Visible : Visibility.Collapsed;
        if (hotkeyMode)
        {
            var parts = HotkeyString.Split(segment)!;
            hotkey.Text = committedHotkey = TblText.Decompile([parts.Hotkey]);
            if (parts.Type >= HotkeyString.TypeLabels.Count)
            {
                while (type.Items.Count > HotkeyString.TypeLabels.Count) type.Items.RemoveAt(type.Items.Count - 1);
                type.Items.Add(HotkeyString.TypeLabel(parts.Type));
                type.SelectedIndex = type.Items.Count - 1;
            }
            else type.SelectedIndex = parts.Type;
            committedType = parts.Type;
            text.Text = committedText = TblText.Decompile(parts.Text);
        }
        else
            text.Text = committedText = TblText.EditText(segment);

        hotkey.IsReadOnly = text.IsReadOnly = locked;
        type.IsEnabled = insert.IsEnabled = !locked;
        error.Text = "";
        DrawPreview();
        updating = false;
    }

    public void ShowError(string message) => error.Text = message;

    void CommitOnEnter(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        Commit();
    }

    void Insert(string code)
    {
        if (text.IsReadOnly) return;
        int at = Math.Clamp(text.SelectionStart, 0, text.Text.Length);
        text.Text = text.Text.Insert(at, code);
        text.SelectionStart = at + code.Length;
        Commit();
        text.Focus(FocusState.Programmatic);
    }

    byte TypeValue() =>
        type.SelectedIndex >= 0 && type.SelectedIndex < HotkeyString.TypeLabels.Count ? (byte)type.SelectedIndex : (byte)committedType;

    /// <summary>The bytes the fields hold, or null with the reason shown.</summary>
    byte[]? Bytes()
    {
        var body = TblText.Compile(text.Text, out var problem);
        if (body is null) { error.Text = problem; return null; }
        if (!hotkeyMode) return [.. body, 0];
        var key = TblText.Compile(hotkey.Text, out problem);
        if (key is null || key.Length != 1 || key[0] == 0)
        {
            error.Text = problem ?? "The hotkey is one key: a letter, or <27> for Esc.";
            return null;
        }
        return new HotkeyString(key[0], TypeValue(), body).Join();
    }

    void Commit()
    {
        if (updating || text.IsReadOnly) return;
        if (text.Text == committedText && hotkey.Text == committedHotkey && (!hotkeyMode || TypeValue() == committedType)) return;
        error.Text = "";
        if (Bytes() is not { } bytes) return;
        committedText = text.Text;
        committedHotkey = hotkey.Text;
        committedType = TypeValue();
        Committed?.Invoke(bytes);
    }

    void DrawPreview()
    {
        preview.Children.Clear();
        var body = TblText.Compile(text.Text, out _) ?? [];
        foreach (var line in TooltipPreview.Build(body, hotkeyMode ? TypeValue() : null, colours))
        {
            var block = new TextBlock
            {
                FontFamily = new FontFamily("Arial"), FontSize = 12, TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = line.Align switch
                {
                    LineAlign.Right => HorizontalAlignment.Right,
                    LineAlign.Center => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                },
            };
            foreach (var run in line.Runs)
                block.Inlines.Add(new Run
                {
                    Text = run.Text,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(run.Argb >> 24), (byte)(run.Argb >> 16),
                        (byte)(run.Argb >> 8), (byte)run.Argb)),
                });
            if (line.Runs.Count == 0) block.Text = " ";
            preview.Children.Add(block);
        }
    }
}
