using ManifoldEditor.App.Services;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ManifoldEditor.App;

/// <summary>
/// The icon picker (QoL spec §5): a search by name or number, Unused, and the grid of
/// cmdicons.grp's frames (the mod exe's own first). Tiles are made afresh on each search
/// (the bitmaps are cached in IconCache): a GridView may still hold an old tile when it is
/// added again, which throws.
/// </summary>
sealed class IconPicker
{
    public Flyout Flyout { get; }

    /// <summary>A frame was picked.</summary>
    public event Action<int>? Picked;

    /// <summary>The icons the exe's sets use now, asked on each opening and each tick of Unused.</summary>
    public Func<IReadOnlySet<int>> Used { get; init; } = () => new HashSet<int>();

    readonly IconCache icons;
    readonly IReadOnlyList<string> names;
    readonly TextBox query = new() { PlaceholderText = "Search icons: name or number" };
    readonly CheckBox unused = new() { Content = "Unused" };
    readonly GridView grid = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None, Width = 560, Height = 420 };

    public IconPicker(IconCache icons, IReadOnlyList<string> names)
    {
        this.icons = icons;
        this.names = names;
        Flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { query, unused, grid } } };
        Flyout.Opening += (_, _) => Fill();
        query.TextChanged += (_, _) => Fill();
        unused.Click += (_, _) => Fill();
        grid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not FrameworkElement { Tag: int frame }) return;
            Flyout.Hide();
            Picked?.Invoke(frame);
        };
    }

    int Count => Math.Max(icons.Count, names.Count);

    void Fill()
    {
        var shown = IconSearch.Filter(Count, names, query.Text, unused.IsChecked == true, unused.IsChecked == true ? Used() : new HashSet<int>());
        grid.Items.Clear();
        foreach (int frame in shown) grid.Items.Add(Tile(frame));
    }

    FrameworkElement Tile(int frame)
    {
        var tile = new StackPanel { Width = 64, Tag = frame };
        if (icons.View(frame, 36) is { } icon) tile.Children.Add(icon);
        tile.Children.Add(new TextBlock
        {
            Text = frame < names.Count ? $"{frame} {names[frame]}" : frame.ToString(),
            FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        ToolTipService.SetToolTip(tile, frame < names.Count ? names[frame] : frame.ToString());
        return tile;
    }
}
