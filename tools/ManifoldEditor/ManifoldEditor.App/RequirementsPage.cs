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
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>
/// The Requirements page, FireGraft's way (requirements UX spec): the five tables as tabs,
/// FireGraft's tree or a flat ID list, and the selected record one row per word, each changed
/// by its own dropdown. Commands go through EditorState; the owner redraws after one.
/// </summary>
sealed class RequirementsPage
{
    public Grid Root { get; } = new() { ColumnSpacing = 16 };

    readonly Func<OpenedExe?> opened;
    readonly Action changed;
    readonly Func<DatId, IReadOnlyList<string>> names;
    readonly Func<string, string, Task> message;
    readonly Func<EditorSettings> settings;
    readonly Action<EditorSettings> setSettings;

    readonly SelectorBar tabs = new();
    readonly SelectorBarItem[] tabItems = ReqTables.All.Select(t => new SelectorBarItem { Text = ReqTables.Title(t) }).ToArray();
    readonly TextBox find = new() { PlaceholderText = "Name or id" };
    readonly ListView entries = new() { SelectionMode = ListViewSelectionMode.Single, FontFamily = new FontFamily("Consolas") };
    readonly ToggleButton treeView = new() { Content = "Tree" }, idView = new() { Content = "ID" };
    readonly TextBlock listNote = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    readonly StackPanel problemBar = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    readonly TextBlock problemText = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    readonly TextBlock note = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    readonly CheckBox used = new() { Content = "Used" };
    readonly ListView rowList = new() { SelectionMode = ListViewSelectionMode.Single, MinHeight = 260 };
    readonly Button insert = new() { Content = "Insert" }, delete = new() { Content = "Delete" },
        up = new() { Content = "Move up" }, down = new() { Content = "Move down" }, addLevels = new() { Content = "Add levels" },
        addRecord = new() { Content = "Add record" }, deleteRecord = new() { Content = "Delete record" },
        revert = new() { Content = "Revert to vanilla" }, copy = new() { Content = "Copy record" }, paste = new() { Content = "Paste record" };

    ReqTable table = ReqTable.Units;
    readonly Dictionary<ReqTable, int> selected = ReqTables.All.ToDictionary(t => t, _ => 0);
    readonly HashSet<string> openGroups = new();
    IReadOnlyList<ReqRow> rows = [];
    /// <summary>Insert's empty row: where its line will go; shown until a value is chosen or the next redraw.</summary>
    ReqLineAt? pendingInsert;
    bool updating;
    Brush? accent;
    OpenedExe? shownExe;
    readonly MenuFlyout entryMenu = new(), rowMenu = new();
    readonly MenuFlyoutItem menuCopy = new() { Text = "Copy record", KeyboardAcceleratorTextOverride = "Ctrl+Shift+C" },
        menuPaste = new() { Text = "Paste record", KeyboardAcceleratorTextOverride = "Ctrl+Shift+P" },
        menuInsert = new() { Text = "Insert" }, menuDelete = new() { Text = "Delete" }, menuUp = new() { Text = "Move up" },
        menuDown = new() { Text = "Move down" }, menuLevels = new() { Text = "Add levels" };

    int SelectedId { get => selected[table]; set => selected[table] = value; }

    public RequirementsPage(Func<OpenedExe?> opened, Action changed, Func<DatId, IReadOnlyList<string>> names,
        Func<string, string, Task> message, Func<EditorSettings> settings, Action<EditorSettings> setSettings)
    {
        this.opened = opened;
        this.changed = changed;
        this.names = names;
        this.message = message;
        this.settings = settings;
        this.setSettings = setSettings;

        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        foreach (var item in tabItems) tabs.Items.Add(item);
        tabs.SelectedItem = tabItems[0];
        tabs.SelectionChanged += (_, _) =>
        {
            if (updating || tabs.SelectedItem is not SelectorBarItem item) return;
            table = ReqTables.All[Array.IndexOf(tabItems, item)];
            pendingInsert = null;
            RevealSelected();
            Refresh();
        };
        Grid.SetColumnSpan(tabs, 2);
        Root.Children.Add(tabs);

        // The left: Find, the tree or the ID list, the view buttons.
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(find);
        Grid.SetRow(entries, 1);
        left.Children.Add(entries);
        var views = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { treeView, idView, listNote } };
        Grid.SetRow(views, 2);
        left.Children.Add(views);
        Grid.SetRow(left, 1);
        Root.Children.Add(left);

