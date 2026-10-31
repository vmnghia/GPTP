using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Editor;

/// <summary>
/// What a drag onto the card does: Move (none; a filled slot swaps), Join (Alt: stack),
/// Copy (Ctrl), MoveSlot (Shift: the whole slot), CopySlot (Ctrl+Shift).
/// </summary>
public enum ButtonDrop { Move, Join, Copy, MoveSlot, CopySlot }

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
    /// <summary>The exe edited; Save as moves it to the new exe.</summary>
    public string ExeName { get; set; } = exeName;
    public int SelectedSetId { get; set; }
    /// <summary>The string the last undo or redo changed, for the window to show; null for a set-only step.</summary>
    public int? LastStringId { get; private set; }
    /// <summary>The .dat entry the last undo or redo changed, for the window to show; null for a step elsewhere.</summary>
    public DatPlace? LastDatPlace { get; private set; }
    /// <summary>The requirement record the last undo or redo changed; null when it changed none.</summary>
    public ReqPlace? LastReqPlace { get; private set; }
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

    /// <summary>
    /// A button dragged onto the card (spec §5 as changed on 2026-10-08). Returns the index of
    /// the button to keep selected, or null when nothing changed.
    /// </summary>
    public int? DropButton(int index, ushort to, ButtonDrop drop)
    {
        if (!IsIndex(index) || !IsPosition(to)) return null;
        ushort from = SelectedSet.Buttons[index].Position;
        switch (drop)
        {
            case ButtonDrop.Move:
            case ButtonDrop.Join:
                return Apply(s => CardEdits.MoveButton(s, index, to, drop == ButtonDrop.Join), drop == ButtonDrop.Join ? "Stack" : "Move")
                    ? index : null;
            case ButtonDrop.Copy:
                return Apply(s => CardEdits.Paste(s, s.Buttons[index], to), "Copy") ? SelectedSet.Buttons.Count - 1 : null;
            case ButtonDrop.MoveSlot:
            case ButtonDrop.CopySlot:
                // The whole slot; the dragged button stays selected (a copy: its copy).
                int before = SelectedSet.Buttons.Count;
                int offset = SelectedSet.Buttons.Take(index).Count(b => b.Position == from);
                if (!Drop(from, to, drop == ButtonDrop.CopySlot)) return null;
                return drop == ButtonDrop.CopySlot ? before + offset : index;
        }
        return null;
    }

    /// <summary>The slot box: the button alone to another slot (stacking if it is taken).</summary>
    public bool SetSlot(int index, ushort position) =>
        IsIndex(index) && IsPosition(position) && Apply(s => CardEdits.MoveButton(s, index, position, join: true), "Slot");

    /// <summary>What the slot box warns about for a slot: buttons already there, replays.</summary>
    public string SlotNote(int index, ushort position)
    {
        if (!IsIndex(index) || !IsPosition(position)) return "Slots are 1-15.";
        var notes = new List<string>();
        var others = Enumerable.Range(0, SelectedSet.Buttons.Count)
            .Where(i => i != index && SelectedSet.Buttons[i].Position == position).ToArray();
        if (others.Length > 0)
            notes.Add("Shares the slot with " + string.Join(", ", others.Select(NameOf)) +
                      ". The first in the list whose condition passes shows; if another one's passes too, " +
                      "the game puts it in the next slot, as vanilla does.");
        if (position > Card.ReplayMaxPosition) notes.Add("Not shown in replays (they show slots 1-9).");
        return string.Join(" ", notes);
    }

    public void CopyButton(int index)
    {
        if (IsIndex(index)) clipboardButton = SelectedSet.Buttons[index];
    }

    public bool PasteButton(ushort position) =>
        clipboardButton is Button button && IsPosition(position) &&
        Apply(s => CardEdits.Paste(s, button, position), "Paste");

    /// <summary>Add new button: the button at the slot, after the buttons there (as Paste), one undo step.</summary>
    public bool AddButton(ushort position, Button button) =>
        IsPosition(position) && Apply(s => CardEdits.Paste(s, button with { Position = position }, position), "Add button");

    public bool DeleteButton(int index) => IsIndex(index) && Apply(s => CardEdits.Delete(s, index), "Delete");

    public bool EditButton(int index, Button button) =>
        IsIndex(index) && IsPosition(button.Position) && Apply(s => CardEdits.Replace(s, index, button), "Edit button");

    public void CopySet() => clipboardSet = SelectedSet;

    /// <summary>
    /// Copy set to units: each target takes the selected set's buttons and keeps its own
    /// connected unit (as Paste set), all in one step. Returns how many sets changed.
    /// </summary>
    public int CopySetTo(IEnumerable<int> targets)
    {
        var source = SelectedSet;
        var replacements = targets.Distinct().Where(t => t != SelectedSetId && t >= 0 && t < Card.SetCount)
            .ToDictionary(t => t, t => CardEdits.PasteSet(Document[t], source));
        return Document.ApplySets(replacements, "Copy set to units");
    }

    /// <summary>
    /// Every set replaced by <paramref name="sets"/> (Re-import FireGraft's sets), as one
    /// step. Returns the ids that change, without changing anything when <paramref name="apply"/> is false.
    /// </summary>
    public IReadOnlyList<int> ReplaceAllSets(IReadOnlyList<ButtonSet> sets, string label, bool apply)
    {
        if (sets.Count != Card.SetCount) throw new ArgumentException($"expected {Card.SetCount} sets");
        var changed = Enumerable.Range(0, Card.SetCount).Where(id => !sets[id].SameAs(Document[id])).ToArray();
        if (apply) Document.ApplySets(changed.ToDictionary(id => id, id => sets[id]), label, SelectedSetId);
        return changed;
    }

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
        LastDatPlace = p.Dat;
        LastReqPlace = p.Req;
        return true;
    }

    // Requirements (spec 2026-10-30-dat-requirements §6). <paramref name="name"/> names the record in the step's label.

    /// <summary>The record's lines edited, as one step. False when it has no record or nothing changes.</summary>
    public bool EditRequirement(ReqTable table, int id, Func<ReqRecord, ReqRecord> edit, string name) =>
        Document.ApplyRequirements(table, id, t => t.Find(id) is { } r ? t.With(edit(r)) : t, $"Edit {name}'s requirements");

    public bool SetRequirementUsed(ReqTable table, int id, bool used, string name) =>
        Document.ApplyRequirements(table, id, t => t.Find(id) is { } r ? t.With(r.WithUsed(used)) : t,
            $"Turn {(used ? "on" : "off")} {name}'s requirements");

    /// <summary>A record with no lines, used, for an id that has none.</summary>
    public bool AddRequirementRecord(ReqTable table, int id, string name) =>
        Document.ApplyRequirements(table, id, t => t.Find(id) is null ? t.With(new ReqRecord(id, true, [], [])) : t,
            $"Add {name}'s requirements record");

    public bool DeleteRequirementRecord(ReqTable table, int id, string name) =>
        Document.ApplyRequirements(table, id, t => t.Without(id), $"Delete {name}'s requirements record");

    (ReqRecord Record, ReqTable From)? copiedRequirement;

    /// <summary>A record has been copied (Paste record has something to put).</summary>
    public bool HasCopiedRequirement => copiedRequirement is not null;

    /// <summary>Keeps the record for Paste record. An id with no record copies nothing and keeps the earlier copy (false).</summary>
    public bool CopyRequirement(ReqTable table, int id)
    {
        if (Document.Requirements?[table].Find(id) is not { } record) return false;
        copiedRequirement = (record, table);
        return true;
    }

    /// <summary>
    /// The copied record put on <paramref name="id"/> (replacing or adding its record), as one
    /// step. Null when done or when it changes nothing; else why not.
    /// </summary>
    public string? PasteRequirement(ReqTable table, int id, string name)
    {
        if (copiedRequirement is not { } copied) return "Nothing copied";
        if (ReqPaste.Refusal(copied.Record, copied.From, table) is string refused) return refused;
        var record = new ReqRecord(id, copied.Record.Used, copied.Record.Lines, copied.Record.Levels);
        Document.ApplyRequirements(table, id, t => t.With(record), $"Paste onto {name}'s requirements");
        return null;
    }

    /// <summary>The record as StarCraft's; deleted when StarCraft has none.</summary>
    public bool RevertRequirementToVanilla(ReqTable table, int id, string name) =>
        Document.VanillaRequirements is { } vanilla &&
        Document.ApplyRequirements(table, id, t => vanilla[table].Find(id) is { } v ? t.With(v) : t.Without(id),
            $"Revert {name}'s requirements to vanilla");

    // .dat files (spec 2026-10-24-dat-editor).

    /// <summary>
    /// One property of one entry set to a value, as one step. False when it changes nothing
    /// or is refused: the file is missing, the entry is out of range or doesn't have the
    /// property, or the value doesn't fit its type.
    /// </summary>
    public bool EditDat(DatId file, int entry, string property, ulong value)
    {
        try
        {
            return Document.ApplyDat(file, d => d.With(entry, property, value), entry, property, $"Edit {property}");
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// An entry back to the file as opened (or last saved), or to StarCraft's own, as one
    /// step. False when it already matches, there is no such file, or the entry is past the
    /// end of it (an entry added since, or one vanilla never had).
    /// </summary>
    public bool RevertEntry(DatId file, int entry, bool toVanilla)
    {
        var source = toVanilla ? Document.VanillaDat(file) : Document.OpenedDat(file);
        if (source is null || entry < 0 || entry >= source.Count) return false;
        return Document.ApplyDat(file, d => d.WithEntryFrom(entry, source, uncoveredIsZero: !toVanilla), entry, null,
            toVanilla ? "Revert entry to vanilla" : "Revert entry");
    }

    /// <summary>
    /// The file's name overrides replaced, as one step (spec 2026-10-25 §6). False when they
    /// are the same. An undo shows <paramref name="entry"/> (null: the first).
    /// </summary>
    public bool EditOverrides(DatId file, NameOverrides overrides, int? entry = null) =>
        Document.ApplyOverrides(file, overrides, entry);

    // .dat text: copy, paste and import (links spec §3).

    const string NoEntryOnClipboard = "The clipboard holds no PyDAT entry";

    /// <summary>
    /// PyMS's text for one entry: the properties of <paramref name="subTab"/> when it is a tab
    /// of this file (the Units sub-tabs), else all of them. Empty without the file.
    /// </summary>
    public string CopyEntryText(DatId file, int entry, DatTabId? subTab)
    {
        if (Document.Dat(file) is not DatFile dat || entry < 0 || entry >= dat.Count) return "";
        // Only the Units sub-tabs narrow the copy; elsewhere the command copies the entry (spec §3).
        var tab = file == DatId.Units && subTab is DatTabId id ? DatTabs.Of(id) : null;
        return DatText.Export(dat, [entry], tab is not null && tab.File == file ? tab.Properties : null);
    }

    /// <summary>
    /// The clipboard's one entry put on <paramref name="entry"/> (the text's id is ignored), as
    /// one step "Paste entry". Null on success, including when the text equals the entry
    /// (nothing recorded); else the reason it was refused.
    /// </summary>
    public string? PasteEntry(DatId file, int entry, string? clipboard)
    {
        // Text with no header line isn't an entry; text with one keeps its "line N" refusals.
        if (string.IsNullOrWhiteSpace(clipboard) || !DatText.HasHeader(clipboard)) return NoEntryOnClipboard;
        if (Document.Dat(file) is not DatFile dat) return $"There is no {DatFormats.Of(file).FileName} to paste onto";
        try
        {
            var (after, changed) = DatText.Apply(dat, DatText.Parse(clipboard), entry);
            if (changed.Count > 0) Document.ApplyDatFile(file, after, entry, "Paste entry");
            return null;
        }
        catch (DatTextException e)
        {
            return Refusal(e);
        }
    }

    /// <summary>
    /// PyMS's text imported onto the file, every entry in it, as one step named after the file
    /// and the number of entries the text lists.
    /// Null on success (also when nothing changes: nothing recorded); else the refusal, with
    /// its line when it has one, and nothing applied.
    /// </summary>
    public string? ImportText(DatId file, string text, string fileName)
    {
        if (Document.Dat(file) is not DatFile dat) return $"There is no {DatFormats.Of(file).FileName} to import onto";
        try
        {
            var entries = DatText.Parse(text);
            var (after, changed) = DatText.Apply(dat, entries);
            if (changed.Count > 0)
            {
                // The label counts the entries the text lists, not the ones that differ.
                int n = entries.Count;
                Document.ApplyDatFile(file, after, changed[0], $"Import {fileName} ({n} entr{(n == 1 ? "y" : "ies")})");
            }
            return null;
        }
        catch (DatTextException e)
        {
            return Refusal(e);
        }
    }

    /// <summary>
    /// PyDAT's Add Entry: one more entry (PyMS's rounding: units.dat's first growth is 252), as one
    /// undo step shown at <paramref name="shown"/> after an undo. False when the file is missing or
    /// can't grow (counts spec §2).
    /// </summary>
    public bool AddEntry(DatId file, int shown)
    {
        if (Document.Dat(file) is not DatFile dat || !dat.CanGrow) return false;
        return Document.ApplyDatFile(file, dat.WithCount(dat.Count + 1L), shown, $"Add entry to {dat.Format.FileName}");
    }

    /// <summary>
    /// PyDAT's Set Entry Count, as one undo step shown at <paramref name="shown"/> after an undo: null when
    /// applied or when it changes nothing, else why it is refused (fewer entries, past the maximum).
    /// </summary>
    public string? SetEntryCount(DatId file, long count, int shown)
    {
        if (Document.Dat(file) is not DatFile dat) return $"There is no {DatFormats.Of(file).FileName}";
        if (dat.CountRefusal(count) is { } refusal) return refusal;
        var after = dat.WithCount(count);
        Document.ApplyDatFile(file, after, shown, $"Set {dat.Format.FileName} to {after.Count} entries");
        return null;
    }

    static string Refusal(DatTextException e) => e.Line is int line ? $"line {line}: {e.Message}" : e.Message;

    // Strings (spec §5, §6).

    public StringTable? Strings => Document.Strings;

    /// <summary>A string as part 1 shows it (first part; hotkey, type and text joined), or null.</summary>
    public string? Text(ushort id) => Document.Strings is StringTable t ? StatTxt.Of(t).Get(id) : null;

    /// <summary>A unit's name with its subname, for the set list; null without strings.</summary>
    public string? UnitName(int unit) => Document.Strings is StringTable t ? StatTxt.Of(t).UnitName(unit) : null;

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
