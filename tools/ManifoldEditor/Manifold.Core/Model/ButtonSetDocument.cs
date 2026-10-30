using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Model;

/// <summary>Where in a .dat file a step was made: the entry, and the property (null for a whole entry).</summary>
public readonly record struct DatPlace(DatId File, int Entry, string? Property);

/// <summary>Where a step was made: the set it changed, the string it changed, the .dat entry it changed, or several.</summary>
public readonly record struct Place(int? SetId, int? StringId, DatPlace? Dat = null);

/// <summary>One set before and after a step.</summary>
public sealed record SetEdit(int SetId, ButtonSet Before, ButtonSet After);

/// <summary>One .dat file before and after a step, made at an entry (and a property, or null for the whole entry).</summary>
public sealed record DatEdit(DatId File, DatFile Before, DatFile After, int Entry, string? Property);

/// <summary>
/// One step of the history: sets before and after (usually one; several for Copy set to
/// units and Re-import), the string table before and after, or both (a button given a copy
/// of its string is one step), or one .dat file before and after, or one file's name overrides.
/// </summary>
public sealed record Change(IReadOnlyList<SetEdit> Sets,
    StringTable? StringsBefore, StringTable? StringsAfter, int? StringId, string Label, DatEdit? Dat = null, NamesEdit? Names = null)
{
    /// <summary>Where the step shows: its first set (the one being edited), its string, and its .dat entry (an overrides step: the entry it was made at, no property).</summary>
    public Place Place => new(Sets.Count > 0 ? Sets[0].SetId : null, StringId,
        Dat is { } d ? new DatPlace(d.File, d.Entry, d.Property) :
        Names is { } n ? new DatPlace(n.File, n.Entry, null) : null);
}

/// <summary>
/// The 250 sets, the string table, the .dat files and their name overrides being edited, the
/// versions they are compared with, and the undo history. Every edit is one step in one history
/// across all of them; saving does not clear the history.
/// </summary>
public sealed class ButtonSetDocument
{
    readonly ButtonSet[] sets;
    readonly List<Change> history = new();
    int done;        // history[..done] is applied
    int savedAt;     // the value of done when last opened or saved
    StringTable? savedStrings;
    readonly Dictionary<DatId, DatFile> dats;       // being edited
    readonly Dictionary<DatId, DatFile> openedDats; // as opened (Revert entry); the saved ones after a save
    readonly Dictionary<DatId, DatFile> savedDats;  // what the exe holds
    readonly Dictionary<DatId, DatFile> vanillaDats;
    readonly Dictionary<DatId, NameOverrides> overrides;       // being edited; a file with none is absent
    readonly Dictionary<DatId, NameOverrides> savedOverrides;  // what the exe holds

    /// <summary>The sets as opened from the exe (Revert set, to the opened version).</summary>
    public IReadOnlyList<ButtonSet> Opened { get; private set; }
    /// <summary>StarCraft.exe's own sets (Revert set, to vanilla; the modified dot).</summary>
    public IReadOnlyList<ButtonSet> Vanilla { get; }

    /// <summary>The strings being edited; null when no stat_txt.tbl was found.</summary>
    public StringTable? Strings { get; private set; }
    /// <summary>The strings as opened (Revert string, the "edited" filter).</summary>
    public StringTable? OpenedStrings { get; private set; }

    /// <param name="savedStrings">
    /// The table the exe holds, when it differs from <paramref name="strings"/> (the user
    /// chose to-repack's copy on opening); the strings then count as unsaved.
    /// </param>
    /// <param name="dats">The .dat files opened and StarCraft's own; none without it.</param>
    public ButtonSetDocument(IReadOnlyList<ButtonSet> opened, IReadOnlyList<ButtonSet> vanilla,
        StringTable? strings = null, StringTable? savedStrings = null, DatSet? dats = null)
    {
        if (opened.Count != Card.SetCount || vanilla.Count != Card.SetCount)
            throw new ArgumentException($"expected {Card.SetCount} sets");
        sets = opened.ToArray();
        Opened = opened.ToArray();
        Vanilla = vanilla.ToArray();
        Strings = OpenedStrings = strings;
        this.savedStrings = savedStrings ?? strings;
        this.dats = dats is null ? new() : new(dats.Opened);
        openedDats = new(this.dats);
        savedDats = new(this.dats);
        vanillaDats = dats is null ? new() : new(dats.Vanilla);
        overrides = dats?.Overrides is { } o ? new(o) : new();
        savedOverrides = new(overrides);
    }

    /// <summary>
    /// On opening, before any edit: edit <paramref name="strings"/> instead (the user chose
    /// the to-repack copy over the exe's). The exe's table stays the saved one, so the
    /// strings count as unsaved.
    /// </summary>
    public void UseOpenedStrings(StringTable strings)
    {
        if (history.Count > 0) throw new InvalidOperationException("only before any edit");
        Strings = OpenedStrings = strings;
    }

