using ManifoldEditor.App.Services;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Button = Microsoft.UI.Xaml.Controls.Button;

namespace ManifoldEditor.App;

/// <summary>
/// The Strings page (spec §6): every string of stat_txt.tbl as PyTBL lists it, with search,
/// go to (#id), filters, the string's fields and preview, where it is used, Add and Revert.
/// Commands go through EditorState; the owner redraws after one.
/// </summary>
sealed class StringsPage
{
    public Grid Root { get; } = new() { ColumnSpacing = 16 };

    readonly Func<OpenedExe?> opened;
    readonly Action changed;
    readonly Func<string, string, Task> message;
    readonly Action<ButtonRef> jump;

    readonly TextBox search = new() { PlaceholderText = "Search text, or #id to go to" };
    readonly ComboBox filter = new() { MinWidth = 160 };
    readonly CheckBox matchCase = new() { Content = "Match case" };
    readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single };
    readonly TextBlock count = new() { FontSize = 12, Opacity = 0.75 };
    readonly StringEditorView editor = new(offerCopy: false);
    readonly StackPanel usedBy = new() { Spacing = 2 };
    readonly Button revert = new() { Content = "Revert string" };
    readonly TextBlock budget = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    readonly HashSet<int> unlocked = new();

    int selectedId;
    bool updating;

    public StringsPage(Func<OpenedExe?> opened, Action changed, Func<string, string, Task> message, Action<ButtonRef> jump)
    {
        this.opened = opened;
        this.changed = changed;
        this.message = message;
        this.jump = jump;

        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var name in new[] { "All strings", "Used by buttons", "Unit names", "Edited" }) filter.Items.Add(name);
        filter.SelectedIndex = 0;
        var tools = new Grid { ColumnSpacing = 8 };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.Children.Add(search);
        Grid.SetColumn(filter, 1);
        tools.Children.Add(filter);
        Grid.SetColumn(matchCase, 2);
        tools.Children.Add(matchCase);
        left.Children.Add(tools);
        list.FontFamily = new FontFamily("Consolas");
        Grid.SetRow(list, 1);
        left.Children.Add(list);
        Grid.SetRow(count, 2);
        left.Children.Add(count);
        Root.Children.Add(left);

        search.TextChanged += (_, _) => FillList();
        filter.SelectionChanged += (_, _) => FillList();
        matchCase.Click += (_, _) => FillList();
        list.SelectionChanged += (_, _) =>
        {
            if (updating || list.SelectedItem is not StringRow row) return;
            selectedId = row.Id;
            ShowSelected();
        };

        var right = new StackPanel { Spacing = 10 };
        editor.Committed += bytes => Edit(bytes);
        editor.EditForAll += () => { unlocked.Add(selectedId); ShowSelected(); };
        right.Children.Add(editor.Root);
        right.Children.Add(new TextBlock { Text = "Used by", FontWeight = FontWeights.SemiBold });
        right.Children.Add(usedBy);
        var add = new Button { Content = "Add string" };
        add.Click += async (_, _) => await Add();
        revert.Click += (_, _) => { if (opened()?.State.RevertString(selectedId) == true) changed(); };
        right.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { add, revert } });
        right.Children.Add(budget);
        var scroll = new ScrollViewer { Content = right };
        Grid.SetColumn(scroll, 1);
        Root.Children.Add(scroll);
    }

    StringFilter Filter => (StringFilter)Math.Max(0, filter.SelectedIndex);

    /// <summary>Shows the string (after an undo, or a jump from the button panel).</summary>
    public void Select(int id)
    {
        selectedId = id;
        if (!search.Text.StartsWith('#') && search.Text.Length > 0 || Filter != StringFilter.All)
        {
            search.Text = "";
            filter.SelectedIndex = 0;
        }
        Refresh();
    }

    /// <summary>Redraws the list and the selected string from the state.</summary>
    public void Refresh()
    {
        FillList();
        ShowSelected();
    }

    void FillList()
    {
        if (opened() is not { } exe || exe.State.Strings is not StringTable table)
        {
            list.ItemsSource = null;
            count.Text = "No stat_txt.tbl was found: strings can't be edited.";
            return;
        }
        updating = true;
        var rows = StringList.Rows(table, exe.State.Document.Sets, search.Text, matchCase.IsChecked == true, Filter,
            id => exe.Catalog.Entries[id].Name);
        list.ItemsSource = rows;
        var selected = rows.FirstOrDefault(r => r.Id == selectedId) ?? (search.Text.Trim().StartsWith('#') ? rows.FirstOrDefault() : null);
        if (selected is not null)
        {
            selectedId = selected.Id;
            list.SelectedItem = selected;
            list.ScrollIntoView(selected);
        }
        count.Text = $"{rows.Count} of {table.Count} strings";
        updating = false;
        ShowSelected();
    }

    void ShowSelected()
    {
        usedBy.Children.Clear();
        var exe = opened();
        if (exe?.State.Strings is not StringTable table || selectedId < 1 || selectedId > table.Count)
        {
            editor.ShowNone(exe?.State.Strings is null ? "No stat_txt.tbl" : "Select a string");
            revert.IsEnabled = false;
            budget.Text = exe?.State.Strings is StringTable t ? Budget(t, exe) : "";
            return;
        }
        var sets = exe.State.Document.Sets;
        var refs = StringUses.Buttons(sets, selectedId);
        string uses = StringList.UsesLabel(selectedId, refs, sets, id => exe.Catalog.Entries[id].Name);
        bool shared = refs.Count + (StringUses.UnitOf(selectedId) is null ? 0 : 1) > 1;
        editor.Show($"String {selectedId}" + (table.IsEdited(selectedId) ? " (edited)" : ""), table.Segment(selectedId),
            StringList.IsHotkeyString(sets, selectedId), shared ? uses : "", shared && !unlocked.Contains(selectedId),
            exe.Resources.TextColours);

        if (StringUses.UnitOf(selectedId) is int unit)
            usedBy.Children.Add(new TextBlock { Text = $"The name of unit {unit} ({exe.Catalog.Entries[unit].Name})", FontSize = 12 });
        foreach (var r in refs)
        {
            var link = new HyperlinkButton
            {
                Content = $"{exe.Catalog.Entries[r.SetId].Name} (set {r.SetId}), position {sets[r.SetId].Buttons[r.Index].Position}" +
                          (r.Field == StringField.Disabled ? ", disabled text" : ""),
                Padding = new Thickness(0),
            };
            link.Click += (_, _) => jump(r);
            usedBy.Children.Add(link);
        }
        if (usedBy.Children.Count == 0)
            usedBy.Children.Add(new TextBlock { Text = "No button or unit name the editor knows of (.dat files may use it).", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        revert.IsEnabled = exe.State.Document.OpenedStrings is StringTable o && selectedId <= o.Count && table.IsEdited(selectedId);
        budget.Text = Budget(table, exe);
    }

    static string Budget(StringTable table, OpenedExe exe) =>
        $"{table.BytesFree:N0} bytes left of the 65,536 a .tbl can address. " +
        $"{table.Count:N0} strings, from {exe.Resources.Sources.FirstOrDefault(s => s.StartsWith(StringTable.ArchivePath)) ?? StringTable.ArchivePath}.";

    void Edit(byte[] bytes)
    {
        if (opened() is not { } exe) return;
        if (exe.State.EditString(selectedId, bytes, out var refused)) changed();
        else if (refused is not null) editor.ShowError(refused);
    }

    async Task Add()
    {
        if (opened() is not { } exe) return;
        if (exe.State.AddString(out var refused) is int id)
        {
            Select(id);
            changed();
        }
        else if (refused is not null) await message("Not added", refused);
    }
}
