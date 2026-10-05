using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>One step of the history: a set before and after an edit.</summary>
public sealed record SetChange(int SetId, ButtonSet Before, ButtonSet After, string Label);

/// <summary>
/// The 250 sets being edited, the versions they are compared with, and the undo history.
/// Every edit is one step across all sets; saving does not clear the history.
/// </summary>
public sealed class ButtonSetDocument
{
    readonly ButtonSet[] sets;
    readonly List<SetChange> history = new();
    int done;        // history[..done] is applied
    int savedAt;     // the value of done when last opened or saved

    /// <summary>The sets as opened from the exe (Revert set, to the opened version).</summary>
    public IReadOnlyList<ButtonSet> Opened { get; private set; }
    /// <summary>StarCraft.exe's own sets (Revert set, to vanilla; the modified dot).</summary>
    public IReadOnlyList<ButtonSet> Vanilla { get; }

    public ButtonSetDocument(IReadOnlyList<ButtonSet> opened, IReadOnlyList<ButtonSet> vanilla)
    {
        if (opened.Count != Card.SetCount || vanilla.Count != Card.SetCount)
            throw new ArgumentException($"expected {Card.SetCount} sets");
        sets = opened.ToArray();
        Opened = opened.ToArray();
        Vanilla = vanilla.ToArray();
    }

    public IReadOnlyList<ButtonSet> Sets => sets;
    public ButtonSet this[int setId] => sets[setId];

    /// <summary>Unsaved changes (the title bar's mark).</summary>
    public bool IsDirty => done != savedAt;
    /// <summary>The set list's dot: the set differs from vanilla.</summary>
    public bool DiffersFromVanilla(int setId) => !sets[setId].SameAs(Vanilla[setId]);
    public bool CanUndo => done > 0;
    public bool CanRedo => done < history.Count;

    /// <summary>
    /// Applies an edit of one set as one undo step. Does nothing (and records nothing)
    /// when the edit changes nothing. Returns whether it changed the set.
    /// </summary>
    public bool Apply(int setId, Func<ButtonSet, ButtonSet> edit, string label)
    {
        var before = sets[setId];
        var after = edit(before);
        if (after.SameAs(before)) return false;
        history.RemoveRange(done, history.Count - done);
        if (savedAt > done) savedAt = -1; // the saved state can no longer be reached
        history.Add(new SetChange(setId, before, after, label));
        sets[setId] = after;
        done++;
        return true;
    }

    public bool RevertToOpened(int setId) => Apply(setId, _ => Opened[setId], "Revert set");
    public bool RevertToVanilla(int setId) => Apply(setId, _ => Vanilla[setId], "Revert set to vanilla");

    /// <summary>Undoes the last step; returns the set it changed, so the UI can show it.</summary>
    public int? Undo()
    {
        if (!CanUndo) return null;
        var change = history[--done];
        sets[change.SetId] = change.Before;
        return change.SetId;
    }

    public int? Redo()
    {
        if (!CanRedo) return null;
        var change = history[done++];
        sets[change.SetId] = change.After;
        return change.SetId;
    }

    /// <summary>After a successful save: the current sets are what the exe holds.</summary>
    public void MarkSaved()
    {
        savedAt = done;
        Opened = sets.ToArray();
    }
}