    /// <summary>
    /// On opening, before any edit: edit <paramref name="copy"/> of a .dat file instead (the
    /// user chose the to-repack copy over the exe's). The exe's file stays the saved one, so
    /// the file counts as unsaved.
    /// </summary>
    public void UseOpenedDat(DatId file, DatFile copy)
    {
        if (history.Count > 0) throw new InvalidOperationException("only before any edit");
        if (copy.Format.Id != file) throw new ArgumentException($"{copy.Format.FileName} is not {DatFormats.Of(file).FileName}", nameof(copy));
        dats[file] = openedDats[file] = copy;
    }

    /// <summary>The .dat file being edited; null when none was found.</summary>
    public DatFile? Dat(DatId file) => dats.GetValueOrDefault(file);
    /// <summary>The file as opened or last saved (Revert entry); null when none was found.</summary>
    public DatFile? OpenedDat(DatId file) => openedDats.GetValueOrDefault(file);
    /// <summary>StarCraft's own file (Revert entry to vanilla, the modified dot); null when none was found.</summary>
    public DatFile? VanillaDat(DatId file) => vanillaDats.GetValueOrDefault(file);

    /// <summary>The file's values differ from the file last saved: the save writes it.</summary>
    public bool DatDirty(DatId file) =>
        dats.TryGetValue(file, out var current) && (!savedDats.TryGetValue(file, out var saved) || !current.SameAs(saved));

    /// <summary>The file's name overrides being edited; empty when it has none.</summary>
    public NameOverrides Overrides(DatId file) => overrides.GetValueOrDefault(file) ?? NameOverrides.Empty;

    /// <summary>The file's overrides differ from those last saved: the save writes them.</summary>
    public bool OverridesDirty(DatId file) =>
        !Overrides(file).SameAs(savedOverrides.GetValueOrDefault(file) ?? NameOverrides.Empty);

    /// <summary>Whether the entry differs from vanilla's; false past vanilla's last entry or without a vanilla file.</summary>
    public bool EntryDiffersFromVanilla(DatId file, int entry) =>
        dats.TryGetValue(file, out var current) && vanillaDats.TryGetValue(file, out var vanilla) &&
        entry >= 0 && entry < vanilla.Count && entry < current.Count && !current.EntrySameAs(entry, vanilla);

    /// <summary>
    /// Whether the entry differs from the file last saved, which the next Save writes (the list's
    /// coloured rows, counts spec §2): its values differ, or the saved file lacks it (an entry past
    /// the saved count, or a file the exe didn't hold). False past the current count.
    /// </summary>
    public bool EntryChangedSinceSave(DatId file, int entry)
    {
        if (!dats.TryGetValue(file, out var current) || entry < 0 || entry >= current.Count) return false;
        return !savedDats.TryGetValue(file, out var saved) || !current.EntrySameAs(entry, saved, uncoveredIsZero: true);
    }

    public IReadOnlyList<ButtonSet> Sets => sets;
    public ButtonSet this[int setId] => sets[setId];

    /// <summary>Unsaved changes (the title bar's mark).</summary>
    public bool IsDirty => done != savedAt || StringsDirty || dats.Keys.Any(DatDirty) || DatFormats.All.Any(f => OverridesDirty(f.Id));
    /// <summary>The strings differ from the table last saved: the save writes stat_txt.tbl.</summary>
    public bool StringsDirty => !ReferenceEquals(Strings, savedStrings);
    /// <summary>The set list's dot: the set differs from vanilla.</summary>
    public bool DiffersFromVanilla(int setId) => !sets[setId].SameAs(Vanilla[setId]);
    public bool CanUndo => done > 0;
    public bool CanRedo => done < history.Count;
    /// <summary>The label of the step Undo would take back; null when there is none.</summary>
    public string? UndoLabel => CanUndo ? history[done - 1].Label : null;

    void Record(Change change)
    {
        history.RemoveRange(done, history.Count - done);
        if (savedAt > done) savedAt = -1; // the saved state can no longer be reached
        history.Add(change);
        done++;
        Put(change, after: true);
    }

    void Put(Change change, bool after)
    {
        foreach (var edit in change.Sets) sets[edit.SetId] = after ? edit.After : edit.Before;
        if (change.StringsBefore is not null) Strings = after ? change.StringsAfter : change.StringsBefore;
        if (change.Dat is { } d) dats[d.File] = after ? d.After : d.Before;
        if (change.Names is { } n) overrides[n.File] = after ? n.After : n.Before;
    }

    /// <summary>
    /// Applies an edit of one set as one undo step. Does nothing (and records nothing)
    /// when the edit changes nothing. Returns whether it changed the set.
    /// </summary>
    public bool Apply(int setId, Func<ButtonSet, ButtonSet> edit, string label)
    {
        var before = sets[setId];
        var after = edit(before);
        if (after.SameAs(before)) return false;
        Record(new Change([new SetEdit(setId, before, after)], null, null, null, label));
        return true;
    }

