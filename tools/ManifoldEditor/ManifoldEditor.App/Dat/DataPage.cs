using System.Collections.ObjectModel;
using System.Globalization;
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
using Windows.System;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// The Data page (spec §6), PyDAT's layout: on the left the current file's entries, with
/// Find, ID Jump and the Names settings; on the right a bar of the 11 files, the units
/// sub-tabs, the tab, and the check lines. Edits go through <see cref="DatContext.Edit"/>;
/// the window redraws after one and calls <see cref="Refresh"/> while the page shows.
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
    readonly MenuFlyoutItem revertItem = new() { Text = "Revert entry", KeyboardAcceleratorTextOverride = "Ctrl+Shift+R" };
    readonly MenuFlyoutItem revertVanillaItem = new() { Text = "Revert entry to vanilla" };

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

    /// <summary>True while the page fills its controls: their selection events are its own.</summary>
    bool updating;

    /// <param name="opened">The open exe, or null.</param>
    /// <param name="changed">After an edit or a revert: the window redraws (and calls Refresh).</param>
    /// <param name="settings">The window's settings now.</param>
    /// <param name="setSettings">Replaces the window's settings (kept in memory; the window writes them to disk).</param>
    public DataPage(Func<OpenedExe?> opened, Action changed, Func<EditorSettings> settings, Action<EditorSettings> setSettings)
    {
        this.opened = opened;
        this.changed = changed;
        this.settings = settings;
        this.setSettings = setSettings;
        context = new DatContext(opened, NameSetting, changed);
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
        revertItem.Click += (_, _) => RevertEntry(toVanilla: false);
        revertVanillaItem.Click += (_, _) => RevertEntry(toVanilla: true);
        menu.Items.Add(revertItem);
        menu.Items.Add(revertVanillaItem);
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

        fileItems = DatFormats.All.Select(f => new SelectorBarItem { Text = FileTabName(f.Id) }).ToArray();
        foreach (var item in fileItems) files.Items.Add(item);
        files.SelectionChanged += (_, _) =>
        {
            if (updating || files.SelectedItem is not SelectorBarItem item) return;
            int index = Array.IndexOf(fileItems, item);
            if (index >= 0 && (DatId)index != file) SelectFile((DatId)index);
        };
        // Eleven names may not fit beside the list: the bar scrolls sideways.
        right.Children.Add(SideScroll(files));

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

        checksScroll = new ScrollViewer { MaxHeight = 160, Content = checkLines };
        var checks = new StackPanel { Children = { checksTitle, checksScroll } };
        Grid.SetRow(checks, 3);
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
    /// its property (the current units sub-tab when it does), and its entry.
    /// </summary>
    public void Show(DatPlace place)
    {
        CommitPending();
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

    /// <summary>Commits what the shown tab's boxes hold (before Save, Undo, Redo, and before the page shows something else).</summary>
    public void CommitPending() => shownTab?.CommitPending();

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

    /// <summary>The file, the units sub-tab and the file's entry, into the settings.</summary>
    void Remember() =>
        setSettings(settings().WithEntry(file, Entry) with { LastDat = file.ToString(), LastUnitsTab = unitsTab.ToString() });

    /// <summary>A new exe (or the first): the file, sub-tab and entries from the settings, the caches emptied.</summary>
    void TakeState(OpenedExe exe)
    {
        if (ReferenceEquals(exe.State, shownState)) return;
        shownState = exe.State;
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
        revertItem.IsEnabled = doc?.OpenedDat(file) is { } o && entry < o.Count && doc.Dat(file) is not null;
        revertVanillaItem.IsEnabled = doc?.VanillaDat(file) is { } v && entry < v.Count && doc.Dat(file) is not null;
    }

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
                string text = FileTabName(id) + (doc.DatDirty(id) ? " ●" : "");
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

    /// <summary>
    /// The tab for an id. Tasks 9-11 replace the placeholders here, one case per tab
    /// (<c>DatTabId.UnitsBasic =&gt; new UnitsBasicTab(context),</c>).
    /// </summary>
    static IDatTab CreateTab(DatTabId id, DatContext context) => id switch
    {
        _ => new PlaceholderTab(id),
    };

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
                link.Click += (_, _) => Show(place);
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
