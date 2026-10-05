using Manifold.Core.Data;

namespace Manifold.Core.Tests;

static class Fixtures
{
    public static string PathOf(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
    public static string Text(string name) => File.ReadAllText(PathOf(name));
    public static FunctionTable Conditions() => FunctionTable.Parse(Text("FireGraftConFunc.txt"));
    public static FunctionTable Actions() => FunctionTable.Parse(Text("FireGraftActFunc.txt"));

    /// <summary>The user's StarCraft folder, for tests that need StarCraft.exe or the MPQs.</summary>
    public static string? StarCraftDir => Environment.GetEnvironmentVariable("MANIFOLD_SC_DIR");
}

static class Make
{
    public static Button Button(ushort position, ushort icon = 1, uint condition = 0x4282D0, uint action = 0x424440,
        ushort enabledString = 0) =>
        new(position, icon, condition, action, 0, 0, enabledString, 0);

    public static ButtonSet Set(params Button[] buttons) => new(buttons, 0);

    public static ButtonSet[] EmptySets() => Enumerable.Repeat(ButtonSet.Empty, Card.SetCount).ToArray();
}
