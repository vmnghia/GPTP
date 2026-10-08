using ManifoldEditor.App.Services;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Storage.Pickers;
using Windows.System;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>
/// The editor window (spec §5), built in code. It only wires controls to EditorState
/// (Manifold.Core), where every command and every piece of text it shows is decided and
/// tested; after a command it redraws from the state.
/// </summary>
public sealed class MainWindow : Window
{
    const double CellSize = 64;

    EditorSettings settings;
    OpenedExe? opened;
    IconCache? icons;
    IReadOnlyList<string> iconNames = Array.Empty<string>();

    ushort selectedPosition;          // 0: no cell selected, all buttons listed
    int selectedButton = -1;          // index in the set, -1: none
    ushort dragFrom;
    bool updating;                    // true while controls are filled from the state
    bool closeConfirmed;

    readonly TextBox search = new() { PlaceholderText = "Search sets (name or id)" };
    readonly ListView setList = new() { SelectionMode = ListViewSelectionMode.Single };
    readonly Dictionary<int, ListViewItem> setItems = new();
    readonly TextBlock setTitle = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
    readonly Grid card = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly ListView buttonList = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 240 };
    readonly TextBlock buttonListTitle = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
    readonly TextBlock checks = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Button pasteSetButton = new() { Content = "Paste set" };

    // The button panel.
    readonly StackPanel panel = new() { Spacing = 8 };
    readonly TextBlock panelTitle = new() { FontWeight = FontWeights.SemiBold };
    readonly Button iconButton = new() { MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Left };
    readonly GridView iconGrid = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None, Width = 560, Height = 420 };
    readonly ComboBox conditionBox = new() { Header = "Condition", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox actionBox = new() { Header = "Action", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumberBox conditionVar = Number("Condition var");
    readonly NumberBox actionVar = Number("Action var");
    readonly NumberBox enabledString = Number("Enabled string id (the tooltip, with the hotkey)");
    readonly NumberBox disabledString = Number("Disabled string id");
    readonly StringEditorView enabledEditor = new(offerCopy: true);
    readonly StringEditorView disabledEditor = new(offerCopy: true);
    readonly Button enabledNew = new() { Content = "New string" };
    readonly Button disabledNew = new() { Content = "New string" };
    /// <summary>Shared strings the user chose to edit for every button using them (Edit for all).</summary>
    readonly HashSet<int> unlockedStrings = new();

    // The pages: button sets, strings (spec §6).
    readonly SelectorBar pages = new();
    readonly SelectorBarItem setsPageItem = new() { Text = "Button sets", IsSelected = true };
    readonly SelectorBarItem stringsPageItem = new() { Text = "Strings" };
    Grid setsPage = null!;
    StringsPage stringsPage = null!;
    StringTable? catalogStrings;

    public MainWindow()
    {
        settings = Workspace.LoadSettings();
        Title = "Manifold Editor";
        Content = BuildLayout();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 860));
        AppWindow.Closing += OnClosing;
        iconNames = Workspace.IconNames();
        ShowNothingOpen();
        // Reopen the last exe once the window is up, so a dialog has somewhere to show.
        ((FrameworkElement)Content).Loaded += async (_, _) =>
        {
            if (opened is null && settings.LastExe is { } last && File.Exists(last))
                await OpenExe(last);
        };
    }

    // -------- Layout --------

    static NumberBox Number(string header) => new()
    {
        Header = header, Minimum = 0, Maximum = ushort.MaxValue, SmallChange = 1,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
    };

    static TextBlock Small() => new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

    static Button ToolButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += click;
        return button;
    }

    UIElement BuildLayout()
    {
        var root = new Grid { Padding = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        toolbar.Children.Add(ToolButton("Open exe...", async (_, _) => await PickExe()));
        toolbar.Children.Add(ToolButton("Save (Ctrl+S)", async (_, _) => await Save()));
        toolbar.Children.Add(ToolButton("Undo (Ctrl+Z)", (_, _) => Undo()));
        toolbar.Children.Add(ToolButton("Redo (Ctrl+Y)", (_, _) => Redo()));
        toolbar.Children.Add(ToolButton("StarCraft folder...", async (_, _) => await PickStarCraftDir()));
        root.Children.Add(toolbar);

        pages.Items.Add(setsPageItem);
        pages.Items.Add(stringsPageItem);
        pages.SelectionChanged += (_, _) => ShowPage();
        pages.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(pages, 1);
        root.Children.Add(pages);

        stringsPage = new StringsPage(() => opened, Refresh, (title, text) => Message(title, text), JumpToButton);
        Grid.SetRow(stringsPage.Root, 2);
        stringsPage.Root.Visibility = Visibility.Collapsed;
        root.Children.Add(stringsPage.Root);

        var body = new Grid { ColumnSpacing = 16 };
        setsPage = body;
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        // Set list.
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        search.TextChanged += (_, _) => FillSetList();
        left.Children.Add(search);
        Grid.SetRow(setList, 1);
        setList.SelectionChanged += OnSetSelected;
        left.Children.Add(setList);
        body.Children.Add(left);

        // The card and what goes with it.
        var middle = new StackPanel();
        middle.Children.Add(setTitle);
        for (int r = 0; r < CardLayout.Rows; r++)
            card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellSize + 6) });
        for (int c = 0; c < CardLayout.Columns; c++)
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellSize + 6) });
        middle.Children.Add(card);

        var setCommands = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        setCommands.Children.Add(ToolButton("Copy set", (_, _) => { opened?.State.CopySet(); Refresh(); }));
        pasteSetButton.Margin = new Thickness(0, 0, 6, 0);
        pasteSetButton.Click += (_, _) => Run(s => s.PasteSet());
        setCommands.Children.Add(pasteSetButton);
        var revert = new MenuFlyout();
        revert.Items.Add(Item("To the version opened", () => Run(s => s.Revert(toVanilla: false))));
        revert.Items.Add(Item("To vanilla", () => Run(s => s.Revert(toVanilla: true))));
        setCommands.Children.Add(new DropDownButton { Content = "Revert set", Flyout = revert });
        middle.Children.Add(setCommands);

        middle.Children.Add(buttonListTitle);
        buttonList.SelectionChanged += OnButtonSelected;
        middle.Children.Add(buttonList);
        middle.Children.Add(checks);
        Grid.SetColumn(middle, 1);
        body.Children.Add(new ScrollViewer { Content = middle }.Also(v => Grid.SetColumn(v, 1)));

        // Button panel.
        panel.Children.Add(panelTitle);
        iconGrid.ItemClick += OnIconPicked;
        iconButton.Flyout = new Flyout { Content = iconGrid };
        iconButton.Flyout.Opening += (_, _) => FillIconGrid();
        panel.Children.Add(new TextBlock { Text = "Icon" });
        panel.Children.Add(iconButton);
        conditionBox.SelectionChanged += (_, _) => EditSelected(b => b with { Condition = Pick(conditionBox, opened!.State.Conditions, b.Condition) });
        actionBox.SelectionChanged += (_, _) => EditSelected(b => b with { Action = Pick(actionBox, opened!.State.Actions, b.Action) });
        panel.Children.Add(conditionBox);
        panel.Children.Add(actionBox);
        conditionVar.ValueChanged += (_, _) => EditSelected(b => b with { ConditionVar = Value(conditionVar) });
        actionVar.ValueChanged += (_, _) => EditSelected(b => b with { ActionVar = Value(actionVar) });
        enabledString.ValueChanged += (_, _) => EditSelected(b => b with { EnabledString = Value(enabledString) });
        disabledString.ValueChanged += (_, _) => EditSelected(b => b with { DisabledString = Value(disabledString) });
        panel.Children.Add(conditionVar);
        panel.Children.Add(actionVar);
        panel.Children.Add(StringRow(StringField.Enabled, enabledString, enabledNew, enabledEditor));
        panel.Children.Add(StringRow(StringField.Disabled, disabledString, disabledNew, disabledEditor));
        var right = new ScrollViewer { Content = panel };
        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        Grid.SetRow(status, 3);
        status.Margin = new Thickness(0, 10, 0, 0);
        root.Children.Add(status);

        // The toolbar already names the shortcuts; no floating accelerator tooltips.
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        root.KeyboardAccelerators.Add(Accelerator(VirtualKey.S, async () => await Save()));
        root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Z, Undo));
        root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Y, Redo));
        return root;
    }

    /// <summary>
    /// A button's string field in the panel: the id, Pick... and New string, then the
    /// string's own fields (spec §5).
    /// </summary>
    FrameworkElement StringRow(StringField field, NumberBox id, Button newString, StringEditorView editor)
    {
        var pick = new Button { Content = "Pick..." };
        pick.Flyout = PickFlyout(field);
        newString.Click += async (_, _) =>
        {
            if (opened is null || selectedButton < 0) return;
            if (opened.State.NewStringForButton(selectedButton, field, out var refused) is int) Refresh();
            else if (refused is not null) await Message("No new string", refused);
        };
        editor.Committed += bytes =>
        {
            if (opened is null || selectedButton < 0) return;
            ushort stringId = StringUses.IdOf(opened.State.SelectedSet.Buttons[selectedButton], field);
            if (opened.State.EditString(stringId, bytes, out var refused)) Refresh();
            else if (refused is not null) editor.ShowError(refused);
        };
        editor.EditForAll += () =>
        {
            if (opened is null || selectedButton < 0) return;
            unlockedStrings.Add(StringUses.IdOf(opened.State.SelectedSet.Buttons[selectedButton], field));
            Refresh();
        };
        editor.MakeCopy += async () =>
        {
            if (opened is null || selectedButton < 0) return;
            if (opened.State.CopyStringForButton(selectedButton, field, out var refused) is int) Refresh();
            else if (refused is not null) await Message("No copy made", refused);
        };
        var idRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        idRow.Children.Add(id);
        idRow.Children.Add(pick.Also(b => b.VerticalAlignment = VerticalAlignment.Bottom));
        idRow.Children.Add(newString.Also(b => b.VerticalAlignment = VerticalAlignment.Bottom));
        var row = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(idRow);
        row.Children.Add(editor.Root);
        return row;
    }

    /// <summary>The string list as a picker for a field: strings used by buttons, or all.</summary>
    Flyout PickFlyout(StringField field)
    {
        var query = new TextBox { PlaceholderText = "Search text, or #id" };
        var all = new CheckBox { Content = "All strings, not only those buttons use" };
        var rows = new ListView { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None, Height = 380, Width = 620,
            FontFamily = new FontFamily("Consolas") };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { query, all, rows } } };
        void Fill()
        {
            if (opened?.State.Strings is not StringTable table) { rows.ItemsSource = null; return; }
            rows.ItemsSource = StringList.Rows(table, opened.State.Document.Sets, query.Text, false,
                all.IsChecked == true ? StringFilter.All : StringFilter.UsedByButtons, id => opened.Catalog.Entries[id].Name);
        }
        query.TextChanged += (_, _) => Fill();
        all.Click += (_, _) => Fill();
        flyout.Opening += (_, _) => Fill();
        rows.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not StringRow row) return;
            EditSelected(b => StringUses.WithId(b, field, (ushort)row.Id));
            flyout.Hide();
        };
        return flyout;
    }

    void ShowPage()
    {
        if (setsPage is null || stringsPage is null) return;   // a selection event while the layout is built
        bool strings = pages.SelectedItem == stringsPageItem;
        setsPage.Visibility = strings ? Visibility.Collapsed : Visibility.Visible;
        stringsPage.Root.Visibility = strings ? Visibility.Visible : Visibility.Collapsed;
        if (strings) stringsPage.Refresh();
        else Refresh();
    }

    /// <summary>From the Strings page's "used by": show that set and button.</summary>
    void JumpToButton(ButtonRef r)
    {
        if (opened is null) return;
        pages.SelectedItem = setsPageItem;
        opened.State.SelectedSetId = r.SetId;
        selectedButton = r.Index;
        selectedPosition = opened.State.SelectedSet.Buttons[r.Index].Position;
        SelectSet(r.SetId);
        Refresh();
    }

    static MenuFlyoutItem Item(string text, Action click)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => click();
        return item;
    }

    static KeyboardAccelerator Accelerator(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, e) => { e.Handled = true; action(); };
        return accelerator;
    }

    static ushort Value(NumberBox box) =>
        double.IsNaN(box.Value) ? (ushort)0 : (ushort)Math.Clamp(Math.Round(box.Value), 0, ushort.MaxValue);

    /// <summary>The address picked in a function box; an unknown one (the last item) stays.</summary>
    static uint Pick(ComboBox box, FunctionTable table, uint current) =>
        box.SelectedIndex >= 0 && box.SelectedIndex < table.Functions.Count
            ? table.Functions[box.SelectedIndex].Address
            : current;

    // -------- Opening and saving --------

    void ShowNothingOpen()
    {
        setTitle.Text = "Open SCManifold.exe to edit its button sets";
        status.Text = $"StarCraft folder: {Workspace.StarCraftDir(settings)}";
        panel.Visibility = Visibility.Collapsed;
    }

    async Task PickExe()
    {
        if (!await ConfirmDiscard()) return;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await OpenExe(file.Path);
    }

    async Task PickStarCraftDir()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        settings = settings with { StarCraftDir = folder.Path };
        settings.Save(Workspace.SettingsPath);
        if (opened is null) ShowNothingOpen();
        else await Message("StarCraft folder", $"Set to {folder.Path}. It is used the next time an exe is opened.");
    }

    async Task OpenExe(string path)
    {
        try
        {
            opened = Workspace.Open(path, Workspace.StarCraftDir(settings));
        }
        catch (Exception e)
        {
            await Message("Can't open the exe", e.Message, e.ToString());
            return;
        }
        icons = new IconCache(opened.Resources);
        settings = settings with { LastExe = path };
        settings.Save(Workspace.SettingsPath);
        unlockedStrings.Clear();
        catalogStrings = opened.State.Strings;
        await OfferRepackStrings(path);
        selectedPosition = 0;
        selectedButton = -1;
        panel.Visibility = Visibility.Visible;
        FillSetList();
        SelectSet(opened.State.SelectedSetId);
        Refresh();
    }

    async Task<bool> Save()
    {
        if (opened is null) return false;
        string? error;
        byte[]? strings = null;
        try
        {
            error = EditorSession.Save(opened.Archive, opened.State.Document, out strings);
        }
        catch (Exception e)
        {
            error = e.ToString();
        }
        if (error is not null)
        {
            await Message("Not saved", error);
            return false;
        }
        if (strings is not null) await WriteRepackStrings(strings);
        Refresh();
        return true;
    }

    // -------- The to-repack copy of stat_txt.tbl (spec §7) --------

    /// <summary>After a save that wrote strings: the same table to the exe's to-repack copy, asked once per exe.</summary>
    async Task WriteRepackStrings(byte[] table)
    {
        string exePath = opened!.ExePath;
        string? mirror = settings.MirrorFor(exePath);
        if (mirror is null)
        {
            mirror = await AskRepackPath(exePath);
            if (mirror is null) return;   // asked again next time
            settings = settings.WithMirror(exePath, mirror);
            settings.Save(Workspace.SettingsPath);
        }
        if (mirror.Length == 0) return;
        try
        {
            StringMirror.Write(mirror, table);
        }
        catch (Exception e)
        {
            await Message("to-repack copy not written",
                $"{opened.State.ExeName} is saved, but {mirror} could not be written ({e.Message}). The two now differ: " +
                "save again, or put the exe's rez\\stat_txt.tbl there by hand before the next repack.", e.ToString());
        }
    }

    /// <summary>Where to also write the strings: a path, "" for nowhere, null to ask next time.</summary>
    async Task<string?> AskRepackPath(string exePath)
    {
        string suggested = StringMirror.DefaultPath(exePath);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Also write stat_txt.tbl to to-repack?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                Text = $"So that adding to-repack's stat_txt.tbl with PyMPQ later doesn't put the old strings back, the editor can " +
                       $"write the same table to:\n\n{suggested}\n\nThe previous file is kept as stat_txt.tbl.bak. The answer is " +
                       $"remembered for {Path.GetFileName(exePath)}.",
            },
            PrimaryButtonText = "Yes, for this exe",
            SecondaryButtonText = "Choose folder...",
            CloseButtonText = "Not for this exe",
            DefaultButton = ContentDialogButton.Primary,
        };
        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                return suggested;
            case ContentDialogResult.Secondary:
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync();
                return folder is null ? null : Path.Combine(folder.Path, "stat_txt.tbl");
            default:
                return "";
        }
    }

    /// <summary>
    /// On opening: when the exe's to-repack copy differs from the table in the exe (edited
    /// in PyTBL, say), the user picks which one to edit.
    /// </summary>
    async Task OfferRepackStrings(string exePath)
    {
        var state = opened!.State;
        if (state.Strings is null) return;
        string? mirror = settings.MirrorFor(exePath);
        if (mirror is null)
        {
            string suggested = StringMirror.DefaultPath(exePath);
            mirror = File.Exists(suggested) ? suggested : "";
        }
        if (mirror.Length == 0) return;
        byte[]? exeTable;
        try
        {
            exeTable = opened.Archive.TryRead(StringTable.ArchivePath);
            if (StringMirror.Compare(exeTable, mirror) != StringMirror.State.Differs) return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return;
        }
        string newer = StringMirror.IsNewerThan(mirror, exePath) ? "The to-repack copy" : "The exe";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Two different stat_txt.tbl",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                Text = $"{mirror}\ndiffers from the stat_txt.tbl in {state.ExeName}" +
                       (exeTable is null ? " (the exe has none; the editor read vanilla's)" : "") +
                       $". {newer} was changed more recently.\n\nWhich one do you want to edit? Choosing to-repack's " +
                       "puts it into the exe at the next save.",
            },
            PrimaryButtonText = "The exe's",
            SecondaryButtonText = "to-repack's",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Secondary) return;
        try
        {
            state.Document.UseOpenedStrings(StringTable.Parse(File.ReadAllBytes(mirror)));
            catalogStrings = null;
            opened = opened with { Status = opened.Status.Append($"Strings from {mirror} (not yet saved into the exe)").ToArray() };
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await Message("Can't read the to-repack copy", $"{mirror}: {e.Message}. Editing the exe's strings.");
        }
    }

    /// <summary>With unsaved changes: Save, Don't save or Cancel. True to go on.</summary>
    async Task<bool> ConfirmDiscard()
    {
        if (opened is null || !opened.State.Document.IsDirty) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Unsaved changes",
            Content = $"Save the changes to {opened.State.ExeName}?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await Save(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeConfirmed || opened is null || !opened.State.Document.IsDirty) return;
        args.Cancel = true;
        if (await ConfirmDiscard())
        {
            closeConfirmed = true;
            Close();
        }
    }

    /// <summary>A message, with optional details (an exception) in selectable text to copy.</summary>
    async Task Message(string title, string text, string? details = null)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        if (details is not null)
            body.Children.Add(new TextBox
            {
                Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, MaxHeight = 320,
            });
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = title,
            Content = new ScrollViewer { Content = body, MaxHeight = 420 },
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    // -------- Commands --------

    /// <summary>Runs a command on the state and redraws if it changed anything.</summary>
    void Run(Func<EditorState, bool> command)
    {
        if (opened is not null && command(opened.State)) Refresh();
    }

    void Undo() => Run(s => KeepSelection(s.Undo()));
    void Redo() => Run(s => KeepSelection(s.Redo()));

    bool KeepSelection(bool changed)
    {
        if (!changed) return false;
        SelectSet(opened!.State.SelectedSetId);
        if (opened.State.LastStringId is int id && stringsPage.Root.Visibility == Visibility.Visible) stringsPage.Select(id);
        return true;
    }

    void EditSelected(Func<Manifold.Core.Data.Button, Manifold.Core.Data.Button> edit)
    {
        if (updating || opened is null) return;
        var state = opened.State;
        if (selectedButton < 0 || selectedButton >= state.SelectedSet.Buttons.Count) return;
        Run(s => s.EditButton(selectedButton, edit(s.SelectedSet.Buttons[selectedButton])));
    }

    void OnIconPicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FrameworkElement { Tag: int frame })
        {
            EditSelected(b => b with { Icon = (ushort)frame });
            iconButton.Flyout.Hide();
        }
    }

    // -------- Set list --------

    void FillSetList()
    {
        if (opened is null) return;
        updating = true;
        setList.Items.Clear();
        setItems.Clear();
        foreach (var group in opened.Catalog.Filter(search.Text))
        {
            setList.Items.Add(new ListViewItem
            {
                Content = new TextBlock { Text = SetCatalog.GroupTitle(group.Group), FontWeight = FontWeights.SemiBold },
                IsEnabled = false,
            });
            foreach (var entry in group.Entries)
            {
                var item = new ListViewItem { Tag = entry.Id };
                setItems[entry.Id] = item;
                setList.Items.Add(item);
                UpdateSetItem(entry.Id);
            }
        }
        if (setItems.TryGetValue(opened.State.SelectedSetId, out var selected)) setList.SelectedItem = selected;
        updating = false;
    }

    void UpdateSetItem(int id)
    {
        if (opened is null || !setItems.TryGetValue(id, out var item)) return;
        var entry = opened.Catalog.Entries[id];
        string dot = opened.State.Document.DiffersFromVanilla(id) ? "  ●" : "";
        item.Content = new TextBlock { Text = $"{id,3}  {entry.Name}{dot}" };
    }

    void SelectSet(int id)
    {
        updating = true;
        if (setItems.TryGetValue(id, out var item))
        {
            setList.SelectedItem = item;
            setList.ScrollIntoView(item);
        }
        updating = false;
    }

    void OnSetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (updating || opened is null || setList.SelectedItem is not ListViewItem { Tag: int id }) return;
        opened.State.SelectedSetId = id;
        selectedPosition = 0;
        selectedButton = -1;
        Refresh();
    }

    // -------- Redraw --------

    void Refresh()
    {
        if (opened is null) return;
        var state = opened.State;
        Title = state.Title;
        if (!ReferenceEquals(catalogStrings, state.Strings))
        {
            // Unit names are strings: rename the sets when they change.
            catalogStrings = state.Strings;
            opened = opened with { Catalog = SetCatalog.Build(state.Text, opened.Resources.Units) };
        }
        if (stringsPage.Root.Visibility == Visibility.Visible) stringsPage.Refresh();
        foreach (var id in setItems.Keys) UpdateSetItem(id);
        var entry = opened.Catalog.Entries[state.SelectedSetId];
        setTitle.Text = $"{state.SelectedSetId}  {entry.Name}";
        pasteSetButton.IsEnabled = state.HasSetClipboard;
        if (selectedButton >= state.SelectedSet.Buttons.Count) selectedButton = -1;
        DrawCard();
        FillButtonList();
        FillPanel();
        checks.Text = string.Join("\n", state.CheckLines());
        status.Text = string.Join("   |   ", opened.Status);
        ToolTipService.SetToolTip(status, string.Join("\n", opened.Status));
    }

    void DrawCard()
    {
        var state = opened!.State;
        card.Children.Clear();
        foreach (var cell in CardLayout.Cells(state.SelectedSet))
        {
            var view = CellView(cell);
            Grid.SetRow(view, cell.Row);
            Grid.SetColumn(view, cell.Column);
            card.Children.Add(view);
        }
    }

    FrameworkElement CellView(CardCell cell)
    {
        var state = opened!.State;
        var content = new Grid();
        if (cell.Icon is int icon)
        {
            if (icons?.Get(icon) is { } bitmap)
                content.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(6) });
            else
                content.Children.Add(new TextBlock
                {
                    Text = $"#{icon}", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                });
        }
        if (cell.Count > 1)
            content.Children.Add(new Border
            {
                Background = new SolidColorBrush(Colors.DarkOrange), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 0, 5, 0), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2),
                Child = new TextBlock { Text = cell.Count.ToString(), FontSize = 11, Foreground = new SolidColorBrush(Colors.Black) },
            });
        content.Children.Add(new TextBlock
        {
            Text = cell.Position.ToString(), FontSize = 10, Opacity = 0.5, Margin = new Thickness(3, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        });

        bool selected = cell.Position == selectedPosition;
        var border = new Border
        {
            Width = CellSize, Height = CellSize, Margin = new Thickness(3), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = new SolidColorBrush(selected ? Colors.DodgerBlue : Color(0x60, 0x80, 0x80, 0x80)),
            // Positions 10-15 are not shown in replays: a slightly different background.
            Background = new SolidColorBrush(cell.HiddenInReplays ? Color(0x30, 0x80, 0x60, 0x20) : Color(0x30, 0x80, 0x80, 0x80)),
            Child = content, AllowDrop = true, CanDrag = cell.Count > 0,
        };
        ToolTipService.SetToolTip(border, cell.Count == 0 ? $"Position {cell.Position}: empty"
            : $"Position {cell.Position}: " + string.Join(", ", cell.ButtonIndexes.Select(state.NameOf)));

        border.Tapped += (_, _) =>
        {
            selectedPosition = cell.Position;
            selectedButton = cell.Count > 0 ? cell.ButtonIndexes[0] : -1;
            Refresh();
        };
        border.DragStarting += (_, e) =>
        {
            dragFrom = cell.Position;
            e.Data.SetText(cell.Position.ToString());
            e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
        };
        border.DragOver += (_, e) =>
        {
            bool copy = (e.Modifiers & DragDropModifiers.Control) != 0;
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            e.DragUIOverride.Caption = copy ? $"Copy to {cell.Position}" : $"Move to {cell.Position}";
        };
        border.Drop += (_, e) =>
        {
            bool copy = (e.Modifiers & DragDropModifiers.Control) != 0;
            ushort from = dragFrom;
            dragFrom = 0;
            if (from == 0) return;
            if (opened!.State.DropAndSelect(from, cell.Position, copy) is int landed)
            {
                selectedPosition = cell.Position;
                selectedButton = landed;
                Refresh();
            }
        };

        var menu = new MenuFlyout();
        var copyItem = Item("Copy", () => { if (cell.Count > 0) state.CopyButton(cell.ButtonIndexes[0]); });
        copyItem.IsEnabled = cell.Count > 0;
        var pasteItem = Item("Paste", () => Run(s => s.PasteButton(cell.Position)));
        pasteItem.IsEnabled = state.HasButtonClipboard;
        var deleteItem = Item("Delete", () => Run(s => cell.Count > 0 && s.DeleteButton(cell.ButtonIndexes[0])));
        deleteItem.IsEnabled = cell.Count > 0;
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(deleteItem);
        border.ContextFlyout = menu;
        return border;
    }

    static Windows.UI.Color Color(byte a, byte r, byte g, byte b) => Windows.UI.Color.FromArgb(a, r, g, b);

    /// <summary>The selected cell's buttons, in order; or all of the set's.</summary>
    void FillButtonList()
    {
        var state = opened!.State;
        updating = true;
        buttonList.Items.Clear();
        var lines = state.ButtonLines();
        for (int i = 0; i < lines.Count; i++)
        {
            if (selectedPosition != 0 && state.SelectedSet.Buttons[i].Position != selectedPosition) continue;
            var item = new ListViewItem { Content = new TextBlock { Text = lines[i] }, Tag = i };
            buttonList.Items.Add(item);
            if (i == selectedButton) buttonList.SelectedItem = item;
        }
        buttonListTitle.Text = selectedPosition == 0 ? "Buttons in this set" : $"Buttons at position {selectedPosition}";
        updating = false;
    }

    void OnButtonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (updating || buttonList.SelectedItem is not ListViewItem { Tag: int index }) return;
        selectedButton = index;
        Refresh();
    }

    void FillPanel()
    {
        var state = opened!.State;
        updating = true;
        bool has = selectedButton >= 0 && selectedButton < state.SelectedSet.Buttons.Count;
        foreach (var child in panel.Children.OfType<Control>()) child.IsEnabled = has;
        if (!has)
        {
            // Empty, not the last button's values greyed out.
            panelTitle.Text = "Select a button";
            iconButton.Content = null;
            conditionBox.ItemsSource = null;
            actionBox.ItemsSource = null;
            foreach (var box in new[] { conditionVar, actionVar, enabledString, disabledString })
                box.Value = double.NaN;
            enabledEditor.ShowNone("");
            disabledEditor.ShowNone("");
            updating = false;
            return;
        }
        var b = state.SelectedSet.Buttons[selectedButton];
        panelTitle.Text = $"{state.NameOf(selectedButton)} (position {b.Position})";
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (icons?.Get(b.Icon) is { } bitmap) iconRow.Children.Add(new Image { Source = bitmap, Width = 36, Height = 34 });
        iconRow.Children.Add(new TextBlock
        {
            Text = b.Icon < iconNames.Count ? $"{b.Icon}: {iconNames[b.Icon]}" : $"{b.Icon}",
            VerticalAlignment = VerticalAlignment.Center,
        });
        iconButton.Content = iconRow;
        FillFunctions(conditionBox, state.Conditions, b.Condition, state.ConditionLabel);
        FillFunctions(actionBox, state.Actions, b.Action, state.ActionLabel);
        conditionVar.Value = b.ConditionVar;
        actionVar.Value = b.ActionVar;
        enabledString.Value = b.EnabledString;
        disabledString.Value = b.DisabledString;
        ShowString(enabledEditor, enabledNew, StringField.Enabled, b.EnabledString);
        ShowString(disabledEditor, disabledNew, StringField.Disabled, b.DisabledString);
        updating = false;
    }

    /// <summary>A field's string in its editor, locked while other buttons share it (spec §5).</summary>
    void ShowString(StringEditorView editor, Button newString, StringField field, ushort id)
    {
        var state = opened!.State;
        newString.Visibility = id == 0 && state.Strings is not null ? Visibility.Visible : Visibility.Collapsed;
        if (state.Strings is not StringTable table) { editor.ShowNone("No stat_txt.tbl: strings can't be edited"); return; }
        if (id == 0) { editor.ShowNone("No string"); return; }
        if (id > table.Count) { editor.ShowNone($"String {id} is past the end of stat_txt.tbl ({table.Count})"); return; }
        var others = state.SharedWith(selectedButton, field);
        var sets = state.Document.Sets;
        string shared = others.Count == 0 && StringUses.UnitOf(id) is null ? ""
            : StringList.UsesLabel(id, others, sets, s => opened.Catalog.Entries[s].Name)
              + (others.Count > 0 ? $" ({others.Count} other button{(others.Count == 1 ? "" : "s")})" : "");
        editor.Show(field == StringField.Enabled ? $"Tooltip, string {id}" : $"Disabled text, string {id}",
            table.Segment(id), field == StringField.Enabled, shared, shared.Length > 0 && !unlockedStrings.Contains(id),
            opened.Resources.TextColours);
    }

    /// <summary>The table's functions as "name (address)"; an address not in it is added last.</summary>
    static void FillFunctions(ComboBox box, FunctionTable table, uint address, Func<uint, string> label)
    {
        var items = table.Functions.Select(f => label(f.Address)).ToList();
        int index = table.Functions.ToList().FindIndex(f => f.Address == address);
        if (index < 0)
        {
            items.Add(label(address));
            index = items.Count - 1;
        }
        box.ItemsSource = items;
        box.SelectedIndex = index;
    }

    void FillIconGrid()
    {
        if (iconGrid.Items.Count > 0 || icons is null) return;
        int count = Math.Max(icons.Count, iconNames.Count);
        for (int frame = 0; frame < count; frame++)
        {
            var tile = new StackPanel { Width = 64, Tag = frame };
            if (icons.Get(frame) is { } bitmap) tile.Children.Add(new Image { Source = bitmap, Width = 36, Height = 34 });
            tile.Children.Add(new TextBlock
            {
                Text = frame < iconNames.Count ? $"{frame} {iconNames[frame]}" : frame.ToString(),
                FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            ToolTipService.SetToolTip(tile, frame < iconNames.Count ? iconNames[frame] : frame.ToString());
            iconGrid.Items.Add(tile);
        }
    }
}

static class Extensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
