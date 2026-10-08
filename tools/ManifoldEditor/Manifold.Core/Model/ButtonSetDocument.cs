using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>Where a step was made: the set it changed, the string it changed, or both.</summary>
public readonly record struct Place(int? SetId, int? StringId);

/// <summary>
/// One step of the history: a set before and after, the string table before and after, or
/// both (a button given a copy of its string is one step).
/// </summary>
public sealed record Change(int? SetId, ButtonSet? Before, ButtonSet? After,
    StringTable? StringsBefore, StringTable? StringsAfter, int? StringId, string Label)
{
    public Place Place => new(SetId, StringId);
}

/// <summary>
/// The 250 sets and the string table being edited, the versions they are compared with,
/// and the undo history. Every edit is one step across all sets and strings; saving does
/// not clear the history.
/// </summary>
public sealed class ButtonSetDocument
{
    readonly ButtonSet[] sets;
    readonly List<Change> history = new();
    int done;        // history[..done] is applied
    int savedAt;     // the value of done when last opened or saved
    StringTable? savedStrings;

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
    public ButtonSetDocument(IReadOnlyList<ButtonSet> opened, IReadOnlyList<ButtonSet> vanilla,
        StringTable? strings = null, StringTable? savedStrings = null)
    {
        if (opened.Count != Card.SetCount || vanilla.Count != Card.SetCount)
            throw new ArgumentException($"expected {Card.SetCount} sets");
        sets = opened.ToArray();
        Opened = opened.ToArray();
        Vanilla = vanilla.ToArray();
        Strings = OpenedStrings = strings;
        this.savedStrings = savedStrings ?? strings;
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

    public IReadOnlyList<ButtonSet> Sets => sets;
    public ButtonSet this[int setId] => sets[setId];

    /// <summary>Unsaved changes (the title bar's mark).</summary>
    public bool IsDirty => done != savedAt || StringsDirty;
    /// <summary>The strings differ from the table last saved: the save writes stat_txt.tbl.</summary>
    public bool StringsDirty => !ReferenceEquals(Strings, savedStrings);
    /// <summary>The set list's dot: the set differs from vanilla.</summary>
    public bool DiffersFromVanilla(int setId) => !sets[setId].SameAs(Vanilla[setId]);
    public bool CanUndo => done > 0;
    public bool CanRedo => done < history.Count;

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
        if (change.SetId is int setId) sets[setId] = (after ? change.After : change.Before)!;
        if (change.StringsBefore is not null) Strings = after ? change.StringsAfter : change.StringsBefore;
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
        Record(new Change(setId, before, after, null, null, null, label));
        return true;
    }

    /// <summary>An edit of the strings as one step, made at <paramref name="stringId"/>. False without strings or when nothing changes.</summary>
    public bool ApplyStrings(Func<StringTable, StringTable> edit, int stringId, string label)
    {
        if (Strings is not StringTable before) return false;
        var after = edit(before);
        if (after.SameAs(before)) return false;
        Record(new Change(null, null, null, before, after, stringId, label));
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
        Record(new Change(setId, before, after, stringsBefore, stringsAfter, stringId, label));
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

    /// <summary>After a successful save: the current sets and strings are what the exe holds.</summary>
    public void MarkSaved()
    {
        savedAt = done;
        Opened = sets.ToArray();
        savedStrings = Strings;
    }
}
