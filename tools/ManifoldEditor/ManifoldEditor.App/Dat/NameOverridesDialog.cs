using System.Globalization;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// PyDAT's Name Overrides dialog for one .dat file (links spec §6): the overrides list
/// (<c>" {id,5} {+| } {name}"</c>), ID, Name, Append, Update and Remove, and Open... and
/// Save As... of a PyDAT <c>.txt</c>. It edits a copy: OK returns it (the caller records one
/// undo step), Cancel drops it. One instance per showing.
/// </summary>
/// <param name="entry">The entry the dialog opens on (the list's selected one).</param>
sealed class NameOverridesDialog(XamlRoot root, nint windowHandle, DatId file, NameOverrides current, int entry)
{
    /// <summary>The highest id an override file can hold (PyMS's <c>\d{1,5}</c>).</summary>
    const int MaxId = 99999;

    readonly ListView list = new()
    {
        SelectionMode = ListViewSelectionMode.Single, FontFamily = new FontFamily("Consolas"), Height = 220,
    };
    readonly TextBox id = new() { Width = 80, FontFamily = new FontFamily("Consolas"), PlaceholderText = "0" };
    readonly TextBox name = new() { MinWidth = 180, PlaceholderText = "Name" };
    readonly CheckBox append = new() { Content = "Append", MinWidth = 0 };
    readonly TextBlock note = new()
    {
        TextWrapping = TextWrapping.Wrap, FontSize = 12, IsTextSelectionEnabled = true, Visibility = Visibility.Collapsed,
    };
    NameOverrides working = current;
    int[] ids = [];
    /// <summary>True while the dialog fills the list: its selection event is the dialog's own.</summary>
    bool filling;

    /// <summary>The overrides as the dialog left them on OK; null on Cancel.</summary>
    public async Task<NameOverrides?> ShowAsync()
    {
        var open = new Button { Content = "Open..." };
        open.Click += async (_, _) => await Open();
        var saveAs = new Button { Content = "Save As..." };
        saveAs.Click += async (_, _) => await SaveAs();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { open, saveAs } };

        list.SelectionChanged += (_, _) =>
        {
            if (filling || list.SelectedIndex < 0 || list.SelectedIndex >= ids.Length) return;
            ShowOverride(ids[list.SelectedIndex]);
        };

