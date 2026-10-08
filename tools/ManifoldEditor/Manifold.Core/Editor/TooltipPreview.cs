using Manifold.Core.Data;

namespace Manifold.Core.Editor;

public enum LineAlign { Left, Right, Center }

/// <summary>A piece of text in one colour (ARGB; 0 for invisible text).</summary>
public sealed record PreviewRun(string Text, uint Argb);

public sealed record PreviewLine(IReadOnlyList<PreviewRun> Runs, LineAlign Align);

/// <summary>
/// The in-game colour of each text code (1-31), as PyMS draws them: its COLOR_CODES_INGAME
/// map (PyMS/FileFormats/FNT.py) gives a row and a column of 8-pixel ramps in
/// game\tfontgam.pcx; the ramp's pixel 1 is the colour's main shade.
/// </summary>
public static class TextColors
{
    /// <summary>Code to (row, column) in tfontgam.pcx; 1 means 2's colour, as in PyMS.</summary>
    static readonly Dictionary<int, (int Row, int Column)> InGame = new()
    {
        [2] = (0, 0), [3] = (0, 1), [4] = (0, 2), [5] = (0, 3), [6] = (0, 4), [7] = (0, 5), [8] = (1, 0),
        [11] = (0, 6), [14] = (1, 1), [15] = (1, 2), [16] = (1, 3), [17] = (1, 4), [20] = (0, 7),
        [21] = (1, 5), [22] = (1, 6), [23] = (1, 7), [24] = (2, 0), [25] = (2, 1), [26] = (0, 0),
        [27] = (2, 2), [28] = (2, 3), [29] = (2, 4), [30] = (2, 5), [31] = (2, 7),
    };

    /// <summary>Codes after which the game ignores further colour codes (PyMS's COLOR_OVERPOWER).</summary>
    public static readonly IReadOnlySet<int> Overpower = new HashSet<int> { 5, 11, 20 };

    /// <summary>Invisible text.</summary>
    public static readonly IReadOnlySet<int> Invisible = new HashSet<int> { 11, 20 };

    public static bool IsColour(int code) => code == 1 || InGame.ContainsKey(code);

    /// <summary>
    /// Vanilla's tfontgam.pcx colours, for when the file can't be read: pixel 1 of each ramp
    /// (index = code; 0 for codes that aren't colours).
    /// </summary>
    public static readonly uint[] Default = Build(code => code switch
    {
        2 or 26 => 0xB8B8E8u, 3 => 0xDCDC3Cu, 4 => 0xFFFFFFu, 5 => 0x847474u, 6 => 0xC81818u, 7 => 0x10FC18u,
        8 => 0xF40404u, 14 => 0x0C48CCu, 15 => 0x2CB494u, 16 => 0x88409Cu, 17 => 0xF88C14u, 21 => 0x703014u,
        22 => 0xCCE0D0u, 23 => 0xFCFC38u, 24 => 0x088008u, 25 => 0xFCFC7Cu, 27 => 0xECC4B0u, 28 => 0x4068D4u,
        29 => 0x74A47Cu, 30 => 0x9090B8u, 31 => 0x00E4FCu, _ => 0u,
    });

    static uint[] Build(Func<int, uint> rgb)
    {
        var colours = new uint[32];
        for (int code = 1; code < 32; code++)
        {
            int from = code == 1 ? 2 : code;
            colours[code] = !IsColour(code) || Invisible.Contains(code) ? 0u : 0xFF000000u | rgb(from);
        }
        return colours;
    }

    /// <summary>The colours from a tfontgam.pcx (index = code).</summary>
    public static uint[] FromTfontgam(Pcx pcx) => Build(code =>
    {
        var (row, column) = InGame[code];
        if (row >= pcx.Height || column * 8 + 1 >= pcx.Width) throw new InvalidDataException("tfontgam.pcx is too small");
        return pcx.Palette[pcx[column * 8 + 1, row]] & 0xFFFFFFu;
    });
}

/// <summary>
/// A string drawn as a guide to how the game shows it, as PyTBL's Text Previewer does
/// (PyMS/PyTBL/PreviewDialog.py): from colour 2, up to the first NUL, lines split at
/// &lt;10&gt; and &lt;12&gt;; a hotkey string's tooltip type adds a sample cost line.
/// Unlike PyTBL it also aligns lines holding &lt;18&gt; (right) or &lt;19&gt; (centre).
/// </summary>
public static class TooltipPreview
{
    /// <summary>PyTBL's sample cost lines, by tooltip type (with words for its icons).</summary>
    public static string? CostLine(byte type) => type switch
    {
        1 => "\n \u0002200 minerals  100 gas  2 supply",
        2 => "\n\u0002Next Level: 1\n 100 minerals  100 gas",
        3 => "\n \u000275 energy",
        4 or 5 => "\n \u0002150 minerals  50 gas",
        _ => null,
    };

    /// <param name="text">The string's text: without the hotkey and type for a hotkey string.</param>
    /// <param name="type">A hotkey string's tooltip type, or null.</param>
    public static IReadOnlyList<PreviewLine> Build(ReadOnlySpan<byte> text, byte? type, uint[] colours)
    {
        int nul = text.IndexOf((byte)0);
        if (nul >= 0) text = text[..nul];
        string s = System.Text.Encoding.Latin1.GetString(text) + (type is byte t ? CostLine(t) ?? "" : "");

        var lines = new List<PreviewLine>();
        int colour = 2;
        foreach (string line in s.Split('\n', '\f'))
        {
            var runs = new List<PreviewRun>();
            var current = new System.Text.StringBuilder();
            var align = LineAlign.Left;
            void Flush()
            {
                if (current.Length == 0) return;
                runs.Add(new PreviewRun(current.ToString(), colours[colour]));
                current.Clear();
            }
            foreach (char c in line)
            {
                if (c >= ' ') current.Append(c);
                else if (c == '\t') current.Append("    ");
                else if (c == '\u0012') align = LineAlign.Right;
                else if (c == '\u0013') align = LineAlign.Center;
                else if (TextColors.IsColour(c) && !TextColors.Overpower.Contains(colour))
                {
                    Flush();
                    colour = c;
                }
            }
            Flush();
            lines.Add(new PreviewLine(runs, align));
        }
        return lines;
    }
}
