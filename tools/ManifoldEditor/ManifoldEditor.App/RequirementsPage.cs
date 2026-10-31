using ManifoldEditor.App.Services;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>
/// The Requirements page (requirements spec §6): the five tables, every id of the selected
/// one with its record's state, and the selected record's lines in words. Commands go through
/// EditorState; the owner redraws after one.
/// </summary>
sealed class RequirementsPage
{
    public Grid Root { get; } = new() { ColumnSpacing = 16 };

    readonly Func<OpenedExe?> opened;
    readonly Action changed;
    readonly Func<DatId, IReadOnlyList<string>> names;
    readonly Func<string, string, Task> message;

    readonly ComboBox tableBox = new() { MinWidth = 160 };
    readonly TextBox find = new() { PlaceholderText = "Name or id" };
    readonly ListView entries = new() { SelectionMode = ListViewSelectionMode.Single, FontFamily = new FontFamily("Consolas") };
    readonly StackPanel problemBar = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    readonly TextBlock problemText = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    readonly TextBlock note = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    readonly CheckBox used = new() { Content = "Used" };
    readonly ListView lines = new() { SelectionMode = ListViewSelectionMode.Single, MinHeight = 240 };
    readonly Button add = new() { Content = "Add" }, insert = new() { Content = "Insert" }, change = new() { Content = "Change" },
        remove = new() { Content = "Remove" }, up = new() { Content = "Move up" }, down = new() { Content = "Move down" },
        addRecord = new() { Content = "Add record" }, deleteRecord = new() { Content = "Delete record" },
        revert = new() { Content = "Revert to vanilla" };

    ReqTable table = ReqTable.Units;
    int selectedId;
    IReadOnlyList<ReqRow> rows = [];
    bool updating;
    Brush? accent;

    public RequirementsPage(Func<OpenedExe?> opened, Action changed, Func<DatId, IReadOnlyList<string>> names, Func<string, string, Task> message)
    {
        this.opened = opened;
        this.changed = changed;
        this.names = names;
        this.message = message;

        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var t in ReqTables.All) tableBox.Items.Add(ReqTables.Title(t));
        tableBox.SelectedIndex = 0;
        left.Children.Add(tableBox);
        Grid.SetRow(find, 1);
        left.Children.Add(find);
        Grid.SetRow(entries, 2);
        left.Children.Add(entries);
        Root.Children.Add(left);

