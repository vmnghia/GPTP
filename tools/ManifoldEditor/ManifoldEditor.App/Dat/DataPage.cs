using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ManifoldEditor.App.Dat.Tabs;
using ManifoldEditor.App.Services;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// The Data page (spec §6), PyDAT's layout: on the left the current file's entries, with
/// Find, ID Jump and the Names settings; on the right a bar of the 11 files, the units
/// sub-tabs, the tab, and the check lines. Edits go through <see cref="DatContext.Edit"/>;
/// the window redraws after one and calls <see cref="Refresh"/> while the page shows.
/// The list's menu and the TXT menu copy, paste, export and import PyDAT's text, and edit
/// the file's name overrides (links spec §3, §6).
/// </summary>
sealed class DataPage
{
    /// <summary>Check lines shown at most; the rest are counted (a bad expanded file can have thousands).</summary>
    const int MaxCheckLines = 50;
    /// <summary>More rows changed than this and the list is rebuilt at once rather than row by row.</summary>
    const int MaxRowUpdates = 2000;

    public Grid Root { get; } = new() { ColumnSpacing = 16 };

    readonly Func<OpenedExe?> opened;
    readonly Action changed;
    readonly Func<string, string, Task> message;
    readonly Func<nint> windowHandle;
    readonly Func<EditorSettings> settings;
    readonly Action<EditorSettings> setSettings;
    readonly DatContext context;

