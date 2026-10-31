using System.Globalization;

namespace Manifold.Core.Data;

/// <summary>
/// FireGraft's display lists (<c>list\unitlist.txt</c> and its siblings): lines
/// <c>&lt;id&gt;Entry=Group,Subgroup,...,Name</c> place an id in FireGraft's tree; other lines
/// (<c>&lt;id&gt;Set=</c>, its button-set tree) are not this. CRLF and LF both occur.
/// </summary>
public static class FireGraftList
{
    /// <summary>Each id's group path: the Entry's comma parts without the last (the name); empty for a flat entry.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> Parse(string text)
    {
        var groups = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int digits = 0;
            while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
            if (digits == 0 || !line.AsSpan(digits).StartsWith("Entry=")) continue;
            int id = int.Parse(line.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture);
            var parts = line[(digits + "Entry=".Length)..].Split(',').Select(p => p.Trim()).ToArray();
            groups[id] = parts[..^1];
        }
        return groups;
    }
}
