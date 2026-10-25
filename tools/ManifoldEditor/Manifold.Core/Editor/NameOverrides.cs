using System.Text;
using System.Text.RegularExpressions;
using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// The names a mod gives entries of one .dat file in place of, or after, the ones the lists
/// and tables give (PyDAT's name overrides, spec 2026-10-25 §6). Immutable. The file is
/// text, one line per entry: <c>id:name</c> replaces the name, <c>id+:text</c> adds
/// <c>" " + text</c> to it.
/// </summary>
public sealed partial class NameOverrides
{
    // PyMS's DATEntryName.RE_NAME_OVERRIDE.
    [GeneratedRegex(@"^\s*([0-9]{1,5})\s*(\+?)\s*:(.*)$")] private static partial Regex LineRegex();

    readonly SortedDictionary<int, (bool Append, string Name)> entries;

    NameOverrides(SortedDictionary<int, (bool Append, string Name)> entries) => this.entries = entries;

    public static NameOverrides Empty { get; } = new(new());

    /// <summary>The entries by id, in id order: whether the text is appended, and the text.</summary>
    public IReadOnlyDictionary<int, (bool Append, string Name)> Entries => entries;

    public bool IsEmpty => entries.Count == 0;

    /// <summary>The override file for a .dat, in the exe: <c>Manifold\names\units.txt</c>.</summary>
    public static string ArchivePath(DatId file) =>
        $"Manifold\\names\\{Path.GetFileNameWithoutExtension(DatFormats.Of(file).FileName)}.txt";

    /// <summary>
    /// The overrides in <paramref name="text"/>. A line that isn't one is added to
    /// <paramref name="notes"/> ("&lt;path&gt; line 3: not an override") and skipped; the rest
    /// are kept. A later line for the same id wins. An empty name is no override.
    /// </summary>
    public static NameOverrides Parse(string text, List<string> notes, string path)
    {
        var result = new SortedDictionary<int, (bool, string)>();
        if (text.StartsWith('﻿')) text = text[1..];
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            var m = LineRegex().Match(line);
            if (!m.Success)
            {
                notes.Add($"{path} line {i + 1}: not an override");
                continue;
            }
            int id = int.Parse(m.Groups[1].Value);
            string name = m.Groups[3].Value;
            if (name.Length == 0) result.Remove(id);
            else result[id] = (m.Groups[2].Length > 0, name);
        }
        return new NameOverrides(result);
    }

    /// <summary>The file's text: <c>id:name</c> or <c>id+:name</c>, in id order, each line ended by a line feed (as PyDAT writes it).</summary>
    public string Write()
    {
        var text = new StringBuilder();
        foreach (var (id, (append, name)) in entries)
            text.Append(id).Append(append ? "+" : "").Append(':').Append(name).Append('\n');
        return text.ToString();
    }

    /// <summary>These overrides with the entry's set to <paramref name="name"/>; an empty name removes the entry's override.</summary>
    public NameOverrides With(int id, bool append, string name)
    {
        var copy = new SortedDictionary<int, (bool, string)>(entries);
        if (string.IsNullOrEmpty(name)) copy.Remove(id);
        else copy[id] = (append, name);
        return new NameOverrides(copy);
    }

    public bool SameAs(NameOverrides other) =>
        entries.Count == other.entries.Count && entries.All(e => other.entries.TryGetValue(e.Key, out var o) && o == e.Value);
}

/// <summary>One .dat file's name overrides before and after a step, made at <paramref name="Entry"/> (the one the dialog was opened on).</summary>
public sealed record NamesEdit(DatId File, NameOverrides Before, NameOverrides After, int Entry = 0);