    /// <summary>
    /// Several sets replaced as one step; <paramref name="first"/> is shown after an undo.
    /// Sets that don't change are left out. Returns how many changed.
    /// </summary>
    public int ApplySets(IReadOnlyDictionary<int, ButtonSet> replacements, string label, int? first = null)
    {
        var edits = replacements.OrderBy(r => r.Key == first ? -1 : r.Key)
            .Where(r => !r.Value.SameAs(sets[r.Key]))
            .Select(r => new SetEdit(r.Key, sets[r.Key], r.Value)).ToArray();
        if (edits.Length > 0) Record(new Change(edits, null, null, null, label));
        return edits.Length;
    }

    /// <summary>An edit of the strings as one step, made at <paramref name="stringId"/>. False without strings or when nothing changes.</summary>
    public bool ApplyStrings(Func<StringTable, StringTable> edit, int stringId, string label)
    {
        if (Strings is not StringTable before) return false;
        var after = edit(before);
        if (after.SameAs(before)) return false;
        Record(new Change([], before, after, stringId, label));
        return true;
    }

    /// <summary>A set and the strings changed together, as one step.</summary>
    public bool ApplyBoth(int setId, Func<ButtonSet, StringTable, (ButtonSet Set, StringTable Strings, int StringId)> edit,
        string label)
    {
        if (Strings is not StringTable stringsBefore) return false;
        var before = sets[setId];
        var (after, stringsAfter, stringId) = edit(before, stringsBefore);
        if (after.SameAs(before) && stringsAfter.SameAs(stringsBefore)) return false;
        Record(new Change([new SetEdit(setId, before, after)], stringsBefore, stringsAfter, stringId, label));
        return true;
    }

    /// <summary>
    /// An edit of one .dat file as one step, made at <paramref name="entry"/> (and
    /// <paramref name="property"/>, null for the whole entry). False when the file is
    /// missing or the edit changes nothing.
    /// </summary>
    public bool ApplyDat(DatId file, Func<DatFile, DatFile> edit, int entry, string? property, string label)
    {
        if (!dats.TryGetValue(file, out var before)) return false;
        var after = edit(before);
        if (after.SameAs(before)) return false;
        Record(new Change([], null, null, null, label, new DatEdit(file, before, after, entry, property)));
        return true;
    }

    /// <summary>
    /// A whole .dat file replaced as one step (a paste or an import of text), shown at
    /// <paramref name="firstEntry"/> after an undo, with no property. False when the file is
    /// missing or <paramref name="after"/> is the file as it is.
    /// </summary>
    public bool ApplyDatFile(DatId file, DatFile after, int firstEntry, string label)
    {
        if (!dats.TryGetValue(file, out var before) || after.SameAs(before)) return false;
        Record(new Change([], null, null, null, label, new DatEdit(file, before, after, firstEntry, null)));
        return true;
    }

    /// <summary>
    /// A file's name overrides replaced as one step ("Edit name overrides"), shown at
    /// <paramref name="entry"/> after an undo (null: entry 0). False when nothing changes.
    /// </summary>
    public bool ApplyOverrides(DatId file, NameOverrides after, int? entry)
    {
        var before = Overrides(file);
        if (after.SameAs(before)) return false;
        Record(new Change([], null, null, null, "Edit name overrides", Names: new NamesEdit(file, before, after, entry ?? 0)));
        return true;
    }

    public bool RevertToOpened(int setId) => Apply(setId, _ => Opened[setId], "Revert set");
    public bool RevertToVanilla(int setId) => Apply(setId, _ => Vanilla[setId], "Revert set to vanilla");

    /// <summary>A string back to its bytes as opened; false for a string added since.</summary>
    public bool RevertString(int id) =>
        OpenedStrings is StringTable opened && id >= 1 && id <= opened.Count &&
        ApplyStrings(t => t.Revert(id, opened), id, "Revert string");

    /// <summary>Undoes the last step; returns where it was made, so the UI can show it.</summary>
    public Place? Undo()
    {
        if (!CanUndo) return null;
        var change = history[--done];
        Put(change, after: false);
        return change.Place;
    }

    public Place? Redo()
    {
        if (!CanRedo) return null;
        var change = history[done++];
        Put(change, after: true);
        return change.Place;
    }

    /// <summary>After a successful save: the current sets, strings, .dat files and name overrides are what the exe holds.</summary>
    public void MarkSaved()
    {
        savedAt = done;
        Opened = sets.ToArray();
        savedStrings = Strings;
        foreach (var (file, current) in dats) openedDats[file] = savedDats[file] = current;
        savedOverrides.Clear();
        foreach (var (file, current) in overrides) savedOverrides[file] = current;
    }
}