        find.TextChanged += (_, _) =>
        {
            if (find.Text.Trim().Length == 0) RevealSelected();
            FillEntries();
        };
        treeView.Click += (_, _) => SetTreeView(true);
        idView.Click += (_, _) => SetTreeView(false);
        // A leaf's selection shows its record; a group's row is opened or closed by a tap or Enter, not by selection,
        // so the arrow keys pass over headers.
        entries.SelectionChanged += (_, _) =>
        {
            if (updating || entries.SelectedItem is not ListViewItem { Tag: int id }) return;
            SelectedId = id;
            pendingInsert = null;
            ShowRecord();
        };
        menuCopy.Click += (_, _) => CopyRecord();
        menuPaste.Click += (_, _) => PasteRecord();
        entryMenu.Items.Add(menuCopy);
        entryMenu.Items.Add(menuPaste);
        entryMenu.Opening += (_, _) =>
        {
            EnableButtons();
            menuCopy.IsEnabled = copy.IsEnabled;
            menuPaste.IsEnabled = paste.IsEnabled;
        };

        // The right: the record.
        var replace = new Button { Content = "Replace it" };
        replace.Click += (_, _) => { opened()?.State.Document.ReplaceBadRequirements(); changed(); };
        problemBar.Children.Add(problemText);
        problemBar.Children.Add(replace);
        ToolTipService.SetToolTip(used, "Off: the game leaves this record out and allows it nowhere");
        used.Click += (_, _) => Run((s, name) => s.SetRequirementUsed(table, SelectedId, used.IsChecked == true, name));
        rowList.SelectionChanged += (_, _) => { if (!updating) EnableButtons(); };
        menuInsert.Click += (_, _) => InsertRow();
        menuDelete.Click += (_, _) => DeleteRow();
        menuUp.Click += (_, _) => MoveRow(-1);
        menuDown.Click += (_, _) => MoveRow(+1);
        menuLevels.Click += (_, _) => Edit(RequirementEdits.AddLevels);
        foreach (var item in new[] { menuInsert, menuDelete, menuUp, menuDown, menuLevels }) rowMenu.Items.Add(item);
        rowMenu.Opening += (_, _) =>
        {
            EnableButtons();
            menuInsert.IsEnabled = insert.IsEnabled;
            menuDelete.IsEnabled = delete.IsEnabled;
            menuUp.IsEnabled = up.IsEnabled;
            menuDown.IsEnabled = down.IsEnabled;
            menuLevels.IsEnabled = addLevels.IsEnabled && addLevels.Visibility == Visibility.Visible;
        };
        insert.Click += (_, _) => InsertRow();
        delete.Click += (_, _) => DeleteRow();
        up.Click += (_, _) => MoveRow(-1);
        down.Click += (_, _) => MoveRow(+1);
        addLevels.Click += (_, _) => Edit(RequirementEdits.AddLevels);
        addRecord.Click += (_, _) => Run((s, name) => s.AddRequirementRecord(table, SelectedId, name));
        deleteRecord.Click += (_, _) => Run((s, name) => s.DeleteRequirementRecord(table, SelectedId, name));
        revert.Click += (_, _) => Run((s, name) => s.RevertRequirementToVanilla(table, SelectedId, name));
        copy.Click += (_, _) => CopyRecord();
        paste.Click += (_, _) => PasteRecord();

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(problemBar);
        right.Children.Add(title);
        right.Children.Add(note);
        right.Children.Add(used);
        right.Children.Add(rowList);
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { insert, delete, up, down, addLevels } });
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addRecord, deleteRecord, revert, copy, paste } });
        var scroll = new ScrollViewer { Content = right };
        Grid.SetRow(scroll, 1);
        Grid.SetColumn(scroll, 1);
        Root.Children.Add(scroll);
    }

    /// <summary>Shows the record an undo or redo changed.</summary>
    public void Show(ReqPlace place)
    {
        table = place.Table;
        SelectedId = place.Id;
        pendingInsert = null;
        RevealSelected();
        if (find.Text.Length > 0) find.Text = "";
        Refresh();
    }

    /// <summary>Redraws the tabs, the list and the selected record from the state; Insert's empty row goes.</summary>
    public void Refresh()
    {
        pendingInsert = null;
        if (!ReferenceEquals(opened(), shownExe))
        {
            shownExe = opened();
            RevealSelected();
        }
        var doc = opened()?.State.Document;
        updating = true;
        foreach (var (item, t) in tabItems.Zip(ReqTables.All))
            item.Text = ReqTables.Title(t) + (doc?.RequirementTableDirty(t) == true ? " ●" : "");
        tabs.SelectedItem = tabItems[(int)table];
        updating = false;
        FillEntries();
        ShowRecord();
    }

    /// <summary>Copy record (Ctrl+Shift+C): keeps the selected record. True: the key was handled.</summary>
    public bool CopyRecord()
    {
        if (opened() is not { } exe) return false;
        if (!exe.State.CopyRequirement(table, SelectedId)) _ = message("Not copied", $"{Name(SelectedId)} has no requirements record; the earlier copy stays.");
        EnableButtons();
        return true;
    }

    /// <summary>Paste record (Ctrl+Shift+P): onto the selected id; a refusal says why.</summary>
    public bool PasteRecord()
    {
        if (opened() is not { } exe) return false;
        if (exe.State.PasteRequirement(table, SelectedId, Name(SelectedId)) is string refused)
            _ = message("Not pasted", refused);
        else changed();
        return true;
    }

    string Name(int id)
    {
        var list = names(ReqTables.NamesFrom(table));
        return id < list.Count ? list[id] : $"#{id}";
    }

    Brush Accent() => accent ??=
        Application.Current.Resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var brush) && brush is Brush b
            ? b
            : new SolidColorBrush(Colors.DodgerBlue);

    bool TreeShown => settings().RequirementsTree && opened()?.ReqLists?.ContainsKey(table) == true;

    void SetTreeView(bool tree)
    {
        if (settings().RequirementsTree != tree) setSettings(settings() with { RequirementsTree = tree });
        FillEntries();
    }

    string LeafTitle(int id)
    {
        var doc = opened()?.State.Document;
        var record = doc?.Requirements?[table].Find(id);
        string state = record is null ? "  (none)" : record.Used ? "" : "  (off)";
        string dot = doc?.RecordDiffersFromVanilla(table, id) == true ? " •" : "";
        return $"[{id}] {Name(id)}{state}{dot}";
    }

    void FillEntries()
    {
        updating = true;
        entries.Items.Clear();
        var exe = opened();
        bool tree = TreeShown;
        treeView.IsChecked = tree;
        idView.IsChecked = !tree;
        treeView.IsEnabled = exe?.ReqLists?.ContainsKey(table) == true;
        listNote.Text = treeView.IsEnabled ? "" : "No FireGraft list for this table: ID view";
        if (exe?.State.Document is { Requirements: not null } doc)
        {
            var groups = tree ? exe.ReqLists![table] : null;
            var nodes = ReqTree.Build(ReqTables.IdCount(table), groups, LeafTitle, find.Text);
            bool searching = find.Text.Trim().Length > 0;
            ListViewItem? current = null;
            void Add(IReadOnlyList<ReqTreeNode> level, int depth, string prefix)
            {
                foreach (var node in level)
                {
                    var text = new TextBlock { Margin = new Thickness(depth * 16, 0, 0, 0) };
                    if (node.Id is int id)
                    {
                        text.Text = node.Title;
                        if (doc.RecordChangedSinceSave(table, id)) text.Foreground = Accent();
                        if (doc.Requirements[table].Find(id) is null) text.Opacity = 0.5;
                        var item = new ListViewItem { Tag = id, Content = text, ContextFlyout = entryMenu };
                        // A right-click selects the leaf first, so the menu acts on it.
                        item.ContextRequested += (_, _) => { if (!ReferenceEquals(entries.SelectedItem, item)) entries.SelectedItem = item; };
                        entries.Items.Add(item);
                        if (id == SelectedId) current = item;
                        continue;
                    }
                    string key = prefix + node.Title;
                    bool open = searching || openGroups.Contains(key);
                    text.Text = (open ? "▾ " : "▸ ") + node.Title;
                    text.FontWeight = FontWeights.SemiBold;
                    var header = new ListViewItem { Tag = key, Content = text };
                    header.Tapped += (_, _) => ToggleGroup(key);
                    header.KeyDown += (_, args) =>
                    {
                        if (args.Key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return;
                        args.Handled = true;
                        ToggleGroup(key);
                    };
                    entries.Items.Add(header);
                    if (open) Add(node.Children, depth + 1, key + "/");
                }
            }
            Add(nodes, 0, $"{table}/");
            if (current is not null)
            {
                entries.SelectedItem = current;
                entries.ScrollIntoView(current);
            }
        }
        updating = false;
    }

    /// <summary>Opens or closes a group (not while Find shows every group open).</summary>
    void ToggleGroup(string key)
    {
        if (find.Text.Trim().Length > 0) return;
        if (!openGroups.Remove(key)) openGroups.Add(key);
        Later(FillEntries);
    }

    /// <summary>Opens the selected record's groups: when the selection comes from outside (a tab, Undo, a cleared Find, an exe).</summary>
    void RevealSelected()
    {
        if (opened()?.ReqLists is not { } lists || !lists.TryGetValue(table, out var groups) ||
            !groups.TryGetValue(SelectedId, out var path)) return;
        for (int i = 1; i <= path.Count; i++) openGroups.Add($"{table}/" + string.Join("/", path.Take(i)));
    }

    // -------- The record --------

    ReqRecord? Record => opened()?.State.Document is { RequirementsProblem: null, Requirements: { } r } ? r[table].Find(SelectedId) : null;

    IReadOnlyList<string> UnitItems => Numbered(names(DatId.Units));
    IReadOnlyList<string> TechItems => Numbered(names(DatId.Techdata));
    static IReadOnlyList<string> Numbered(IReadOnlyList<string> list) => list.Select((n, i) => $"[{i}] {n}").ToArray();

    IReadOnlyList<ReqOpcode> Opcodes => ReqOpcodes.Offered(table).Skip(1).ToArray();   // "Must have" is the Parameter switch

    void ShowRecord()
    {
        updating = true;
        rowList.Items.Clear();
        rows = [];
        var doc = opened()?.State.Document;
        problemBar.Visibility = doc?.RequirementsProblem is null ? Visibility.Collapsed : Visibility.Visible;
        problemText.Text = doc?.RequirementsProblem is string problem
            ? $"{RequirementsFile.ArchivePath} in the exe can't be read ({problem}). The tables shown are what an exe without it opens as; " +
              "Replace it makes them editable, and the next Save writes a new file."
            : "";
        bool editable = doc?.Requirements is not null && doc.RequirementsProblem is null;
        var record = doc?.Requirements?[table].Find(SelectedId);
        title.Text = doc?.Requirements is null ? "No requirements" : $"{ReqTables.Title(table)}: [{SelectedId}] {Name(SelectedId)}";
        note.Text = doc?.Requirements is null ? ""
            : record is null ? "No record: the game allows this nowhere. Add record to give it requirements."
            : !record.Used ? "Used is off: the game leaves this record out and allows it nowhere."
            : "";
        used.IsChecked = record?.Used ?? false;
        used.IsEnabled = editable && record is not null;
        if (record is not null)
        {
            rows = RequirementEdits.Rows(record);
            foreach (var row in rows)
            {
                if (pendingInsert is { } p && row.At == p && row.Kind != ReqRowKind.Param ||
                    pendingInsert is { } q && row.Kind == ReqRowKind.End && row.Level == q.Level && q.Index == LinesIn(record, q.Level))
                    rowList.Items.Add(Selectable(new ListViewItem { Content = NewRowControls(pendingInsert!.Value, "(new row: choose a value)"), Tag = "pending" }));
                rowList.Items.Add(Selectable(new ListViewItem { Content = RowControls(row, editable) }));
            }
        }
        addRecord.IsEnabled = editable && record is null;
        deleteRecord.IsEnabled = editable && record is not null;
        revert.IsEnabled = editable && doc!.RecordDiffersFromVanilla(table, SelectedId);
        addLevels.Visibility = table == ReqTable.Upgrades ? Visibility.Visible : Visibility.Collapsed;
        updating = false;
        EnableButtons();
    }

    /// <summary>Focus inside a row (its dropdowns) selects the row, so Insert, Delete and Move act on it.</summary>
    ListViewItem Selectable(ListViewItem item)
    {
        item.GotFocus += (_, _) => { if (!updating && !ReferenceEquals(rowList.SelectedItem, item)) rowList.SelectedItem = item; };
        item.ContextFlyout = rowMenu;
        item.ContextRequested += (_, _) => { if (!ReferenceEquals(rowList.SelectedItem, item)) rowList.SelectedItem = item; };
        return item;
    }

    static int LinesIn(ReqRecord r, int level) => level == 0 ? r.Lines.Count : r.Levels.First(l => l.Level == level).Lines.Count;

    UIElement RowControls(ReqRow row, bool editable)
    {
        double indent = row.Level > 0 ? 24 : 0;
        switch (row.Kind)
        {
            case ReqRowKind.LevelHeader:
                return new TextBlock { Text = $"Level {row.Level}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
            case ReqRowKind.End:
                return NewRowControls(new ReqLineAt(row.Level, Record is { } r ? LinesIn(r, row.Level) : 0), "==End of List==", indent, editable);
        }
        var at = row.At!.Value;
        var line = row.Line!.Value;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(indent, 0, 0, 0) };
        if (row.Kind == ReqRowKind.Param)
        {
            panel.Margin = new Thickness(indent + 126, 0, 0, 0);
            bool tech = ReqTables.ParamOf(table, line.Word) == ReqParam.Tech;
            panel.Children.Add(ValueBox(tech ? TechItems : UnitItems, line.Param ?? 0, editable,
                value => Edit(r => RequirementEdits.SetValue(r, at, (ushort)value), at)));
            return panel;
        }
        var kind = SwitchBox(row.Kind == ReqRowKind.MustHave, editable, parameter =>
            Edit(r => parameter ? RequirementEdits.ToParameter(r, table, at) : RequirementEdits.ToOpcode(r, at), at));
        panel.Children.Add(kind);
        if (row.Kind == ReqRowKind.MustHave)
        {
            panel.Children.Add(ValueBox(UnitItems, line.Word, editable, value => Edit(r => RequirementEdits.SetValue(r, at, (ushort)value), at)));
            return panel;
        }
        var opcodes = Opcodes.ToList();
        var labels = opcodes.Select(o => o.Name).ToList();
        int index = opcodes.FindIndex(o => o.Word == line.Word);
        if (index < 0)
        {
            labels.Add($"Unknown 0x{line.Word:X4}");
            index = labels.Count - 1;
        }
        panel.Children.Add(ValueBox(labels, index, editable, chosen =>
        {
            if (chosen < opcodes.Count) Edit(r => RequirementEdits.SetOpcode(r, table, at, opcodes[chosen].Word), at);
        }));
        return panel;
    }

    /// <summary>An empty row (End of List, or Insert's): a switch and an empty value box; a choice adds the line at <paramref name="at"/>.</summary>
    UIElement NewRowControls(ReqLineAt at, string placeholder, double indent = 0, bool editable = true)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(indent, 0, 0, 0) };
        var opcodes = Opcodes;
        ComboBox values = new() { Width = 360, PlaceholderText = placeholder, IsEnabled = editable, IsTextSearchEnabled = true };
        var kind = SwitchBox(false, editable, parameter =>
        {
            values.Items.Clear();
            foreach (var label in parameter ? UnitItems : opcodes.Select(o => o.Name)) values.Items.Add(label);
        });
        foreach (var o in opcodes) values.Items.Add(o.Name);
        OnCommit(values, -1, index =>
        {
            bool parameter = kind.SelectedIndex == 1;
            ushort word = parameter ? (ushort)index : opcodes[index].Word;
            var line = RequirementEdits.NewLine(table, word, parameter);
            pendingInsert = null;
            Edit(r => RequirementEdits.Insert(r, at, line), at);
        });
        panel.Children.Add(kind);
        panel.Children.Add(values);
        return panel;
    }

    /// <summary>The Opcode/Parameter switch; <paramref name="switched"/> gets true for Parameter.</summary>
    ComboBox SwitchBox(bool parameter, bool editable, Action<bool> switched)
    {
        var box = new ComboBox { Width = 120, IsEnabled = editable, Items = { "Opcode", "Parameter" }, SelectedIndex = parameter ? 1 : 0 };
        OnCommit(box, box.SelectedIndex, index => switched(index == 1));
        return box;
    }

    ComboBox ValueBox(IReadOnlyList<string> items, int selectedIndex, bool editable, Action<int> chosen)
    {
        var box = new ComboBox { Width = 360, IsEnabled = editable, IsTextSearchEnabled = true };
        foreach (var item in items) box.Items.Add(item);
        if (selectedIndex >= 0 && selectedIndex < items.Count) box.SelectedIndex = selectedIndex;
        OnCommit(box, box.SelectedIndex, chosen);
        return box;
    }

    /// <summary>
    /// A dropdown's choice is applied when it closes or loses focus, not on each selection change:
    /// the arrow keys and type-ahead on a closed box would otherwise make an undo step per key.
    /// </summary>
    void OnCommit(ComboBox box, int initial, Action<int> chosen)
    {
        bool done = false;
        void Commit()
        {
            if (done || updating || box.SelectedIndex < 0 || box.SelectedIndex == initial) return;
            done = true;
            int index = box.SelectedIndex;
            Later(() => chosen(index));
        }
        box.DropDownClosed += (_, _) => Commit();
        box.LostFocus += (_, _) => Commit();
    }

    /// <summary>Runs after the dropdown's own event: the edit redraws the rows, the dropdown with them.</summary>
    void Later(Action action) => Root.DispatcherQueue.TryEnqueue(() => action());

    ReqRow? SelectedRow => rowList.SelectedItem is ListViewItem { Tag: "pending" } ? null
        : rowList.SelectedIndex >= 0 ? RowAt(rowList.SelectedIndex) : null;

    /// <summary>The row behind a list index, past Insert's pending row.</summary>
    ReqRow? RowAt(int index)
    {
        int pendingAt = rowList.Items.Select((item, i) => (item, i)).FirstOrDefault(p => p.item is ListViewItem { Tag: "pending" }, (null!, -1)).Item2;
        if (pendingAt >= 0 && index > pendingAt) index--;
        return index < rows.Count ? rows[index] : null;
    }

    void EnableButtons()
    {
        bool editable = Record is not null;
        var row = SelectedRow;
        insert.IsEnabled = editable && row is { } && (row.At is not null || row.Kind == ReqRowKind.End);
        delete.IsEnabled = editable && row is { } && (row.At is not null || row.Kind == ReqRowKind.LevelHeader);
        up.IsEnabled = down.IsEnabled = editable && row?.At is not null;
        addLevels.IsEnabled = editable && Record!.Levels.Count == 0;
        copy.IsEnabled = opened()?.State.Document.Requirements?[table].Find(SelectedId) is not null;
        paste.IsEnabled = opened()?.State.HasCopiedRequirement == true && opened()?.State.Document.RequirementsProblem is null;
    }

    void Run(Func<EditorState, string, bool> command)
    {
        if (opened() is not { } exe) return;
        if (command(exe.State, Name(SelectedId))) changed();
    }

    /// <summary>One edit of the record; afterwards the line at <paramref name="select"/> (or the one before it) is selected and its dropdown focused.</summary>
    void Edit(Func<ReqRecord, ReqRecord> edit, ReqLineAt? select = null)
    {
        if (opened() is not { } exe) return;
        if (exe.State.EditRequirement(table, SelectedId, edit, Name(SelectedId))) changed();
        else ShowRecord();   // nothing changed: put the dropdowns back
        if (select is { } at) SelectLine(at);
    }

    void SelectLine(ReqLineAt at)
    {
        int index = rows.ToList().FindIndex(r => r.At == at && r.Kind != ReqRowKind.Param);
        if (index < 0 && at.Index > 0) index = rows.ToList().FindIndex(r => r.At == at with { Index = at.Index - 1 } && r.Kind != ReqRowKind.Param);
        if (index < 0 || index >= rowList.Items.Count) return;
        rowList.SelectedIndex = index;
        var item = rowList.Items[index] as ListViewItem;
        rowList.ScrollIntoView(item);
        Later(() => { if (item?.Content is Panel { Children.Count: > 0 } panel && panel.Children[^1] is ComboBox box) box.Focus(FocusState.Programmatic); });
    }

    void InsertRow()
    {
        if (Record is not { } record || SelectedRow is not { } row) return;
        pendingInsert = row.At ?? new ReqLineAt(row.Level, LinesIn(record, row.Level));
        ShowRecord();
    }

    void DeleteRow()
    {
        if (SelectedRow is not { } row) return;
        if (row.At is { } at) Edit(r => RequirementEdits.Remove(r, at), at);
        else if (row.Kind == ReqRowKind.LevelHeader) Edit(RequirementEdits.RemoveLevels);
    }

    void MoveRow(int delta)
    {
        if (SelectedRow is not { At: { } at } || Record is not { } record) return;
        if (RequirementEdits.Move(record, at, delta).SameAs(record)) return;   // at an end: nothing to move
        Edit(r => RequirementEdits.Move(r, at, delta), at with { Index = at.Index + delta });
    }
}
