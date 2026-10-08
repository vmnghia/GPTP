using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Editor;

/// <summary>
/// What the window shows and does, without the window: the selected set, the clipboards,
/// every command, the title and the check box. Each command returns whether it changed
/// anything, so the window knows to redraw. Strings are read from the document, so names
/// and hotkeys follow string edits.
/// </summary>
public sealed class EditorState(ButtonSetDocument document, string exeName,
    FunctionTable conditions, FunctionTable actions)
{
    Button? clipboardButton;
    ButtonSet? clipboardSet;

    public ButtonSetDocument Document { get; } = document;
    public string ExeName { get; } = exeName;
    public int SelectedSetId { get; set; }
    /// <summary>The string the last undo or redo changed, for the window to show; null for a set-only step.</summary>
    public int? LastStringId { get; private set; }
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

    /// <summary>Undo, then show the set it changed (and note the string, <see cref="LastStringId"/>).</summary>
    public bool Undo() => Select(Document.Undo());

    public bool Redo() => Select(Document.Redo());

    bool Select(Place? place)
    {
        if (place is not Place p) return false;
        if (p.SetId is int id) SelectedSetId = id;
        LastStringId = p.StringId;
        return true;
    }

    // Strings (spec §5, §6).

    public StringTable? Strings => Document.Strings;

    /// <summary>A string as part 1 shows it (first part; hotkey, type and text joined), or null.</summary>
    public string? Text(ushort id) => Document.Strings is StringTable t ? StatTxt.Of(t).Get(id) : null;

    /// <summary>The other button fields pointing at the string this field points at.</summary>
    public IReadOnlyList<ButtonRef> SharedWith(int index, StringField field)
    {
        if (!IsIndex(index)) return [];
        int id = StringUses.IdOf(SelectedSet.Buttons[index], field);
        if (id == 0) return [];
        var self = new ButtonRef(SelectedSetId, index, field);
        return StringUses.Buttons(Document.Sets, id).Where(r => r != self).ToArray();
    }

    /// <summary>The table, or null with the reason when it would no longer fit in 16-bit offsets.</summary>
    static StringTable? Fitting(StringTable table, out string? refused)
    {
        if (table.FirstOverLimit() is int id)
        {
            refused = $"That doesn't fit: string {id} would start past byte 65,535, the most a .tbl can address " +
                      $"({-table.BytesFree} bytes over). Shorten a string first.";
            return null;
        }
        refused = null;
        return table;
    }

    public bool EditString(int id, byte[] bytes, out string? refused)
    {
        refused = null;
        if (Strings is not StringTable t || id < 1 || id > t.Count) return false;
        if (Fitting(t.With(id, bytes), out refused) is not StringTable after) return false;
        return Document.ApplyStrings(_ => after, id, "Edit string");
    }

    /// <summary>
    /// A new string with the same bytes as the field's, the field pointed at it: one step.
    /// Returns the new id, or null.
    /// </summary>
    public int? CopyStringForButton(int index, StringField field, out string? refused)
    {
        refused = null;
        if (!IsIndex(index) || Strings is not StringTable t) return null;
        int id = StringUses.IdOf(SelectedSet.Buttons[index], field);
        if (id < 1 || id > t.Count) return null;
        return PointAtNew(index, field, t.Segment(id), "Make a separate copy", out refused);
    }

    /// <summary>The bytes a new string starts with: a hotkey string for the enabled field.</summary>
    public static byte[] NewStringBytes(StringField field) =>
        field == StringField.Enabled ? [(byte)'?', 0, .. "New string"u8, 0] : [.. "New string"u8, 0];

    /// <summary>An empty field (id 0) given a new string. Returns its id, or null.</summary>
    public int? NewStringForButton(int index, StringField field, out string? refused)
    {
        refused = null;
        if (!IsIndex(index) || StringUses.IdOf(SelectedSet.Buttons[index], field) != 0) return null;
        return PointAtNew(index, field, NewStringBytes(field), "New string", out refused);
    }

    int? PointAtNew(int index, StringField field, byte[] bytes, string label, out string? refused)
    {
        refused = null;
        if (Strings is not StringTable t) return null;
        var (added, newId) = t.Add(bytes);
        if (Fitting(added, out refused) is null) return null;
        bool changed = Document.ApplyBoth(SelectedSetId, (set, _) =>
            (CardEdits.Replace(set, index, StringUses.WithId(set.Buttons[index], field, (ushort)newId)), added, newId), label);
        return changed ? newId : null;
    }

    /// <summary>A new string at the end of the table (the Strings view's Add). Returns its id, or null.</summary>
    public int? AddString(out string? refused)
    {
        refused = null;
        if (Strings is not StringTable t) return null;
        var (added, id) = t.Add(NewStringBytes(StringField.Disabled));
        if (Fitting(added, out refused) is null) return null;
        return Document.ApplyStrings(_ => added, id, "Add string") ? id : null;
    }

    public bool RevertString(int id) => Document.RevertString(id);

    /// <summary>A button's name: its enabled string without the hotkey, or its index.</summary>
    public string NameOf(int index)
    {
        string name = ButtonText.Display(Text(SelectedSet.Buttons[index].EnabledString));
        return name.Length > 0 ? name : $"button {index + 1}";
    }

    /// <summary>One line per button, in order: position, hotkey, name.</summary>
    public IReadOnlyList<string> ButtonLines()
    {
        var keys = CardChecks.Hotkeys(SelectedSet, Text);
        return Enumerable.Range(0, SelectedSet.Buttons.Count)
            .Select(i => $"{SelectedSet.Buttons[i].Position}  {(keys[i] is null ? " " : ButtonText.HotkeyLabel(keys[i]))}  {NameOf(i)}")
            .ToArray();
    }

    /// <summary>The check box's warnings: hotkey clashes, then buttons replays won't show.</summary>
    public IReadOnlyList<string> CheckLines()
    {
        var lines = new List<string>();
        foreach (var clash in CardChecks.Clashes(SelectedSet, Text))
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
        id == 0 ? "0 (none)" : $"{id}: {ButtonText.Clean(Text(id)) switch { "" => "(no text)", var t => t }}";
}
