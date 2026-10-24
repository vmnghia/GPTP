using ManifoldEditor.App.Services;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Button = Manifold.Core.Data.Button;

namespace ManifoldEditor.App;

/// <summary>
/// Add new button's picker (QoL spec §3): a search and the templates (Blank, then every
/// distinct button of the exe and vanilla), each with its icon, name, action and uses.
/// </summary>
sealed class ButtonPicker
{
    public Flyout Flyout { get; }

    /// <summary>A template was picked (its slot is 0; the caller places it).</summary>
    public event Action<Button>? Picked;

    readonly TextBox query = new() { PlaceholderText = "Search: tooltip, action, condition, icon name or number" };
    readonly ListView list = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None, Height = 400, Width = 520 };
    IReadOnlyList<ButtonTemplate> all = [];
    Func<string, IReadOnlyList<ButtonTemplate>> filter = _ => [];
    Func<uint, string> actionName = _ => "";
    IconCache? icons;

    public ButtonPicker()
    {
        Flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { query, list } } };
        query.TextChanged += (_, _) => Show();
        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not FrameworkElement { Tag: ButtonTemplate template }) return;
            Flyout.Hide();
            Picked?.Invoke(template.Button);
        };
    }

    /// <summary>The list to pick from, before the flyout shows; the search starts empty.</summary>
    public void Fill(IReadOnlyList<ButtonTemplate> templates, Func<string, IReadOnlyList<ButtonTemplate>> filterBy,
        Func<uint, string> actionNameOf, IconCache? iconCache)
    {
        all = templates;
        filter = filterBy;
        actionName = actionNameOf;
        icons = iconCache;
        if (query.Text.Length > 0) query.Text = "";   // raises TextChanged, which shows the list
        else Show();
    }

    void Show()
    {
        list.Items.Clear();
        foreach (var template in query.Text.Trim().Length == 0 ? all : filter(query.Text))
        {
            var row = new Grid { ColumnSpacing = 8, Tag = template };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (icons?.View(template.Button.Icon, 28) is { } icon) row.Children.Add(icon);
            string uses = template.Uses == 0 ? "vanilla only" : $"in {template.Uses} set{(template.Uses == 1 ? "" : "s")}";
            var text = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = template.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = template.Button == ButtonTemplates.Blank ? "Always, Move: fill it in" : $"{actionName(template.Button.Action)} · {uses}",
                        FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Items.Add(row);
        }
    }
}