    // The left: Find, ID Jump, the entries, Names.
    readonly TextBox find = new() { PlaceholderText = "Name or text" };
    readonly TextBlock findNote = new() { FontSize = 12, Opacity = 0.75, Visibility = Visibility.Collapsed };
    readonly TextBox jump = new() { Width = 90, PlaceholderText = "0 or 0x0", FontFamily = new FontFamily("Consolas") };
    readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, FontFamily = new FontFamily("Consolas") };
    readonly ComboBox namesMode = new() { MinWidth = 160 };
    readonly CheckBox simpleNames = new() { Content = "Simple TBL Names" };
    // The list's menu, PyDAT's order and names (links spec §3, §6).
    readonly MenuFlyoutItem copyEntryItem = new() { Text = "Copy Entry to Clipboard", KeyboardAcceleratorTextOverride = "Ctrl+Shift+C" };
    readonly MenuFlyoutItem copySubTabItem = new() { Text = "Copy Sub-Tab to Clipboard", KeyboardAcceleratorTextOverride = "Ctrl+Shift+Y" };
    readonly MenuFlyoutItem pasteItem = new() { Text = "Paste from Clipboard", KeyboardAcceleratorTextOverride = "Ctrl+Shift+P" };
    readonly MenuFlyoutItem revertItem = new() { Text = "Revert entry", KeyboardAcceleratorTextOverride = "Ctrl+Shift+R" };
    readonly MenuFlyoutItem revertVanillaItem = new() { Text = "Revert entry to vanilla" };
    readonly MenuFlyoutItem overrideItem = new() { Text = "Override Name", KeyboardAcceleratorTextOverride = "Ctrl+Shift+N" };
    // The TXT menu above the tabs.
    readonly DropDownButton txtButton = new() { Content = "TXT", VerticalAlignment = VerticalAlignment.Center };
    readonly MenuFlyoutItem exportItem = new() { Text = "Export to TXT...", KeyboardAcceleratorTextOverride = "Ctrl+E" };
    readonly MenuFlyoutItem importItem = new() { Text = "Import from TXT...", KeyboardAcceleratorTextOverride = "Ctrl+I" };

    // The right: the files, the units sub-tabs, the tab, the checks.
    readonly SelectorBar files = new();
    readonly SelectorBarItem[] fileItems;
    readonly SelectorBar unitsTabs = new();
    readonly ScrollViewer unitsTabsScroll;
    readonly SelectorBarItem[] unitsItems;
    readonly Grid tabHost = new();
    readonly TextBlock missing = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    readonly Dictionary<DatTabId, (IDatTab? Tab, ScrollViewer Host)> tabs = new();
    readonly TextBlock checksTitle = new() { Text = "Checks", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
    readonly StackPanel checkLines = new() { Spacing = 0 };
    readonly ScrollViewer checksScroll;
    // "Used by" (links spec §5), under the tab host: a header that folds the list, and the list.
    readonly StackPanel usedByPanel = new() { Spacing = 2, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly HyperlinkButton usedByHeader = new() { Padding = new Thickness(0) };
    readonly FontIcon usedByArrow = new() { FontSize = 12 };
    readonly TextBlock usedByTitle = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ListView usedByList = new()
    {
        MaxHeight = 132, FontFamily = new FontFamily("Consolas"), FontSize = 12, SelectionMode = ListViewSelectionMode.Single,
    };

    /// <summary>Each file's tabs (DatTabs.For allocates: asked once).</summary>
    readonly Dictionary<DatId, IReadOnlyList<DatTab>> fileTabs = new();
    static readonly DatTabId[] UnitsTabIds =
        [DatTabId.UnitsBasic, DatTabId.UnitsAdvanced, DatTabId.UnitsSounds, DatTabId.UnitsGraphics, DatTabId.UnitsStarEdit, DatTabId.UnitsAiActions];

    /// <summary>The exe the page shows (its state); another one reads the settings again.</summary>
    EditorState? shownState;
    DatId file = DatId.Units;
    DatTabId unitsTab = DatTabId.UnitsBasic;
    readonly Dictionary<DatId, int> entries = new();
    IDatTab? shownTab;

    // The list's rows and what they were made from, to change only the rows that changed.
    ObservableCollection<string> rows = new();
    IReadOnlyList<string> rowNames = [];
    bool[] rowDots = [];
    DatId? rowsFile;
    int rowsPad;

    // The checks and the files they were worked out from.
    DatFile?[]? checksKey;
    EditorState? checksState;

    // "Used by": the lines for the shown file and entry, and what they were worked out from (an edit makes
    // new files, so one pass over the referring files is paid per entry shown or edit, not per redraw).
    EditorState? usedByState;
    DatId usedByFile;
    int usedByEntry = -1;
    DatFile?[]? usedByDats;
    IReadOnlyList<DatRefMatch> usedByFound = [];
    /// <summary>The Check buttons' lines, in place of the found ones until the entry, the file or the data changes.</summary>
    IReadOnlyList<DatRefMatch>? usedByChecked;
    /// <summary>The lines the list shows (what a double-click acts on) and their text.</summary>
    IReadOnlyList<DatRefMatch> usedByShown = [];
    List<string> usedByText = [];

    /// <summary>True while the page fills its controls: their selection events are its own.</summary>
    bool updating;
    /// <summary>True while a command waits on the clipboard, a file picker or the overrides dialog: another one waits its turn.</summary>
    bool busy;

    /// <param name="opened">The open exe, or null.</param>
    /// <param name="changed">After an edit or a revert: the window redraws (and calls Refresh).</param>
    /// <param name="settings">The window's settings now.</param>
    /// <param name="setSettings">Replaces the window's settings (kept in memory; the window writes them to disk).</param>
    /// <param name="message">The window's message dialog (a refused paste or import).</param>
    /// <param name="windowHandle">The window, for the file pickers.</param>
    public DataPage(Func<OpenedExe?> opened, Action changed, Func<EditorSettings> settings, Action<EditorSettings> setSettings,
        Func<string, string, Task> message, Func<nint> windowHandle)
    {
        this.opened = opened;
        this.changed = changed;
        this.message = message;
        this.windowHandle = windowHandle;
        this.settings = settings;
        this.setSettings = setSettings;
        context = new DatContext(opened, NameSetting, changed, (id, entry) => JumpTo(id, entry, null), ShowChecked);
        foreach (var format in DatFormats.All) fileTabs[format.Id] = DatTabs.For(format.Id);

        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // -------- The left --------
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var next = new Button { Content = "Next" };
        next.Click += (_, _) => FindNext();
        find.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            FindNext();
        };
        find.TextChanged += (_, _) => findNote.Visibility = Visibility.Collapsed;
        var findRow = new Grid { ColumnSpacing = 6 };
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        findRow.Children.Add(Label("Find:"));
        Grid.SetColumn(find, 1);
        findRow.Children.Add(find);
        Grid.SetColumn(next, 2);
        findRow.Children.Add(next);

        var go = new Button { Content = "Go" };
        go.Click += (_, _) => Jump();
        jump.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            Jump();
        };
        var jumpRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        jumpRow.Children.Add(Label("ID Jump:", 64));
        jumpRow.Children.Add(jump);
        jumpRow.Children.Add(go);
        left.Children.Add(new StackPanel { Spacing = 6, Children = { findRow, jumpRow, findNote } });

        list.ItemsSource = rows;
        list.SelectionChanged += (_, _) =>
        {
            // The page's own selection (while it fills the list, or a row replaced) is not the user's.
            if (updating || list.SelectedIndex < 0 || list.SelectedIndex == Entry) return;
            SelectEntry(list.SelectedIndex, scroll: false);
        };
        var menu = new MenuFlyout();
        copyEntryItem.Click += (_, _) => CopyEntry(subTab: false);
        copySubTabItem.Click += (_, _) => CopyEntry(subTab: true);
        pasteItem.Click += async (_, _) => await PasteEntry();
        revertItem.Click += (_, _) => RevertEntry(toVanilla: false);
        revertVanillaItem.Click += (_, _) => RevertEntry(toVanilla: true);
        overrideItem.Click += async (_, _) => await OverrideName();
        menu.Items.Add(copyEntryItem);
        menu.Items.Add(copySubTabItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(revertItem);
        menu.Items.Add(revertVanillaItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(overrideItem);
        menu.Opening += (_, _) => UpdateMenu();
        // Not ContextFlyout: a right click doesn't select the row, and the menu acts on the
        // selected entry. The row under the pointer (or the focused row, from the keyboard) is
        // selected first, then the menu opens for it.
        list.ContextRequested += (_, e) =>
        {
            if (Ancestor<ListViewItem>(e.OriginalSource as DependencyObject) is not { } item) return;
            int index = list.IndexFromContainer(item);
            if (index < 0) return;
            // Where to open, read before the selection redraws (and may recycle the row's container).
            bool atPointer = e.TryGetPosition(list, out var point);
            if (index != Entry) SelectEntry(index, scroll: false);
            UpdateMenu();
            if (atPointer) menu.ShowAt(list, new FlyoutShowOptions { Position = point });
            else menu.ShowAt(list);
            e.Handled = true;
        };
        Grid.SetRow(list, 1);
        left.Children.Add(list);

        foreach (string mode in new[] { "Basic", "TBL Based", "Combined" }) namesMode.Items.Add(mode);
        namesMode.SelectionChanged += (_, _) => { if (!updating) ChangeNames(); };
        simpleNames.Click += (_, _) => { if (!updating) ChangeNames(); };
        var namesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        namesRow.Children.Add(Label("Names:", 64));
        namesRow.Children.Add(namesMode);
        var simpleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        simpleRow.Children.Add(Label("", 64));
        simpleRow.Children.Add(simpleNames);
        var names = new StackPanel { Spacing = 4, Children = { namesRow, simpleRow } };
        Grid.SetRow(names, 2);
        left.Children.Add(names);
        Root.Children.Add(left);

        // -------- The right --------
        var right = new Grid { RowSpacing = 4 };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        fileItems = DatFormats.All.Select(f => new SelectorBarItem { Text = FileTabName(f.Id) }).ToArray();
        foreach (var item in fileItems) files.Items.Add(item);
        files.SelectionChanged += (_, _) =>
        {
            if (updating || files.SelectedItem is not SelectorBarItem item) return;
            int index = Array.IndexOf(fileItems, item);
            if (index >= 0 && (DatId)index != file) SelectFile((DatId)index);
        };
        // Eleven names may not fit beside the list: the bar scrolls sideways. The TXT menu at its end.
        exportItem.Click += async (_, _) => await ExportText();
        importItem.Click += async (_, _) => await ImportText();
        var txtMenu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        txtMenu.Items.Add(exportItem);
        txtMenu.Items.Add(importItem);
        txtMenu.Opening += (_, _) => UpdateMenu();
        txtButton.Flyout = txtMenu;
        var filesRow = new Grid { ColumnSpacing = 8 };
        filesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filesRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filesRow.Children.Add(SideScroll(files));
        Grid.SetColumn(txtButton, 1);
        filesRow.Children.Add(txtButton);
        right.Children.Add(filesRow);

        unitsItems = UnitsTabIds.Select(id => new SelectorBarItem { Text = DatTabs.Of(id).Name }).ToArray();
        foreach (var item in unitsItems) unitsTabs.Items.Add(item);
        unitsTabs.SelectionChanged += (_, _) =>
        {
            if (updating || unitsTabs.SelectedItem is not SelectorBarItem item) return;
            int index = Array.IndexOf(unitsItems, item);
            if (index >= 0 && UnitsTabIds[index] != unitsTab) SelectUnitsTab(UnitsTabIds[index]);
        };
        unitsTabsScroll = SideScroll(unitsTabs);
        Grid.SetRow(unitsTabsScroll, 1);
        right.Children.Add(unitsTabsScroll);

        tabHost.Children.Add(missing);
        Grid.SetRow(tabHost, 2);
        right.Children.Add(tabHost);

        usedByHeader.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, Children = { usedByArrow, usedByTitle },
        };
        usedByHeader.Click += (_, _) => SetUsedByOpen(!settings().UsedByOpen);
        usedByList.DoubleTapped += (_, e) =>
        {
            // The row under the pointer (the first tap selected it); else the selected one.
            int index = Ancestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } item ? usedByList.IndexFromContainer(item) : usedByList.SelectedIndex;
            UsedByJump(index);
        };
        usedByList.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            UsedByJump(usedByList.SelectedIndex);
        };
        usedByPanel.Children.Add(usedByHeader);
        usedByPanel.Children.Add(usedByList);
        Grid.SetRow(usedByPanel, 3);
        right.Children.Add(usedByPanel);

        checksScroll = new ScrollViewer { MaxHeight = 160, Content = checkLines };
        var checks = new StackPanel { Children = { checksTitle, checksScroll } };
        Grid.SetRow(checks, 4);
        right.Children.Add(checks);
        Grid.SetColumn(right, 1);
        Root.Children.Add(right);

        ShowNothing();
    }

    // -------- What the window calls --------

    /// <summary>Redraws from the state: the bars' marks, the list, the tab, the checks.</summary>
    public void Refresh() => Refresh(scroll: false);

    /// <summary>
    /// Shows a place (after an undo or redo, or a check line): its file, the tab that shows
    /// its property (the current units sub-tab when it does), and its entry. It commits
    /// nothing: after an undo the boxes still hold what the step took away, and the window
    /// committed them before it (a check line's link commits first itself).
    /// </summary>
    public void Show(DatPlace place)
    {
        if (opened() is not { } exe) return;
        TakeState(exe);
        file = place.File;
        if (place.File == DatId.Units && place.Property is { } property)
        {
            try { unitsTab = DatTabs.TabFor(DatId.Units, property, unitsTab); }
            catch (ArgumentException) { }   // a property no tab shows: stay on the sub-tab
        }
        entries[file] = place.Entry;
        Remember();
        Refresh(scroll: true);
    }

    /// <summary>Ctrl+F: the Find box, its text selected.</summary>
    public void FocusFind()
    {
        find.Focus(FocusState.Keyboard);
        find.SelectAll();
    }

    /// <summary>Ctrl+G: the ID Jump box, its text selected.</summary>
    public void FocusJump()
    {
        jump.Focus(FocusState.Keyboard);
        jump.SelectAll();
    }

    /// <summary>
    /// Commits what the shown tab's boxes hold (before Save, Undo, Redo, and before the page
    /// shows something else); only into the exe the tab shows: after another exe is opened
    /// (with the page hidden, it is not redrawn) its boxes are the last exe's.
    /// </summary>
    public void CommitPending()
    {
        if (shownState is not null && ReferenceEquals(opened()?.State, shownState)) shownTab?.CommitPending();
    }

    /// <summary>
    /// Revert entry (to the file as opened or last saved) or Revert entry to vanilla, on the
    /// selected entry: one undo step. False when there is nothing to revert.
    /// </summary>
    public bool RevertEntry(bool toVanilla)
    {
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is null) return false;
        if (!exe.State.RevertEntry(file, Entry, toVanilla)) return false;
        changed();
        return true;
    }

    // -------- Choosing --------

    int Entry => entries.GetValueOrDefault(file);

    DatTabId CurrentTab => file == DatId.Units ? unitsTab : fileTabs[file][0].Id;

    void SelectEntry(int entry, bool scroll)
    {
        // The caller read the new entry first: a commit redraws, and the redraw selects the entry still shown.
        CommitPending();
        entries[file] = entry;
        Remember();
        Refresh(scroll);
    }

    void SelectFile(DatId id)
    {
        CommitPending();
        file = id;
        Remember();
        findNote.Visibility = Visibility.Collapsed;
        Refresh(scroll: true);
    }

    void SelectUnitsTab(DatTabId id)
    {
        CommitPending();
        unitsTab = id;
        Remember();
        Refresh(scroll: false);
    }

    /// <summary>
    /// Shows <paramref name="target"/>'s tab at <paramref name="entry"/>, on the units sub-tab <paramref name="subTab"/>
    /// when it names one (the Jump buttons, a "Used by" line). Not an edit. Nothing happens for a file the exe
    /// lacks or an entry it lacks.
    /// </summary>
    void JumpTo(DatId target, int entry, DatTabId? subTab)
    {
        if (opened() is not { } exe || exe.State.Document.Dat(target) is not { } dat || entry < 0 || entry >= dat.Count) return;
        CommitPending();
        TakeState(exe);
        file = target;
        if (target == DatId.Units && subTab is { } tab && DatTabs.Of(tab).File == DatId.Units) unitsTab = tab;
        entries[file] = entry;
        Remember();
        findNote.Visibility = Visibility.Collapsed;
        Refresh(scroll: true);
    }

    /// <summary>The file, the units sub-tab and the file's entry, into the settings.</summary>
    void Remember() =>
        setSettings(settings().WithEntry(file, Entry) with { LastDat = file.ToString(), LastUnitsTab = unitsTab.ToString() });

    /// <summary>A new exe (or the first): the file, sub-tab and entries from the settings, the caches emptied.</summary>
    void TakeState(OpenedExe exe)
    {
        if (ReferenceEquals(exe.State, shownState)) return;
        shownState = exe.State;
        shownTab = null;   // the last exe's boxes: shown again (reloaded) by the redraw that follows
        var s = settings();
        file = Enum.TryParse<DatId>(s.LastDat, out var f) && Enum.IsDefined(f) ? f : DatId.Units;
        unitsTab = Enum.TryParse<DatTabId>(s.LastUnitsTab, out var t) && Enum.IsDefined(t) && DatTabs.Of(t).File == DatId.Units
            ? t : DatTabId.UnitsBasic;
        entries.Clear();
        foreach (var format in DatFormats.All) entries[format.Id] = s.EntryFor(format.Id);
        rowsFile = null;
        checksKey = null;
    }

    void ChangeNames()
    {
        var mode = (NamesMode)Math.Max(0, namesMode.SelectedIndex);
        bool simple = DatNames.HasSimple(file) && simpleNames.IsChecked == true;
        if (NameSetting(file) == new DatNameSetting(mode, simple)) return;
        CommitPending();
        setSettings(settings().WithNames(file, new DatNameSetting(mode, simple)));
        // The names show in the list and in the tabs' dropdowns that point into this file.
        Refresh(scroll: false);
    }

    void FindNext()
    {
        string query = find.Text;
        if (query.Length == 0 || rows.Count == 0) return;
        int start = Math.Clamp(Entry, 0, rows.Count - 1);
        // PyDAT's: the next row holding the text after the selection, wrapping, any case.
        for (int step = 1; step < rows.Count; step++)
        {
            int at = (start + step) % rows.Count;
            if (rows[at].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                findNote.Visibility = Visibility.Collapsed;
                SelectEntry(at, scroll: true);
                return;
            }
        }
        findNote.Text = $"Can't find '{query}'.";
        findNote.Visibility = Visibility.Visible;
    }

    void Jump()
    {
        if (rows.Count == 0) return;
        if (DatValues.ParseId(jump.Text) is not { } id)
        {
            jump.BorderBrush = new SolidColorBrush(Colors.Red);
            ToolTipService.SetToolTip(jump, $"0-{rows.Count - 1}, or 0x for hex");
            return;
        }
        jump.ClearValue(Control.BorderBrushProperty);
        ToolTipService.SetToolTip(jump, null);
        // As PyDAT's box, a number past the end goes to the last entry.
        SelectEntry((int)Math.Min(id, (ulong)(rows.Count - 1)), scroll: true);
    }

    void UpdateMenu()
    {
        var doc = opened()?.State.Document;
        int entry = Entry;
        bool hasEntry = doc?.Dat(file) is { } dat && entry < dat.Count;
        copyEntryItem.IsEnabled = hasEntry;
        copySubTabItem.IsEnabled = hasEntry;
        pasteItem.IsEnabled = hasEntry;
        revertItem.IsEnabled = doc?.OpenedDat(file) is { } o && entry < o.Count && doc.Dat(file) is not null;
        revertVanillaItem.IsEnabled = doc?.VanillaDat(file) is { } v && entry < v.Count && doc.Dat(file) is not null;
        overrideItem.IsEnabled = hasEntry;
        exportItem.IsEnabled = doc?.Dat(file) is not null;
        importItem.IsEnabled = doc?.Dat(file) is not null;
    }

    // -------- Copy, paste, TXT, name overrides (links spec §3, §6) --------

    /// <summary>
    /// Copy Entry (or Copy Sub-Tab: the shown units sub-tab's properties) to the Windows
    /// clipboard, as PyDAT's text. False when there is no entry to copy.
    /// </summary>
    public bool CopyEntry(bool subTab)
    {
        if (DialogOpen()) return false;
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is null) return false;
        string text = exe.State.CopyEntryText(file, Entry, subTab ? CurrentTab : null);
        if (text.Length == 0) return false;
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception e)
        {
            // Another program holding the clipboard.
            _ = Tell("Not copied", $"The clipboard can't be written: {e.Message}");
            return false;
        }
        try
        {
            // Kept after the editor closes, to paste into PyDAT or a text file.
            Clipboard.Flush();
        }
        catch (Exception)
        {
            // Copied all the same, for as long as the editor runs.
        }
        return true;
    }

    /// <summary>Paste from Clipboard: the clipboard's one entry onto the selected entry (one undo step); a refusal in a message.</summary>
    public async Task PasteEntry()
    {
        if (busy || DialogOpen()) return;
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is null) return;
        var state = exe.State;
        var at = file;
        int entry = Entry;
        string? text = null;
        busy = true;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text)) text = await content.GetTextAsync();
        }
        catch (Exception)
        {
            text = null;   // a clipboard another program holds, or text it can't give: no entry
        }
        finally
        {
            busy = false;
        }
        if (!ReferenceEquals(opened()?.State, state)) return;   // another exe opened meanwhile
        string? refused;
        // Core refuses what it can name; anything else is said too, rather than closing the editor (an async click).
        try { refused = state.PasteEntry(at, entry, text); }
        catch (Exception e) { refused = e.Message; }
        if (refused is not null) await Tell("Not pasted", refused);
        else changed();
    }

    /// <summary>Export to TXT: every entry of the current file, to a .txt the user picks (a temporary file, then replaced).</summary>
    public async Task ExportText()
    {
        if (busy || DialogOpen()) return;
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is not { } dat) return;
        string fileName = DatFormats.Of(file).FileName;
        string text = DatText.Export(dat, Enumerable.Range(0, dat.Count));
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(fileName) };
        picker.FileTypeChoices.Add("PyDAT text", [".txt"]);
        if (await Pick(picker, () => picker.PickSaveFileAsync().AsTask()) is not string path) return;
        try
        {
            TextFiles.Write(path, new UTF8Encoding(false).GetBytes(text));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Tell("Not exported", $"{fileName} could not be written to {path}: {e.Message}");
        }
    }

    /// <summary>
    /// Import from TXT: the entries a .txt lists replace those entries of the current file, as
    /// one undo step; any refusal refuses the whole file, in a message with its line.
    /// </summary>
    public async Task ImportText()
    {
        if (busy || DialogOpen()) return;
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is null) return;
        var state = exe.State;
        var at = file;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        if (await Pick(picker, () => picker.PickSingleFileAsync().AsTask()) is not string path) return;
        string name = Path.GetFileName(path);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Tell("Not imported", $"{name} can't be read: {e.Message}");
            return;
        }
        if (!ReferenceEquals(opened()?.State, state)) return;
        string? refused;
        try { refused = state.ImportText(at, text, name); }
        catch (Exception e) { refused = e.Message; }
        if (refused is not null) await Tell("Not imported", $"{name}: {refused}");
        else changed();
    }

    /// <summary>Override Name: PyDAT's Name Overrides dialog for the current file, on the selected entry; OK is one undo step.</summary>
    public async Task OverrideName()
    {
        if (busy || DialogOpen() || Root.XamlRoot is not { } xamlRoot) return;
        CommitPending();
        if (opened() is not { } exe || exe.State.Document.Dat(file) is null) return;
        var state = exe.State;
        var at = file;
        int entry = Entry;
        NameOverrides? after;
        busy = true;
        try
        {
            after = await new NameOverridesDialog(xamlRoot, windowHandle(), at, state.Document.Overrides(at), entry).ShowAsync();
        }
        catch (Exception)
        {
            after = null;   // a dialog that couldn't open (another one showing): nothing changes
        }
        finally
        {
            busy = false;
        }
        if (after is null || !ReferenceEquals(opened()?.State, state)) return;
        if (state.EditOverrides(at, after, entry)) changed();
    }

    /// <summary>A file picker shown for the window: the path picked, or null (cancelled, or the picker failed: said in a message).</summary>
    async Task<string?> Pick(object picker, Func<Task<Windows.Storage.StorageFile>> show)
    {
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle());
        Windows.Storage.StorageFile? picked = null;
        string? failure = null;
        busy = true;
        try
        {
            picked = await show();
        }
        catch (Exception e)
        {
            failure = e.Message;
        }
        finally
        {
            busy = false;
        }
        if (failure is not null) await Tell("No file chosen", failure);
        return picked?.Path;
    }

    /// <summary>A message in the window's dialog, unless a dialog already shows (one at a time).</summary>
    async Task Tell(string title, string text)
    {
        if (DialogOpen()) return;
        try
        {
            await message(title, text);
        }
        catch (Exception)
        {
            // A dialog opened meanwhile: the message is dropped rather than the editor closed.
        }
    }

    /// <summary>Whether a ContentDialog shows: a second one can't open (and its keys shouldn't reach the page).</summary>
    bool DialogOpen() =>
        Root.XamlRoot is { } xamlRoot && VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Any(p => p.Child is ContentDialog);

    // -------- Drawing --------

    void Refresh(bool scroll)
    {
        if (opened() is not { } exe)
        {
            ShowNothing();
            return;
        }
        TakeState(exe);
        var doc = exe.State.Document;
        var dat = doc.Dat(file);
        if (dat is not null) entries[file] = Math.Clamp(Entry, 0, dat.Count - 1);
        updating = true;
        try
        {
            for (int i = 0; i < fileItems.Length; i++)
            {
                var id = (DatId)i;
                // Unsaved: the .dat file or its name overrides (links spec §6).
                string text = FileTabName(id) + (doc.DatDirty(id) || doc.OverridesDirty(id) ? " ●" : "");
                if (fileItems[i].Text != text) fileItems[i].Text = text;
            }
            files.SelectedItem = fileItems[(int)file];
            unitsTabsScroll.Visibility = file == DatId.Units ? Visibility.Visible : Visibility.Collapsed;
            unitsTabs.SelectedItem = unitsItems[Array.IndexOf(UnitsTabIds, unitsTab)];

            var names = NameSetting(file);
            namesMode.SelectedIndex = (int)names.Mode;
            simpleNames.IsEnabled = DatNames.HasSimple(file);
            simpleNames.IsChecked = simpleNames.IsEnabled && names.Simple;

            bool rebuilt = FillList(doc, dat);
            if (dat is not null && Entry < rows.Count)
            {
                if (list.SelectedIndex != Entry) list.SelectedIndex = Entry;
                if (scroll || rebuilt) list.ScrollIntoView(rows[Entry]);
            }
        }
        finally
        {
            updating = false;
        }
        ShowTab(dat);
        ShowUsedBy(exe.State, dat);
        ShowChecks(exe.State);
    }

    /// <summary>
    /// The entry list, " {id}  {name}" (PyDAT's ' %s  %s') with the id padded as PyDAT pads it and • after an entry
    /// that differs from vanilla's. Only the rows that changed are replaced. True when the list
    /// was made anew (another file, another count, or many rows at once).
    /// </summary>
    bool FillList(ButtonSetDocument doc, DatFile? dat)
    {
        if (dat is null)
        {
            rows = new ObservableCollection<string>();
            list.ItemsSource = rows;
            rowNames = [];
            rowDots = [];
            rowsFile = null;
            return true;
        }
        var names = context.Names(file);
        int count = Math.Min(dat.Count, names.Count);
        int pad = Math.Min(4, (count - 1).ToString(CultureInfo.InvariantCulture).Length);
        var dots = new bool[count];
        for (int i = 0; i < count; i++) dots[i] = doc.EntryDiffersFromVanilla(file, i);

        bool rebuild = rowsFile != file || rows.Count != count || rowsPad != pad;
        List<int>? changedRows = null;
        if (!rebuild)
        {
            changedRows = new List<int>();
            for (int i = 0; i < count; i++)
                if (dots[i] != rowDots[i] || !string.Equals(names[i], rowNames[i], StringComparison.Ordinal))
                    changedRows.Add(i);
            rebuild = changedRows.Count > MaxRowUpdates;
        }
        rowNames = names;
        rowDots = dots;
        rowsFile = file;
        rowsPad = pad;
        if (rebuild)
        {
            var all = new string[count];
            for (int i = 0; i < count; i++) all[i] = Row(i);
            rows = new ObservableCollection<string>(all);
            list.ItemsSource = rows;
            return true;
        }
        foreach (int i in changedRows!) rows[i] = Row(i);
        return false;
    }

    string Row(int id) =>
        $" {id.ToString(CultureInfo.InvariantCulture).PadLeft(rowsPad)}  {rowNames[id]}" + (rowDots[id] ? " •" : "");

    /// <summary>The current tab, showing the entry; or why there is none.</summary>
    void ShowTab(DatFile? dat)
    {
        foreach (var (_, host) in tabs.Values) host.Visibility = Visibility.Collapsed;
        missing.Visibility = Visibility.Collapsed;
        shownTab = null;
        if (dat is null)
        {
            missing.Text = $"{DatFormats.Of(file).ArchivePath} was not found.";
            missing.Visibility = Visibility.Visible;
            return;
        }
        var id = CurrentTab;
        var (tab, scroller) = TabOf(id);
        scroller.Visibility = Visibility.Visible;
        if (tab is null) return;
        try
        {
            tab.Show(Entry);
            shownTab = tab;
        }
        catch (Exception e)
        {
            // A tab's mistake shows in its place, rather than closing the editor.
            scroller.Content = Failure($"The {DatTabs.Of(id).Name} tab could not show entry {Entry}: {e.Message}");
            tabs[id] = (null, scroller);
        }
    }

    /// <summary>The tab, made the first time it shows (its ScrollViewer kept in the host, so it keeps its scroll).</summary>
    (IDatTab? Tab, ScrollViewer Host) TabOf(DatTabId id)
    {
        if (tabs.TryGetValue(id, out var made)) return made;
        IDatTab? tab;
        UIElement content;
        try
        {
            tab = CreateTab(id, context);
            content = tab.Root;
        }
        catch (Exception e)
        {
            // A property misspelt in a tab (DatTabs.Property throws) shows here, not as a crash.
            tab = null;
            content = Failure($"The {DatTabs.Of(id).Name} tab could not be built: {e.Message}");
        }
        var scroller = new ScrollViewer
        {
            Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto,
            Visibility = Visibility.Collapsed,
        };
        tabHost.Children.Add(scroller);
        tabs[id] = (tab, scroller);
        return (tab, scroller);
    }

    /// <summary>The tab for an id, one case per tab.</summary>
    static IDatTab CreateTab(DatTabId id, DatContext context) => id switch
    {
        DatTabId.UnitsBasic => new UnitsBasicTab(context),
        DatTabId.UnitsAdvanced => new UnitsAdvancedTab(context),
        DatTabId.UnitsSounds => new UnitsSoundsTab(context),
        DatTabId.UnitsGraphics => new UnitsGraphicsTab(context),
        DatTabId.UnitsStarEdit => new UnitsStarEditTab(context),
        DatTabId.UnitsAiActions => new UnitsAiActionsTab(context),
        DatTabId.Weapons => new WeaponsTab(context),
        DatTabId.Flingy => new FlingyTab(context),
        DatTabId.Sprites => new SpritesTab(context),
        DatTabId.Images => new ImagesTab(context),
        DatTabId.Upgrades => new UpgradesTab(context),
        DatTabId.Techdata => new TechdataTab(context),
        DatTabId.Sounds => new SoundsTab(context),
        DatTabId.Portraits => new PortraitsTab(context),
        DatTabId.Campaign => new CampaignTab(context),
        DatTabId.Orders => new OrdersTab(context),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such Data tab"),
    };

    /// <summary>
    /// "Used by" (links spec §5): every entry of every file whose field points at the shown entry. Worked out
    /// again only when the entry, the file or any file's data changed (an edit makes a new file), else the lines
    /// stay (and with them the Check buttons' lines, and the list's selection and scroll).
    /// </summary>
    void ShowUsedBy(EditorState state, DatFile? dat)
    {
        var doc = state.Document;
        var dats = DatFormats.All.Select(f => doc.Dat(f.Id)).ToArray();
        if (!(ReferenceEquals(state, usedByState) && usedByFile == file && usedByEntry == Entry && usedByDats is not null
              && dats.SequenceEqual(usedByDats, ReferenceComparer)))
        {
            usedByState = state;
            usedByFile = file;
            usedByEntry = Entry;
            usedByDats = dats;
            usedByChecked = null;
            usedByFound = dat is not null && DatRefs.All.Any(r => r.Target == file)
                ? DatRefs.UsedBy(file, Entry, id => doc.Dat(id))
                : [];
        }
        RenderUsedBy(dat is not null && (usedByChecked is not null || DatRefs.All.Any(r => r.Target == file)));
    }

    /// <summary>The Check buttons' lines (the Images tab) in place of the found ones, and the list open.</summary>
    void ShowChecked(IReadOnlyList<DatRefMatch> matches)
    {
        if (shownState is null) return;
        usedByChecked = matches;
        if (!settings().UsedByOpen) setSettings(settings() with { UsedByOpen = true });
        RenderUsedBy(true);
    }

    /// <summary>The header's arrow and count and the list, from the lines held. The list's items are replaced only when their text changed.</summary>
    void RenderUsedBy(bool visible)
    {
        usedByPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var matches = usedByChecked ?? usedByFound;
        var text = new List<string>(matches.Count);
        foreach (var match in matches)
        {
            var names = context.Names(match.File);
            text.Add(DatRefs.Describe(match, match.Entry < names.Count ? names[match.Entry] : ""));
        }
        usedByShown = matches;
        if (!text.SequenceEqual(usedByText))
        {
            usedByText = text;
            usedByList.ItemsSource = text.ToList();   // a fresh list: the one shown is never edited
        }
        bool open = settings().UsedByOpen;
        usedByTitle.Text = $"Used By ({matches.Count})" + (open ? ":" : "");
        usedByArrow.Glyph = open ? "\uE70D" : "\uE76C";   // chevron down; chevron right
        usedByList.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The header: fold or unfold the list, remembered in the settings.</summary>
    void SetUsedByOpen(bool open)
    {
        if (settings().UsedByOpen != open) setSettings(settings() with { UsedByOpen = open });
        RenderUsedBy(usedByPanel.Visibility == Visibility.Visible);
    }

    /// <summary>A line of "Used by" (double-click, Enter): its file, its sub-tab when it has one, its entry.</summary>
    void UsedByJump(int index)
    {
        if (index < 0 || index >= usedByShown.Count) return;
        var match = usedByShown[index];
        JumpTo(match.File, match.Entry, match.SubTab);
    }

    /// <summary>The check lines of every file (spec §8), at most 50; each with an entry is a link that shows it.</summary>
    void ShowChecks(EditorState state)
    {
        var doc = state.Document;
        var key = DatFormats.All.Select(f => doc.Dat(f.Id)).ToArray();
        if (ReferenceEquals(state, checksState) && checksKey is not null && key.SequenceEqual(checksKey, ReferenceComparer)) return;
        checksState = state;
        checksKey = key;
        var lines = DatChecks.Lines(doc);
        // Fresh elements every time: one element can't be in two places.
        checkLines.Children.Clear();
        foreach (var line in lines.Take(MaxCheckLines))
        {
            if (line.Entry is int entry)
            {
                var link = new HyperlinkButton
                {
                    Content = new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap }, Padding = new Thickness(0), FontSize = 12,
                };
                var place = new DatPlace(line.File, entry, line.Property);
                link.Click += (_, _) =>
                {
                    CommitPending();
                    Show(place);
                };
                checkLines.Children.Add(link);
            }
            else checkLines.Children.Add(new TextBlock { Text = line.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        if (lines.Count > MaxCheckLines)
            checkLines.Children.Add(new TextBlock { Text = $"and {lines.Count - MaxCheckLines} more", FontSize = 12, Opacity = 0.75 });
        checksTitle.Text = lines.Count == 0 ? "Checks: nothing found" : $"Checks ({lines.Count})";
        checksScroll.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Nothing open: an empty list, no tab.</summary>
    void ShowNothing()
    {
        CommitPending();
        shownState = null;
        shownTab = null;
        rows = new ObservableCollection<string>();
        list.ItemsSource = rows;
        rowsFile = null;
        checksKey = null;
        checksState = null;
        checkLines.Children.Clear();
        checksTitle.Text = "";
        usedByState = null;
        usedByDats = null;
        usedByChecked = null;
        usedByPanel.Visibility = Visibility.Collapsed;
        foreach (var (_, host) in tabs.Values) host.Visibility = Visibility.Collapsed;
        missing.Text = "Open an exe to edit its .dat files.";
        missing.Visibility = Visibility.Visible;
    }

    // -------- Pieces --------

    /// <summary>The file's Names setting; a mode the settings file got wrong is Basic.</summary>
    DatNameSetting NameSetting(DatId id)
    {
        var names = settings().NamesFor(id);
        return Enum.IsDefined(names.Mode) ? names : names with { Mode = NamesMode.Basic };
    }

    /// <summary>PyDAT's tab names: "Units" for the units file, else its one tab's ("Sound Effects", "Campaign"...).</summary>
    string FileTabName(DatId id) => id == DatId.Units ? "Units" : fileTabs[id][0].Name;

    static TextBlock Label(string text, double width = 0) => new()
    {
        Text = text, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, MinWidth = width,
    };

    static ScrollViewer SideScroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled,
    };

    /// <summary>The nearest element of type T at or above <paramref name="element"/> in the visual tree.</summary>
    static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is T found) return found;
        return null;
    }

    static TextBlock Failure(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    /// <summary>Files compare by reference: an edit makes a new one.</summary>
    static readonly IEqualityComparer<DatFile?> ReferenceComparer = ReferenceEqualityComparer.Instance;
}
