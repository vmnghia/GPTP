using System.Globalization;

namespace Manifold.Core.Data;

/// <summary>One FireGraft condition or action: its index (from 0), name and address.</summary>
public sealed record GameFunction(int Index, string Name, uint Address);

/// <summary>
/// FireGraft's condition or action list (FireGraftConFunc.txt / FireGraftActFunc.txt):
/// one line per index, <c>name TAB hexAddress [TAB varType]</c>.
/// </summary>
public sealed class FunctionTable
{
    public IReadOnlyList<GameFunction> Functions { get; }

    FunctionTable(IReadOnlyList<GameFunction> functions) => Functions = functions;

    public static FunctionTable Parse(string text)
    {
        var list = new List<GameFunction>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            list.Add(new GameFunction(list.Count, parts[0],
                uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        return new FunctionTable(list);
    }

    public GameFunction? ByIndex(uint index) => index < Functions.Count ? Functions[(int)index] : null;

    public GameFunction? ByAddress(uint address) => Functions.FirstOrDefault(f => f.Address == address);
}