        tableBox.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            table = ReqTables.All[Math.Max(0, tableBox.SelectedIndex)];
            selectedId = 0;
            Refresh();
        };
        find.TextChanged += (_, _) => FillEntries();
        entries.SelectionChanged += (_, _) =>
        {
            if (updating || entries.SelectedItem is not ListViewItem { Tag: int id }) return;
            selectedId = id;
            ShowRecord();
        };

        var replace = new Button { Content = "Replace it" };
        replace.Click += (_, _) => { opened()?.State.Document.ReplaceBadRequirements(); changed(); };
        problemBar.Children.Add(problemText);
        problemBar.Children.Add(replace);

        ToolTipService.SetToolTip(used, "Off: the game leaves this record out and allows it nowhere");
        used.Click += (_, _) => Run((s, name) => s.SetRequirementUsed(table, selectedId, used.IsChecked == true, name));
        lines.SelectionChanged += (_, _) => { if (!updating) EnableLineButtons(); };
        add.Click += async (_, _) => await AddLine(insertBefore: false);
        insert.Click += async (_, _) => await AddLine(insertBefore: true);
        change.Click += async (_, _) => await ChangeLine();
        remove.Click += (_, _) => RemoveSelected();
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(+1);
        addRecord.Click += (_, _) => Run((s, name) => s.AddRequirementRecord(table, selectedId, name));
        deleteRecord.Click += (_, _) => Run((s, name) => s.DeleteRequirementRecord(table, selectedId, name));
        revert.Click += (_, _) => Run((s, name) => s.RevertRequirementToVanilla(table, selectedId, name));

        var right = new StackPanel { Spacing = 10 };
        right.Children.Add(problemBar);
        right.Children.Add(title);
        right.Children.Add(note);
        right.Children.Add(used);
        right.Children.Add(lines);
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { add, insert, change, remove, up, down } });
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addRecord, deleteRecord, revert } });
        var scroll = new ScrollViewer { Content = right };
        Grid.SetColumn(scroll, 1);
        Root.Children.Add(scroll);
    }

    /// <summary>Shows the record an undo or redo changed.</summary>
    public void Show(ReqPlace place)
    {
        table = place.Table;
        selectedId = place.Id;
        updating = true;
        tableBox.SelectedIndex = (int)table;
        updating = false;
        if (find.Text.Length > 0) find.Text = "";   // its TextChanged refills the list
        Refresh();
    }

    /// <summary>Redraws the list and the selected record from the state.</summary>
    public void Refresh()
    {
        FillEntries();
        ShowRecord();
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

    void FillEntries()
    {
        updating = true;
        entries.Items.Clear();
        if (opened() is { State.Document: { Requirements: { } requirements } doc })
        {
            string query = find.Text.Trim();
            ListViewItem? selected = null;
            for (int id = 0; id < ReqTables.IdCount(table); id++)
            {
                string name = Name(id);
                if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase) && id.ToString() != query) continue;
                var record = requirements[table].Find(id);
                string state = record is null ? "  (none)" : record.Used ? "" : "  (off)";
                string dot = doc.RecordDiffersFromVanilla(table, id) ? " •" : "";
                var text = new TextBlock { Text = $"{id,3}  {name}{state}{dot}" };
                if (doc.RecordChangedSinceSave(table, id)) text.Foreground = Accent();
                if (record is null) text.Opacity = 0.5;
                var item = new ListViewItem { Tag = id, Content = text };
                entries.Items.Add(item);
                if (id == selectedId) selected = item;
            }
            if (selected is not null)
            {
                entries.SelectedItem = selected;
                entries.ScrollIntoView(selected);
            }
        }
        updating = false;
    }

    void ShowRecord()
    {
        updating = true;
        lines.Items.Clear();
        rows = [];
        var exe = opened();
        var doc = exe?.State.Document;
        problemBar.Visibility = doc?.RequirementsProblem is null ? Visibility.Collapsed : Visibility.Visible;
        problemText.Text = doc?.RequirementsProblem is string problem
            ? $"{RequirementsFile.ArchivePath} in the exe can't be read ({problem}). The tables shown are what an exe without it opens as; " +
              "Replace it makes them editable, and the next Save writes a new file."
            : "";
        bool editable = doc?.Requirements is not null && doc.RequirementsProblem is null;
        var record = doc?.Requirements?[table].Find(selectedId);
        title.Text = doc?.Requirements is null ? "No requirements" : $"{ReqTables.Title(table)}: {selectedId}  {Name(selectedId)}";
        note.Text = doc?.Requirements is null ? ""
            : record is null ? "No record: the game allows this nowhere. Add record to give it requirements."
            : !record.Used ? "Used is off: the game leaves this record out and allows it nowhere."
            : "";
        used.IsChecked = record?.Used ?? false;
        used.IsEnabled = editable && record is not null;
        if (record is not null)
        {
            rows = RequirementEdits.Rows(record);
            var units = names(DatId.Units);
            var techs = names(DatId.Techdata);
            string Unit(int u) => u < units.Count ? units[u] : $"unit {u}";
            string Tech(int t) => t < techs.Count ? techs[t] : $"tech {t}";
            foreach (var row in rows)
            {
                var text = row.LevelHeader is int level
                    ? new TextBlock { Text = $"Level {level}", FontWeight = FontWeights.SemiBold }
                    : new TextBlock { Text = (row.At!.Value.Level > 0 ? "      " : "") + ReqOpcodes.Describe(table, row.Line!.Value, Unit, Tech) };
                lines.Items.Add(new ListViewItem { Content = text });
            }
        }
        addRecord.IsEnabled = editable && record is null;
        deleteRecord.IsEnabled = editable && record is not null;
        revert.IsEnabled = editable && doc!.RecordDiffersFromVanilla(table, selectedId);
        updating = false;
        EnableLineButtons();
    }

    ReqRow? SelectedRow => lines.SelectedIndex >= 0 && lines.SelectedIndex < rows.Count ? rows[lines.SelectedIndex] : null;

    ReqRecord? Record => opened()?.State.Document is { RequirementsProblem: null, Requirements: { } r } ? r[table].Find(selectedId) : null;

    void EnableLineButtons()
    {
        bool editable = Record is not null;
        var row = SelectedRow;
        add.IsEnabled = insert.IsEnabled = editable;
        change.IsEnabled = up.IsEnabled = down.IsEnabled = editable && row?.At is not null;
        // A level's header goes only with the last level.
        remove.IsEnabled = editable && (row?.At is not null || row?.LevelHeader is int level && level == Record!.Levels[^1].Level);
    }

    void Run(Func<EditorState, string, bool> command)
    {
        if (opened() is not { } exe) return;
        if (command(exe.State, Name(selectedId))) changed();
    }

    void Edit(Func<ReqRecord, ReqRecord> edit, ReqLineAt? select = null)
    {
        if (opened() is not { } exe) return;
        if (!exe.State.EditRequirement(table, selectedId, edit, Name(selectedId))) return;
        changed();
        if (select is { } at)
        {
            int index = rows.ToList().FindIndex(r => r.At == at);
            if (index >= 0) lines.SelectedIndex = index;
        }
    }

    /// <summary>The level the selection is in (a header: its level; nothing: the record's own lines).</summary>
    int SelectedLevel => SelectedRow is { } row ? row.LevelHeader ?? row.At!.Value.Level : 0;

    async Task AddLine(bool insertBefore)
    {
        if (Record is not { } record) return;
        int next = record.Levels.Count == 0 ? 1 : record.Levels[^1].Level + 1;
        int? offerLevel = table == ReqTable.Upgrades && next <= 3 ? next : null;
        var choice = await new ReqLineDialog(Root.XamlRoot, table, null, offerLevel, names).ShowAsync();
        if (choice is null) return;
        if (choice.AddLevel)
        {
            Edit(RequirementEdits.AddLevel);
            return;
        }
        int level = SelectedLevel;
        int count = level == 0 ? record.Lines.Count : record.Levels.First(l => l.Level == level).Lines.Count;
        int index = insertBefore ? SelectedRow?.At?.Index ?? 0 : count;
        var at = new ReqLineAt(level, index);
        Edit(r => RequirementEdits.Insert(r, at, choice.Line!.Value), at);
    }

    async Task ChangeLine()
    {
        if (SelectedRow is not { At: { } at, Line: { } line }) return;
        var choice = await new ReqLineDialog(Root.XamlRoot, table, line, offerLevel: null, names).ShowAsync();
        if (choice?.Line is not { } replaced) return;
        Edit(r => RequirementEdits.Replace(r, at, replaced), at);
    }

    void RemoveSelected()
    {
        if (SelectedRow is not { } row) return;
        if (row.At is { } at) Edit(r => RequirementEdits.Remove(r, at));
        else Edit(RequirementEdits.RemoveLastLevel);
    }

    void MoveSelected(int delta)
    {
        if (SelectedRow is not { At: { } at }) return;
        Edit(r => RequirementEdits.Move(r, at, delta), at with { Index = at.Index + delta });
    }
}
