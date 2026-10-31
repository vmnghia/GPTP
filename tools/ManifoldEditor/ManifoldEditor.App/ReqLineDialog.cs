using System.Globalization;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ManifoldEditor.App;

/// <summary>What the line dialog chose: a line, or (for an upgrade without them) the three level headers.</summary>
sealed record ReqLineChoice(ReqLine? Line, bool AddLevels);

/// <summary>
/// The requirement line dialog (requirements spec §6): the opcode, from those the table's
/// parser reads, then the unit or tech it takes, searchable. For an upgrade without levels it
/// can also add the three level headers, which come as a set.
/// </summary>
sealed class ReqLineDialog
{
    readonly ContentDialog dialog;
    readonly ComboBox opcode = new() { MinWidth = 360 };
    readonly TextBox find = new() { PlaceholderText = "Name or id" };
    readonly ListView parameter = new() { SelectionMode = ListViewSelectionMode.Single, Height = 260, FontFamily = new FontFamily("Consolas") };
    readonly IReadOnlyList<ReqOpcode> offered;
    readonly Func<DatId, IReadOnlyList<string>> names;
    readonly int levelChoice;   // the index of "Levels 1-3" in the opcode list; -1 when not offered
    ReqParam shownParam = ReqParam.None;
    ReqLineChoice? result;

    /// <param name="offerLevels">Offer the three level headers (an upgrade's record that has none).</param>
    public ReqLineDialog(XamlRoot root, ReqTable table, ReqLine? current, bool offerLevels, Func<DatId, IReadOnlyList<string>> names)
    {
        this.names = names;
        offered = ReqOpcodes.Offered(table);
        foreach (var o in offered) opcode.Items.Add(o.Name);
        levelChoice = -1;
        if (offerLevels)
        {
            levelChoice = opcode.Items.Count;
            opcode.Items.Add("Levels 1-3 (the upgrade's level headers)");
        }

        var content = new StackPanel { Spacing = 8, MinWidth = 380 };
        content.Children.Add(opcode);
        content.Children.Add(find);
        content.Children.Add(parameter);
        dialog = new ContentDialog
        {
            XamlRoot = root, Title = current is null ? "Add requirement" : "Change requirement", Content = content,
            PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };

        opcode.SelectionChanged += (_, _) => ShowParameter(null);
        find.TextChanged += (_, _) => FillParameter(null);
        parameter.SelectionChanged += (_, _) => Enable();
        dialog.PrimaryButtonClick += (_, args) =>
        {
            result = Choice();
            if (result is null) args.Cancel = true;
        };

        int start = 0;
        if (current is { } line)
        {
            int at = line.Word < 0xFF00 ? 0 : offered.ToList().FindIndex(o => o.Word == line.Word && o.Word != ReqOpcodes.MustHave);
            start = Math.Max(0, at);
        }
        opcode.SelectedIndex = start;
        ShowParameter(current is { } c ? (c.Word < 0xFF00 ? c.Word : c.Param) : null);
    }

    public async Task<ReqLineChoice?> ShowAsync()
    {
        await dialog.ShowAsync();
        return result;
    }

    ReqOpcode? Selected => opcode.SelectedIndex >= 0 && opcode.SelectedIndex < offered.Count ? offered[opcode.SelectedIndex] : null;

    void ShowParameter(int? select)
    {
        shownParam = Selected?.Param ?? ReqParam.None;
        var visible = shownParam == ReqParam.None ? Visibility.Collapsed : Visibility.Visible;
        find.Visibility = parameter.Visibility = visible;
        FillParameter(select);
    }

    void FillParameter(int? select)
    {
        parameter.Items.Clear();
        if (shownParam != ReqParam.None)
        {
            var list = names(shownParam == ReqParam.Tech ? DatId.Techdata : DatId.Units);
            string query = find.Text.Trim();
            for (int id = 0; id < list.Count; id++)
            {
                if (query.Length > 0 && !list[id].Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    id.ToString(CultureInfo.InvariantCulture) != query) continue;
                var item = new ListViewItem { Tag = id, Content = $"{id,3}  {list[id]}" };
                parameter.Items.Add(item);
                if (id == select)
                {
                    parameter.SelectedItem = item;
                    parameter.ScrollIntoView(item);
                }
            }
        }
        Enable();
    }

    void Enable() => dialog.IsPrimaryButtonEnabled = Choice() is not null;

    /// <summary>The choice as the controls stand; null while a needed parameter isn't picked.</summary>
    ReqLineChoice? Choice()
    {
        if (opcode.SelectedIndex == levelChoice && levelChoice >= 0) return new ReqLineChoice(null, AddLevels: true);
        if (Selected is not { } o) return null;
        if (o.Param == ReqParam.None) return new ReqLineChoice(new ReqLine(o.Word, null), false);
        if (parameter.SelectedItem is not ListViewItem { Tag: int id }) return null;
        return o.Word == ReqOpcodes.MustHave
            ? new ReqLineChoice(new ReqLine((ushort)id, null), false)
            : new ReqLineChoice(new ReqLine(o.Word, (ushort)id), false);
    }
}
