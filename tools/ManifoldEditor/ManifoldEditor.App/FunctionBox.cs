using Manifold.Core.Data;
using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ManifoldEditor.App;

/// <summary>
/// The Condition or Action field (QoL spec §4): shows the current function; typing narrows
/// the list to names or addresses holding the text (FunctionSearch). Enter or a click picks,
/// Esc or leaving the box without a pick puts the current label back and changes nothing.
/// </summary>
sealed class FunctionBox
{
    public AutoSuggestBox Root { get; }

    /// <summary>A function was picked: its address.</summary>
    public event Action<uint>? Picked;

    FunctionTable? table;
    Func<uint, string> label = _ => "";
    string current = "";

    /// <summary>A list row: the function, shown (and put in the box) as its label.</summary>
    sealed record Item(GameFunction Function, string Label)
    {
        public override string ToString() => Label;
    }

    public FunctionBox(string header)
    {
        Root = new AutoSuggestBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch, UpdateTextOnSelect = true };
        Root.GotFocus += (_, _) => Open();
        // A click on the box while it has focus (right after a pick) opens the list again.
        Root.AddHandler(UIElement.TappedEvent, new TappedEventHandler((_, _) =>
        {
            if (!Root.IsSuggestionListOpen) Open();
        }), handledEventsToo: true);
        Root.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput || table is null) return;
            Root.ItemsSource = Items(Root.Text);
            Root.IsSuggestionListOpen = true;
        };
        Root.QuerySubmitted += (_, e) =>
        {
            var items = Items(e.QueryText);
            if ((e.ChosenSuggestion as Item ?? (items.Count == 1 ? items[0] : null)) is { } chosen) Pick(chosen);
            else Restore(close: true);
        };
        // Only the text goes back: a click on the list may still be on its way, and it picks
        // from the list, not from the text.
        Root.LostFocus += (_, _) => Restore(close: false);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == VirtualKey.Escape) Restore(close: true);
        }), handledEventsToo: true);
    }

    /// <summary>The box for a button: its function's label, the table to search.</summary>
    public void Show(FunctionTable functions, uint address, Func<uint, string> labelOf)
    {
        table = functions;
        label = labelOf;
        current = labelOf(address);
        Root.Text = current;
    }

    /// <summary>No button: empty, nothing to search.</summary>
    public void Clear()
    {
        table = null;
        current = "";
        Root.Text = "";
        Root.ItemsSource = null;
    }

    /// <summary>The list, all of it unless a search is under way; the text selected to type over.</summary>
    void Open()
    {
        if (table is null) return;
        if (Root.Text == current)
        {
            Root.ItemsSource = Items("");
            // After the click that gave the focus has placed the caret, or it undoes the selection.
            Root.DispatcherQueue.TryEnqueue(() => Find<TextBox>(Root)?.SelectAll());
        }
        Root.IsSuggestionListOpen = true;
    }

    IReadOnlyList<Item> Items(string query) =>
        table is null ? [] : FunctionSearch.Filter(table, query).Select(f => new Item(f, label(f.Address))).ToArray();

    void Pick(Item item)
    {
        current = item.Label;
        Root.Text = current;
        Root.IsSuggestionListOpen = false;
        Picked?.Invoke(item.Function.Address);
    }

    void Restore(bool close)
    {
        if (Root.Text != current) Root.Text = current;
        if (close) Root.IsSuggestionListOpen = false;
    }

    static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
