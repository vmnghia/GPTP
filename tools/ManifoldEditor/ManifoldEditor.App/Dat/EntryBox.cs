using Manifold.Core.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ManifoldEditor.App.Dat;

/// <summary>
/// A reference field's dropdown (spec §6): shows the entry's name; typing narrows the list
/// to the names holding the text, or the id it spells (<see cref="EntrySearch.Filter"/>).
/// Enter or a click picks, Esc or leaving the box without a pick puts the name back and
/// changes nothing. <see cref="FunctionBox"/>'s pattern over a name list.
/// </summary>
sealed class EntryBox
{
    public AutoSuggestBox Root { get; }

    /// <summary>An entry was picked: its id (an index into the names shown).</summary>
    public event Action<int>? Picked;

    IReadOnlyList<string> names = [];
    string current = "";

    /// <summary>A list row: the entry, shown (and put in the box) as its name.</summary>
    sealed record Item(int Id, string Label)
    {
        public override string ToString() => Label;
    }

    public EntryBox(double width = 260)
    {
        Root = new AutoSuggestBox { Width = width, UpdateTextOnSelect = true, VerticalAlignment = VerticalAlignment.Center };
        Root.GotFocus += (_, _) => Open();
        // A click on the box while it has focus (right after a pick) opens the list again.
        Root.AddHandler(UIElement.TappedEvent, new TappedEventHandler((_, _) =>
        {
            if (!Root.IsSuggestionListOpen) Open();
        }), handledEventsToo: true);
        Root.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput || !Root.IsEnabled) return;
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

    /// <summary>
    /// The box for an entry: its name in <paramref name="names"/>, or "#300 (no name)" for an
    /// id past the list's end, and the list to search. <paramref name="unlisted"/> replaces the
    /// shown text (for an id whose own index in the list is another entry).
    /// </summary>
    public void Show(IReadOnlyList<string> names, long id, string? unlisted = null)
    {
        this.names = names;
        current = unlisted ?? Label(names, id);
        Root.Text = current;
        Root.ItemsSource = null;
    }

    /// <summary>No value (the field doesn't cover the entry, or no file): empty, nothing to search.</summary>
    public void Clear()
    {
        names = [];
        current = "";
        Root.Text = "";
        Root.ItemsSource = null;
    }

    /// <summary>The name shown for an id: the list's, or "#300 (no name)" past its end.</summary>
    public static string Label(IReadOnlyList<string> names, long id) =>
        id >= 0 && id < names.Count ? names[(int)id] : $"#{id} (no name)";

    /// <summary>The list, all of it unless a search is under way; the text selected to type over.</summary>
    void Open()
    {
        if (names.Count == 0 || !Root.IsEnabled) return;
        if (Root.Text == current)
        {
            Root.ItemsSource = Items("");
            // After the click that gave the focus has placed the caret, or it undoes the selection.
            Root.DispatcherQueue.TryEnqueue(() => Find<TextBox>(Root)?.SelectAll());
        }
        Root.IsSuggestionListOpen = true;
    }

    IReadOnlyList<Item> Items(string query) =>
        EntrySearch.Filter(names, query).Select(id => new Item(id, names[id])).ToArray();

    void Pick(Item item)
    {
        current = item.Label;
        Root.Text = current;
        Root.IsSuggestionListOpen = false;
        Picked?.Invoke(item.Id);
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
