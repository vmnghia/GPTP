using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Editor;

/// <summary>
/// What the window shows and does, without the window: the selected set, the clipboards,
/// every command, the title and the check box. Each command returns whether it changed
/// anything, so the window knows to redraw.
/// </summary>
public sealed class EditorState(ButtonSetDocument document, string exeName, Func<ushort, string?> strings,
    FunctionTable conditions, FunctionTable actions)
{
    Button? clipboardButton;
    ButtonSet? clipboardSet;

    public ButtonSetDocument Document { get; } = document;
    public string ExeName { get; } = exeName;
    public int SelectedSetId { get; set; }
    public ButtonSet SelectedSet => Document[SelectedSetId];
    public bool HasButtonClipboard => clipboardButton is not null;
    public bool HasSetClipboard => clipboardSet is not null;
    public FunctionTable Conditions => conditions;
    public FunctionTable Actions => actions;

    public string Title => $"Manifold Editor - {ExeName}" + (Document.IsDirty ? " *" : "");

    bool Apply(Func<ButtonSet, ButtonSet> edit, string label) => Document.Apply(SelectedSetId, edit, label);

    static bool IsPosition(ushort position) => position >= 1 && position <= Card.MaxPosition;
    bool IsIndex(int index) => index >= 0 && index < SelectedSet.Buttons.Count;

    /// <summary>A cell dropped on another: move (a filled target swaps), or copy with Ctrl.</summary>
    public bool Drop(ushort from, ushort to, bool copy) =>
        IsPosition(from) && IsPosition(to) &&
        Apply(s => copy ? CardEdits.CopyCell(s, from, to) : CardEdits.MoveCell(s, from, to), copy ? "Copy" : "Move");

    /// <summary>
    /// Drop, then the button to show in the panel: a copy lands as new buttons at the end of
    /// the set (the first of them); a move keeps the dragged button's place in the list.
    /// Null when nothing changed.
    /// </summary>
    public int? DropAndSelect(ushort from, ushort to, bool copy)
    {
        if (!IsPosition(from)) return null;
        int dragged = -1;
        int draggedCount = 0;
        for (int i = 0; i < SelectedSet.Buttons.Count; i++)
            if (SelectedSet.Buttons[i].Position == from)
            {
                if (dragged < 0) dragged = i;
                draggedCount++;
            }
        if (dragged < 0 || !Drop(from, to, copy)) return null;
        return copy ? SelectedSet.Buttons.Count - draggedCount : dragged;
    }

    public void CopyButton(int index)
    {
        if (IsIndex(index)) clipboardButton = SelectedSet.Buttons[index];
    }

    public bool PasteButton(ushort position) =>
        clipboardButton is Button button && IsPosition(position) &&
        Apply(s => CardEdits.Paste(s, button, position), "Paste");

    public bool DeleteButton(int index) => IsIndex(index) && Apply(s => CardEdits.Delete(s, index), "Delete");

    public bool EditButton(int index, Button button) =>
        IsIndex(index) && IsPosition(button.Position) && Apply(s => CardEdits.Replace(s, index, button), "Edit button");

    public void CopySet() => clipboardSet = SelectedSet;

    public bool PasteSet() =>
        clipboardSet is ButtonSet source && Apply(s => CardEdits.PasteSet(s, source), "Paste set");

    public bool Revert(bool toVanilla) =>
        toVanilla ? Document.RevertToVanilla(SelectedSetId) : Document.RevertToOpened(SelectedSetId);

    /// <summary>Undo, then show the set it changed.</summary>
    public bool Undo() => Select(Document.Undo());

    public bool Redo() => Select(Document.Redo());

    bool Select(int? setId)
    {
        if (setId is not int id) return false;
        SelectedSetId = id;
        return true;
    }

    /// <summary>A button's name: its enabled string without the hotkey, or its index.</summary>
    public string NameOf(int index)
    {
        string name = ButtonText.Display(strings(SelectedSet.Buttons[index].EnabledString));
        return name.Length > 0 ? name : $"button {index + 1}";
    }

    /// <summary>One line per button, in order: position, hotkey, name.</summary>
    public IReadOnlyList<string> ButtonLines()
    {
        var keys = CardChecks.Hotkeys(SelectedSet, strings);
        return Enumerable.Range(0, SelectedSet.Buttons.Count)
            .Select(i => $"{SelectedSet.Buttons[i].Position}  {(keys[i] is null ? " " : ButtonText.HotkeyLabel(keys[i]))}  {NameOf(i)}")
            .ToArray();
    }

    /// <summary>The check box's warnings: hotkey clashes, then buttons replays won't show.</summary>
    public IReadOnlyList<string> CheckLines()
    {
        var lines = new List<string>();
        foreach (var clash in CardChecks.Clashes(SelectedSet, strings))
            lines.Add($"{ButtonText.HotkeyLabel(clash.Hotkey)}: " + string.Join(", ", clash.ButtonIndexes.Select(Describe)));
        var hidden = CardChecks.HiddenInReplays(SelectedSet);
        if (hidden.Count > 0)
            lines.Add("Not shown in replays: " + string.Join(", ", hidden.Select(Describe)));
        return lines;
    }

    string Describe(int index) => $"{NameOf(index)} ({SelectedSet.Buttons[index].Position})";

    public string ConditionLabel(uint address) => Label(conditions, address);
    public string ActionLabel(uint address) => Label(actions, address);

    static string Label(FunctionTable table, uint address) =>
        table.ByAddress(address) is { } f ? $"{f.Name} (0x{address:X6})" : $"0x{address:X8}";

    /// <summary>A string id with its text, for the button panel.</summary>
    public string StringLabel(ushort id) =>
        id == 0 ? "0 (none)" : $"{id}: {ButtonText.Clean(strings(id)) switch { "" => "(no text)", var t => t }}";
}