        var update = new Button { Content = "Update" };
        update.Click += (_, _) => Update();
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) => Remove();
        // PyDAT's Return: Update (the dialog has no default button, so Enter doesn't close it).
        id.KeyDown += EnterUpdates;
        name.KeyDown += EnterUpdates;
        id.TextChanged += (_, _) => ClearIdError();

        var fields = new Grid { ColumnSpacing = 6 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        Place(fields, Label("ID:"), 0);
        Place(fields, id, 1);
        Place(fields, Label("Name:"), 2);
        Place(fields, name, 3);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { append, update, remove } };

        var body = new StackPanel { Spacing = 8, MinWidth = 480, Children = { tools, list, fields, actions, note } };

        id.Text = entry.ToString(CultureInfo.InvariantCulture);
        Fill(select: entry);
        if (working.Entries.TryGetValue(entry, out var own))
        {
            name.Text = own.Name;
            append.IsChecked = own.Append;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"{DatNames.TypeName(file)} Name Overrides",
            Content = new ScrollViewer { Content = body, MaxHeight = 560 },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.None,
        };
        // As PyDAT's: the Name box has the focus, its caret at the end.
        dialog.Opened += (_, _) =>
        {
            name.Focus(FocusState.Programmatic);
            name.SelectionStart = name.Text.Length;
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? working : null;
    }

    // -------- The list --------

    /// <summary>The list from <see cref="working"/>, the override of <paramref name="select"/> selected when there is one.</summary>
    void Fill(int? select)
    {
        filling = true;
        try
        {
            ids = working.Entries.Keys.ToArray();
            list.ItemsSource = working.Entries
                .Select(e => $" {e.Key,5} {(e.Value.Append ? '+' : ' ')} {e.Value.Name}")
                .ToList();
            int index = select is int s ? Array.IndexOf(ids, s) : -1;
            list.SelectedIndex = index;
            if (index >= 0) list.ScrollIntoView(list.Items[index]);
        }
        finally
        {
            filling = false;
        }
    }

    void ShowOverride(int overrideId)
    {
        var (isAppend, text) = working.Entries[overrideId];
        id.Text = overrideId.ToString(CultureInfo.InvariantCulture);
        name.Text = text;
        append.IsChecked = isAppend;
    }

    // -------- Update, Remove --------

    void EnterUpdates(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        Update();
    }

    /// <summary>PyDAT's Update: the ID's override set to Name and Append; an empty Name removes it.</summary>
    void Update()
    {
        if (Id() is not int at) return;
        working = working.With(at, append.IsChecked == true, name.Text);
        Fill(select: at);
    }

    void Remove()
    {
        if (Id() is not int at) return;
        working = working.With(at, false, "");
        Fill(select: null);
    }

    /// <summary>The ID box's id (decimal, or hex after 0x), 0-99999; null, and the box marked, when it isn't one.</summary>
    int? Id()
    {
        if (ParsedId() is int value) return value;
        id.BorderBrush = new SolidColorBrush(Colors.Red);
        ToolTipService.SetToolTip(id, $"0-{MaxId}, or 0x for hex");
        return null;
    }

    int? ParsedId() => DatValues.ParseId(id.Text) is ulong value && value <= MaxId ? (int)value : null;

    void ClearIdError()
    {
        id.ClearValue(Control.BorderBrushProperty);
        ToolTipService.SetToolTip(id, null);
    }

    // -------- Open..., Save As... --------

    /// <summary>The .dat's override file name: units.txt.</summary>
    string FileName => Path.GetFileNameWithoutExtension(DatFormats.Of(file).FileName) + ".txt";

    /// <summary>PyDAT's Open: this file's overrides replaced by the file's; lines that aren't overrides are noted and skipped.</summary>
    async Task Open()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        string path;
        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            if (await picker.PickSingleFileAsync() is not { } picked) return;
            path = picked.Path;
        }
        catch (Exception e)
        {
            ShowNote($"Couldn't choose a file: {e.Message}", error: true);
            return;
        }
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            ShowNote($"Couldn't open name overrides '{path}': {e.Message}", error: true);
            return;
        }
        var notes = new List<string>();
        working = NameOverrides.FromBytes(bytes, notes, Path.GetFileName(path));
        Fill(select: ParsedId());
        if (notes.Count == 0) ShowNote($"Opened {Path.GetFileName(path)}: {working.Entries.Count} overrides.", error: false);
        else ShowNote($"Opened {Path.GetFileName(path)}; skipped:\n" + string.Join("\n", notes.Take(20)) +
            (notes.Count > 20 ? $"\nand {notes.Count - 20} more" : ""), error: true);
    }

    /// <summary>PyDAT's Save As: the overrides as the dialog holds them, to a .txt in the same format.</summary>
    async Task SaveAs()
    {
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(FileName) };
        picker.FileTypeChoices.Add("Name overrides", [".txt"]);
        string path;
        try
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            if (await picker.PickSaveFileAsync() is not { } picked) return;
            path = picked.Path;
        }
        catch (Exception e)
        {
            ShowNote($"Couldn't choose a file: {e.Message}", error: true);
            return;
        }
        try
        {
            TextFiles.Write(path, working.ToBytes());
            ShowNote($"Saved {working.Entries.Count} overrides to {Path.GetFileName(path)}.", error: false);
        }
        catch (Exception e)
        {
            ShowNote($"Couldn't save name overrides to '{path}': {e.Message}", error: true);
        }
    }

    /// <summary>Inside the dialog: a second ContentDialog can't open over this one.</summary>
    void ShowNote(string text, bool error)
    {
        note.Text = text;
        if (error) note.Foreground = new SolidColorBrush(Colors.IndianRed);
        else note.ClearValue(TextBlock.ForegroundProperty);
        note.Visibility = Visibility.Visible;
    }

    // -------- Pieces --------

    static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    static void Place(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}

/// <summary>Text files the Data page writes outside the exe (Export to TXT, the overrides' Save As...).</summary>
static class TextFiles
{
    /// <summary>
    /// Writes a temporary file beside <paramref name="path"/>, then puts it in the file's place
    /// (no .bak): a failure leaves an old file whole.
    /// </summary>
    public static void Write(string path, byte[] bytes)
    {
        string temp = path + ".saving";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
