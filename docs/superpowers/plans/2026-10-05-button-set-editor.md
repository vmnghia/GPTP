# Button Set Editor Implementation Plan (Manifold Editor, part 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a FireGraft replacement for button sets: open `SCManifold.exe`, drag Siege Mode and Tank Mode to positions 11 and 12 of the 5x3 card, save, start the game and see the new card, with no FireGraft and no repack step.

**Architecture:** three parts. **Manifold.Core** (C#, plain `net8.0`, no Windows APIs) holds the Data layer (the `MBTS` button set file, FireGraft's `.fgp` import, vanilla sets read from `StarCraft.exe`'s file image, `stat_txt.tbl`, GRP icons, resource lookup behind an `IArchive` interface) and the Model (pure card edits, a 250-set document with one undo history, the card checks, open and save). It is tested with `dotnet test` on Linux. **Windows build:** `Manifold.Storm` implements `IArchive` over the 32-bit StormLib, and the WinUI 3 app is the window. **Windows build:** the GPTP loader reads `Manifold\buttonsets.bin` through Storm at each game start, checks it with a pure parser (host-tested), and points the game's 250 button set table entries at it.

**Tech stack:** C# 12 / .NET 8 (xunit 2.9, Xunit.SkippableFact), WinUI 3 (Windows App SDK, x86, unpackaged, self-contained), StormLib (PyMS's 32-bit `StormLib.dll`), C++ (MSVC, Win32, GPTP).

**Spec:** `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`.

**As built (2026-10-06), where it differs from the tasks below:** Task 9's window is
built in C# without `.xaml` (it compiles on Linux with `dotnet msbuild -t:Compile`), and
its logic went into `Manifold.Core/Editor/` with tests (`EditorTests.cs`), so Core has 52
tests, not 42. The solution now holds the app, so on Linux the tests run with
`dotnet test tools/ManifoldEditor/Manifold.Core.Tests`, not the `.sln`. Task 8's project
sets `EnableWindowsTargeting` and copies `lib/StormLib.dll` only when present.

## Where each task runs

| Tasks | Where | Check |
|---|---|---|
| 0-7 | Anywhere with the .NET 8 SDK, including the Linux cloud session | `dotnet test tools/ManifoldEditor/ManifoldEditor.sln` |
| 8, 9 | **Windows build** (Visual Studio 2026, Windows App SDK) | build, then the manual checks in the task |
| 10 | **Windows build** for the hook; the parser's host test also compiles with g++ on Linux | `tests\buttonsets_file_test.bat`, `tests\verify.ps1`, in game |
| 11 | Anywhere (docs), then the user's in-game round | spec §7 |

## Global constraints

- **Manifold.Core has no Windows dependency**: no P/Invoke, no WinRT, no `System.Drawing`. Files come in as `byte[]` through `IArchive`; images go out as palette indexes and ARGB `uint[]`.
- **Indexes vs addresses.** FireGraft's `.fgp` stores condition and action *indexes* (from 0, into `FireGraftConFunc.txt` / `FireGraftActFunc.txt`). The editor's model, `buttonsets.bin` and the game use `StarCraft.exe` 1.16.1 *addresses*. Converting happens only in the `Buts` import.
- **The two string fields.** Offset 0x10 of a button is the **enabled** string (the tooltip; its first character is the hotkey) and 0x12 the **disabled** string. Spec §2 and §4 list them the other way round; the data settles it (see Verified facts). GPTP calls them `reqStringID` (0x10) and `actStringID` (0x12); leave GPTP's names alone.
- **Positions are 1-15** everywhere. Replays show 1-9.
- **All 250 sets are written**, every save. Button order inside a set is kept by every edit.
- **Undo is one step per model operation**, across all sets; an edit that changes nothing is not a step; saving keeps the history.
- **Saving never edits the exe in place** (a decision beyond the spec, which asks for one operation and nothing half-written): the file is added to a copy of the exe, which then replaces it with `File.Replace`, keeping the previous exe as `<name>.bak` (Task 8). Nothing holds the exe open between opening and saving.
- **Sync safety (GPTP).** The loader runs at game start, the same on every client given the same exe; the card only decides what can be clicked. Nothing local feeds game state.
- **GPTP conventions** (CLAUDE.md): naked wrappers use statics and registers only; a call to a no-argument C++ function from one is fine. New `.cpp` files go into `GPTP/GPTP.vcxproj` (CRLF). The build stamp in `game_hooks.cpp` is touched before an in-game test.
- Files are LF unless they already are CRLF (`GPTP.vcxproj`, `docs/resolution.md`, the fixtures' text files). Fixtures are binary-exact (`* -text`); never re-save them.

## Verified facts (2026-10-05, from the fixtures)

- `SCManifold.fgp`: "FgPa", u32 7, "1.16.1\0", then sections `char[4] tag, u32 length, data` to the end of the file: `Unit`, `Buts` (at 0x250, 2438 bytes), `UntR`, `UpgR`, `TecR`, `TecU`, `OrdR`, `ExEd`, `SUni`, `SBut`, `SUnD`, `SUpg`, `SRes`, `SUse`, `SOrd`, `SExe` (the spec lists the first nine; only `Buts` is used).
- `Buts` parses exactly: 18 sets (11, 12, 13, 15, 22, 67, 68, 69, 70, 73, 74, 75, 76, 77, 78, 79, 80, 90), positions 1-9 only, largest condition index 65, largest action index 59.
- Set 11's first button: position 1, icon 228, condition 6 (`Mixed Group - Move/Patrol/Hold Position`, 0x428DA0), action 9 (`Move`, 0x424440), strings **664 at 0x10 and 0 at 0x12**. Every button has a string at 0x10; only buttons that can be disabled also have one at 0x12 (Burrow 372/382, Stim Packs 334/346, Nuclear Strike 685/761). So 0x10 is the enabled string (the tooltip) and 0x12 the disabled one.
- `FireGraftConFunc.txt` has 67 lines, `FireGraftActFunc.txt` 60, `Icons.txt` 390 (frame 0 "Marine"). Condition index 67 exists in `reqlist.txt` but has no address.
- The Linux session has no .NET by default. `dot.net` and `builds.dotnet.microsoft.com` are blocked; Ubuntu's `dotnet-sdk-8.0` (8.0.131) installs from apt and NuGet restores. Every Core test and the code in Tasks 1-7 were run with it while writing this plan: 42 pass, 1 skips (needs `MANIFOLD_SC_DIR`).
- The C++ parser and host test in Task 10 compile and pass with g++ (`-std=c++17 -Wall -Wextra`), and a mutated parser fails the test.

## Open questions to settle in the Windows tasks

- **[VERIFY] Storm's file API by ordinal** (Task 10): `SFileOpenFileEx` 268, `SFileGetFileSize` 265, `SFileReadFile` 269, `SFileCloseFile` 253, and that `SFileOpenFileEx(NULL, …)` searches every open archive including the launcher's MPQ. The loader reports a missing function instead of crashing.
- **[VERIFY] The MPQ growing inside the exe** (Task 8): `SCManifold.exe`'s MPQ starts at 0x39B600. A save that adds a file may grow it. Test the first save on a **copy** of the exe and start the copy.
- **[VERIFY] PyMS's StormLib build** (Task 8): ANSI or Unicode paths, and stdcall. Pinned by Task 8's first check.
- **[VERIFY] The `.fgp`'s name inside the exe** (Task 9): `Firegraft\SCManifold.fgp` per the spec; the app derives it from the exe's file name.
- **[VERIFY] `stat_txt.tbl` ids count from 1** (Task 9 check: set 11's first button shows "Move"), and **`cmdicons.grp` takes `ticon.pcx`'s palette directly** (Task 9 check: icons look right).
- **Repacking can drop the editor's file.** The repack tool keeps its own list (CLAUDE.md, Deploying). Until `Manifold\buttonsets.bin` is on that list, a repack of `SCManifold.exe` silently returns the game to FireGraft's sets. Task 11 records this in `docs/resolution.md` §6 and the user adds it to the list.

## Review focus

1. **The import is exact**: 18 sets, set 11's Move, the two string fields the right way round. Task 2 pins it on the real `.fgp`.
2. **A bad `buttonsets.bin` never reaches the table**: the loader checks the whole file first (Task 10 host test, every refusal), and the editor refuses the same files (Task 1).
3. **Undo restores the exact set** for every edit, across sets, and the dirty mark follows save and undo (Task 5).
4. **A busy exe at save loses nothing**: the edits stay, the file is unchanged (Task 7; Task 8's busy mapping; in-game step 5).
5. **The loader owns its memory**: the table points into one buffer the plugin keeps; applying again swaps it after every entry is repointed (Task 10).

---

## File structure

```
tools/ManifoldEditor/
  ManifoldEditor.sln
  fixtures/                          (exists: test inputs, binary-exact)
  Manifold.Core/
    Manifold.Core.csproj
    Data/Button.cs                   Button, ButtonSet, Card constants
    Data/ButtonSetFile.cs            MBTS read (with the loader's checks) and write
    Data/FunctionTable.cs            FireGraft's condition / action lists
    Data/FgpProject.cs               .fgp sections, Buts parse and import
    Data/PeImage.cs                  PE reader, vanilla sets at 0x5187E8
    Data/StatTxt.cs                  .tbl strings, hotkeys
    Data/Grp.cs                      GRP frames, PCX palette
    Data/Archives.cs                 IArchive, IWritableArchive, ResourceResolver
    Model/CardEdits.cs               pure edits of one set
    Model/ButtonSetDocument.cs       250 sets, opened/vanilla, undo/redo, dirty
    Model/CardChecks.cs              hotkey clashes, replay-hidden buttons
    Model/EditorSession.cs           open (file, or vanilla + Buts) and save
  Manifold.Core.Tests/               xunit, net8.0
  Manifold.Storm/                    Windows build: StormLib archive (net8.0-windows, x86)
  ManifoldEditor.App/                Windows build: WinUI 3 window
GPTP/SCBW/buttonsets_file.h|.cpp     Windows build (pure): the loader's parser
GPTP/hooks/interface/buttonsets_loader.h|.cpp   Windows build: Storm read, apply at game start
tests/buttonsets_file_test.cpp|.bat  host test of the parser
```

Setup (once, before Task 0): `git checkout -b feature/manifold-editor` from `master`.

---

### Task 0: Toolchain, solution and test project

**Files:** create `tools/ManifoldEditor/ManifoldEditor.sln`, `Manifold.Core/Manifold.Core.csproj`, `Manifold.Core.Tests/Manifold.Core.Tests.csproj`; modify `.gitignore`.

- [ ] **Step 1: the .NET 8 SDK.** Linux session: `apt-get install -y dotnet-sdk-8.0` (after `apt-get update` if the package is not found); `dotnet --list-sdks` then shows an `8.0.x` line. Windows: the .NET 8 SDK from the Visual Studio installer (".NET desktop development", plus "Windows application development" for Task 9).

- [ ] **Step 2: create the solution.**

```bash
cd tools/ManifoldEditor
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet new sln -n ManifoldEditor -o .
dotnet new classlib -n Manifold.Core -f net8.0 -o Manifold.Core
dotnet new xunit -n Manifold.Core.Tests -f net8.0 -o Manifold.Core.Tests
dotnet sln add Manifold.Core Manifold.Core.Tests
dotnet add Manifold.Core.Tests reference Manifold.Core
rm Manifold.Core/Class1.cs Manifold.Core.Tests/UnitTest1.cs Manifold.Core.Tests/GlobalUsings.cs
```

- [ ] **Step 3: replace both project files** with these. The template's xunit runner is too old for `Xunit.SkippableFact`; the fixtures are copied next to the test dll; `Using Include="Xunit"` replaces the deleted `GlobalUsings.cs`.

`Manifold.Core/Manifold.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`Manifold.Core.Tests/Manifold.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Xunit.SkippableFact" Version="1.5.85" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\fixtures\**\*" Exclude="..\fixtures\README.md;..\fixtures\.gitattributes" Link="fixtures\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Manifold.Core\Manifold.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: ignore build output.** Append to `.gitignore`:
```
/tools/ManifoldEditor/**/bin/
/tools/ManifoldEditor/**/obj/
```

- [ ] **Step 5: commit** (`git add .gitignore tools/ManifoldEditor/ManifoldEditor.sln tools/ManifoldEditor/Manifold.Core tools/ManifoldEditor/Manifold.Core.Tests`): `build: Manifold Editor solution and test project`. There is nothing to test yet.

All test runs below are the whole suite: `dotnet test tools/ManifoldEditor/ManifoldEditor.sln`, run from the repo root. The expected line is the summary dotnet prints.

---

### Task 1: The button record and `buttonsets.bin`

**Files:** create `Manifold.Core/Data/Button.cs`, `Manifold.Core/Data/ButtonSetFile.cs`, `Manifold.Core.Tests/TestSupport.cs`, `Manifold.Core.Tests/ButtonSetFileTests.cs`.

- [ ] **Step 1: test support** `Manifold.Core.Tests/TestSupport.cs` (Tasks 2 and 4 add to it):
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

static class Fixtures
{
    public static string PathOf(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
    public static string Text(string name) => File.ReadAllText(PathOf(name));

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
```

- [ ] **Step 2: write the tests** `Manifold.Core.Tests/ButtonSetFileTests.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class ButtonSetFileTests
{
    static ButtonSet[] Sample()
    {
        var sets = Make.EmptySets();
        sets[5] = new ButtonSet(new[] { Make.Button(11, icon: 236), Make.Button(12, icon: 237), Make.Button(11, icon: 238) }, 5);
        sets[249] = new ButtonSet(new[] { Make.Button(1) }, 0xE4);
        return sets;
    }

    [Fact]
    public void Button_is_20_bytes_in_GPTP_order()
    {
        var b = new Button(1, 228, 0x428DA0, 0x424440, 2, 3, 664, 0x1234);
        var bytes = new byte[20];
        b.Write(bytes);
        Assert.Equal("0100E400A08D420040444200020003009802" + "3412", Convert.ToHexString(bytes));
        Assert.Equal(b, Button.Read(bytes));
    }

    [Fact]
    public void Round_trips_byte_for_byte()
    {
        var bytes = ButtonSetFile.Write(Sample());
        var sets = ButtonSetFile.Read(bytes, out var error);
        Assert.Null(error);
        Assert.Equal(bytes, ButtonSetFile.Write(sets!));
        Assert.True(sets![5].SameAs(Sample()[5]));
        Assert.Equal(8 + 250 * 8 + 4 * 20, bytes.Length);
    }

    [Fact]
    public void Writes_all_250_sets_with_the_header()
    {
        var bytes = ButtonSetFile.Write(Make.EmptySets());
        Assert.Equal("4D425453" + "0100" + "FA00", Convert.ToHexString(bytes[..8]));
        Assert.Equal(8 + 250 * 8, bytes.Length);
    }

    [Theory]
    [InlineData("magic", "not a button set file")]
    [InlineData("version", "unknown version")]
    [InlineData("count", "set count is not 250")]
    [InlineData("truncated", "set 249 is cut short")]
    [InlineData("position0", "set 5: position 0")]
    [InlineData("position16", "set 5: position 16")]
    [InlineData("trailing", "data after the last set")]
    public void Refuses_a_bad_file_with_its_reason(string fault, string reason)
    {
        var bytes = ButtonSetFile.Write(Sample());
        int firstButtonOfSet5 = 8 + 5 * 8 + 8;
        switch (fault)
        {
            case "magic": bytes[0] = (byte)'X'; break;
            case "version": bytes[4] = 2; break;
            case "count": bytes[6] = 249; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "position0": bytes[firstButtonOfSet5] = 0; break;
            case "position16": bytes[firstButtonOfSet5] = 16; break;
            case "trailing": bytes = bytes.Append((byte)0).ToArray(); break;
        }
        Assert.Null(ButtonSetFile.Read(bytes, out var error));
        Assert.Equal(reason, error);
    }

    [Fact]
    public void Refuses_an_address_outside_the_code_section()
    {
        var sets = Sample();
        sets[5] = Make.Set(Make.Button(1, condition: 0x00600000));
        var bytes = ButtonSetFile.Write(sets);
        Assert.Null(ButtonSetFile.Read(bytes, out var error, codeStart: 0x401000, codeEnd: 0x500000));
        Assert.Equal("set 5: address outside the code", error);
        Assert.NotNull(ButtonSetFile.Read(bytes, out _));
    }
}
```

- [ ] **Step 3: run, expect a build failure**: `error CS0246` for `Button`, `ButtonSet`, `ButtonSetFile`, `Card`.

- [ ] **Step 4: implement** `Manifold.Core/Data/Button.cs`:
```csharp
namespace Manifold.Core.Data;

/// <summary>
/// One command card button: GPTP's <c>BUTTON</c> (SCBW/structures.h) field for field,
/// 20 bytes little-endian. Condition and Action are StarCraft.exe 1.16.1 addresses.
/// The string at 0x10 is the tooltip shown when the button is enabled (its first
/// character is the hotkey); the one at 0x12 is shown when it is disabled. GPTP names
/// them reqStringID and actStringID.
/// </summary>
public readonly record struct Button(
    ushort Position,
    ushort Icon,
    uint Condition,
    uint Action,
    ushort ConditionVar,
    ushort ActionVar,
    ushort EnabledString,
    ushort DisabledString)
{
    public const int Size = 20;

    public static Button Read(ReadOnlySpan<byte> b) => new(
        BitConverter.ToUInt16(b[0..]),
        BitConverter.ToUInt16(b[2..]),
        BitConverter.ToUInt32(b[4..]),
        BitConverter.ToUInt32(b[8..]),
        BitConverter.ToUInt16(b[12..]),
        BitConverter.ToUInt16(b[14..]),
        BitConverter.ToUInt16(b[16..]),
        BitConverter.ToUInt16(b[18..]));

    public void Write(Span<byte> b)
    {
        BitConverter.TryWriteBytes(b[0..], Position);
        BitConverter.TryWriteBytes(b[2..], Icon);
        BitConverter.TryWriteBytes(b[4..], Condition);
        BitConverter.TryWriteBytes(b[8..], Action);
        BitConverter.TryWriteBytes(b[12..], ConditionVar);
        BitConverter.TryWriteBytes(b[14..], ActionVar);
        BitConverter.TryWriteBytes(b[16..], EnabledString);
        BitConverter.TryWriteBytes(b[18..], DisabledString);
    }
}

/// <summary>A button set: its buttons in order, and the unit it belongs to.</summary>
public sealed record ButtonSet(IReadOnlyList<Button> Buttons, uint ConnectedUnit)
{
    public static readonly ButtonSet Empty = new(Array.Empty<Button>(), 0);

    public bool SameAs(ButtonSet other) =>
        ConnectedUnit == other.ConnectedUnit && Buttons.SequenceEqual(other.Buttons);
}

public static class Card
{
    /// <summary>The game's button set table holds 250 entries (0x5187E8).</summary>
    public const int SetCount = 250;
    /// <summary>The 5x3 card: positions 1-15. Replays show the 3x3 card, 1-9.</summary>
    public const int MaxPosition = 15;
    public const int ReplayMaxPosition = 9;
}
```

`Manifold.Core/Data/ButtonSetFile.cs`:
```csharp
using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// <c>Manifold\buttonsets.bin</c> (spec §4): "MBTS", u16 version 1, u16 setCount 250,
/// then per set u16 buttonCount, u16 reserved 0, u32 connectedUnit and its buttons.
/// </summary>
public static class ButtonSetFile
{
    public const string ArchivePath = "Manifold\\buttonsets.bin";
    public const ushort Version = 1;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("MBTS");

    public static byte[] Write(IReadOnlyList<ButtonSet> sets)
    {
        if (sets.Count != Card.SetCount)
            throw new ArgumentException($"expected {Card.SetCount} sets, got {sets.Count}");
        int size = 8 + sets.Sum(s => 8 + s.Buttons.Count * Button.Size);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        Magic.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], Version);
        BitConverter.TryWriteBytes(span[6..], (ushort)Card.SetCount);
        int at = 8;
        foreach (var set in sets)
        {
            BitConverter.TryWriteBytes(span[at..], checked((ushort)set.Buttons.Count));
            BitConverter.TryWriteBytes(span[(at + 4)..], set.ConnectedUnit);
            at += 8;
            foreach (var button in set.Buttons)
            {
                button.Write(span[at..]);
                at += Button.Size;
            }
        }
        return bytes;
    }

    /// <summary>
    /// Reads and checks the file as the GPTP loader does. codeStart/codeEnd bound the
    /// condition and action addresses (StarCraft.exe's code section); null skips that check.
    /// Returns null and a reason when the file is refused.
    /// </summary>
    public static IReadOnlyList<ButtonSet>? Read(ReadOnlySpan<byte> bytes, out string? error,
        uint? codeStart = null, uint? codeEnd = null)
    {
        error = null;
        if (bytes.Length < 8 || !bytes[..4].SequenceEqual(Magic)) { error = "not a button set file"; return null; }
        if (BitConverter.ToUInt16(bytes[4..]) != Version) { error = "unknown version"; return null; }
        if (BitConverter.ToUInt16(bytes[6..]) != Card.SetCount) { error = "set count is not 250"; return null; }
        var sets = new ButtonSet[Card.SetCount];
        int at = 8;
        for (int s = 0; s < Card.SetCount; s++)
        {
            if (at + 8 > bytes.Length) { error = $"set {s} is cut short"; return null; }
            int count = BitConverter.ToUInt16(bytes[at..]);
            uint unit = BitConverter.ToUInt32(bytes[(at + 4)..]);
            at += 8;
            if (at + count * Button.Size > bytes.Length) { error = $"set {s} is cut short"; return null; }
            var buttons = new Button[count];
            for (int i = 0; i < count; i++, at += Button.Size)
            {
                var b = Button.Read(bytes[at..]);
                if (b.Position < 1 || b.Position > Card.MaxPosition) { error = $"set {s}: position {b.Position}"; return null; }
                if (codeStart is uint lo && codeEnd is uint hi &&
                    (b.Condition < lo || b.Condition >= hi || b.Action < lo || b.Action >= hi))
                { error = $"set {s}: address outside the code"; return null; }
                buttons[i] = b;
            }
            sets[s] = new ButtonSet(buttons, unit);
        }
        if (at != bytes.Length) { error = "data after the last set"; return null; }
        return sets;
    }
}
```

- [ ] **Step 5: run.** Expected: `Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11`.

- [ ] **Step 6: commit** `feat(editor): button record and buttonsets.bin read/write`.

---

### Task 2: FireGraft's lists and the `.fgp` import

**Files:** create `Manifold.Core/Data/FunctionTable.cs`, `Manifold.Core/Data/FgpProject.cs`, `Manifold.Core.Tests/FgpImportTests.cs`; modify `Manifold.Core.Tests/TestSupport.cs`.

- [ ] **Step 1: test support.** In `TestSupport.cs`, add to `Fixtures`, after `Text`:
```csharp
    public static FunctionTable Conditions() => FunctionTable.Parse(Text("FireGraftConFunc.txt"));
    public static FunctionTable Actions() => FunctionTable.Parse(Text("FireGraftActFunc.txt"));
```

- [ ] **Step 2: write the tests** `Manifold.Core.Tests/FgpImportTests.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class FgpImportTests
{
    [Fact]
    public void Function_lists_have_FireGrafts_counts_and_addresses()
    {
        var conditions = Fixtures.Conditions();
        var actions = Fixtures.Actions();
        Assert.Equal(67, conditions.Functions.Count);
        Assert.Equal(60, actions.Functions.Count);
        Assert.Equal(new GameFunction(6, "Mixed Group - Move/Patrol/Hold Position", 0x428DA0), conditions.ByIndex(6));
        Assert.Equal(new GameFunction(9, "Move", 0x424440), actions.ByIndex(9));
        Assert.Null(conditions.ByIndex(67));
    }

    [Fact]
    public void The_users_project_has_its_sections_and_18_button_sets()
    {
        var project = FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
        Assert.Equal(7u, project.Version);
        Assert.Equal("1.16.1", project.GameVersion);
        Assert.Equal(2438, project.Sections["Buts"].Length);
        Assert.Contains("Unit", project.Sections.Keys);
        var sets = ButsImport.ParseButs(project.Sections["Buts"]);
        Assert.Equal(new[] { 11, 12, 13, 15, 22, 67, 68, 69, 70, 73, 74, 75, 76, 77, 78, 79, 80, 90 },
            sets.Select(s => s.SetId));
        Assert.All(sets.SelectMany(s => s.Buttons), b => Assert.InRange(b.Position, 1, 9));
    }

    [Fact]
    public void Set_11s_first_button_is_vanilla_Move()
    {
        var project = FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
        var report = new List<string>();
        var sets = ButsImport.ToSets(ButsImport.ParseButs(project.Sections["Buts"]),
            Fixtures.Conditions(), Fixtures.Actions(), _ => 0, report);
        Assert.Empty(report);
        Assert.Equal(18, sets.Count);
        Assert.Equal(new Button(1, 228, 0x428DA0, 0x424440, 0, 0, 664, 0), sets[11].Buttons[0]);
    }

    [Fact]
    public void A_set_that_cannot_be_imported_is_reported_and_left_out()
    {
        var buts = new List<byte> { 2, 0 };
        void AddSet(byte id, ushort position, uint condition)
        {
            buts.AddRange(new byte[] { id, 1 });
            var b = new byte[20];
            new Button(position, 1, condition, 9, 0, 0, 1, 0).Write(b);
            buts.AddRange(b);
        }
        AddSet(30, 1, 67);   // condition 67: in reqlist.txt but with no address
        AddSet(31, 16, 6);   // position 16
        var report = new List<string>();
        var sets = ButsImport.ToSets(ButsImport.ParseButs(buts.ToArray()),
            Fixtures.Conditions(), Fixtures.Actions(), _ => 0, report);
        Assert.Empty(sets);
        Assert.Equal(new[] { "set 30: condition 67 has no address; kept vanilla", "set 31: position 16; kept vanilla" }, report);
    }
}
```

- [ ] **Step 3: run, expect a build failure** (`FunctionTable`, `FgpProject`, `ButsImport`, `GameFunction` missing).

- [ ] **Step 4: implement** `Manifold.Core/Data/FunctionTable.cs`:
```csharp
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
```

`Manifold.Core/Data/FgpProject.cs`:
```csharp
using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A FireGraft project (.fgp): "FgPa", u32 version, a NUL-terminated game version
/// string, then sections <c>char[4] tag, u32 length, data</c> to the end of the file.
/// </summary>
public sealed class FgpProject
{
    public uint Version { get; }
    public string GameVersion { get; }
    public IReadOnlyDictionary<string, byte[]> Sections { get; }

    FgpProject(uint version, string gameVersion, Dictionary<string, byte[]> sections)
    {
        Version = version;
        GameVersion = gameVersion;
        Sections = sections;
    }

    public static FgpProject Parse(byte[] bytes)
    {
        if (bytes.Length < 9 || Encoding.ASCII.GetString(bytes, 0, 4) != "FgPa")
            throw new InvalidDataException("not a FireGraft project");
        uint version = BitConverter.ToUInt32(bytes, 4);
        int nul = Array.IndexOf(bytes, (byte)0, 8);
        if (nul < 0) throw new InvalidDataException("no game version");
        string gameVersion = Encoding.ASCII.GetString(bytes, 8, nul - 8);
        var sections = new Dictionary<string, byte[]>();
        int at = nul + 1;
        while (at < bytes.Length)
        {
            if (at + 8 > bytes.Length) throw new InvalidDataException("section header cut short");
            string tag = Encoding.ASCII.GetString(bytes, at, 4);
            int length = checked((int)BitConverter.ToUInt32(bytes, at + 4));
            if (at + 8 + length > bytes.Length) throw new InvalidDataException($"section {tag} cut short");
            sections[tag] = bytes[(at + 8)..(at + 8 + length)];
            at += 8 + length;
        }
        return new FgpProject(version, gameVersion, sections);
    }
}

/// <summary>A button as FireGraft stores it: condition and action are list indexes.</summary>
public readonly record struct FgpButton(ushort Position, ushort Icon, uint ConditionIndex, uint ActionIndex,
    ushort ConditionVar, ushort ActionVar, ushort EnabledString, ushort DisabledString);

public sealed record FgpButtonSet(int SetId, IReadOnlyList<FgpButton> Buttons);

/// <summary>Imports the .fgp's <c>Buts</c> section: only the sets FireGraft changed.</summary>
public static class ButsImport
{
    /// <summary>Buts: u16 setCount, per set u8 setId, u8 buttonCount, 20-byte buttons.</summary>
    public static IReadOnlyList<FgpButtonSet> ParseButs(byte[] buts)
    {
        var result = new List<FgpButtonSet>();
        int count = BitConverter.ToUInt16(buts, 0);
        int at = 2;
        for (int s = 0; s < count; s++)
        {
            if (at + 2 > buts.Length) throw new InvalidDataException("Buts cut short");
            int setId = buts[at], n = buts[at + 1];
            at += 2;
            if (at + n * Button.Size > buts.Length) throw new InvalidDataException("Buts cut short");
            var buttons = new FgpButton[n];
            for (int i = 0; i < n; i++, at += Button.Size)
            {
                var raw = Button.Read(buts.AsSpan(at));
                buttons[i] = new FgpButton(raw.Position, raw.Icon, raw.Condition, raw.Action,
                    raw.ConditionVar, raw.ActionVar, raw.EnabledString, raw.DisabledString);
            }
            result.Add(new FgpButtonSet(setId, buttons));
        }
        if (at != buts.Length) throw new InvalidDataException("data after the last Buts set");
        return result;
    }

    /// <summary>
    /// Turns FireGraft's sets into address-based sets. A set with an index that has no
    /// address, or a position outside 1-15, is left out and named in <paramref name="report"/>.
    /// The connected unit is not in Buts: it is taken from <paramref name="connectedUnits"/>.
    /// </summary>
    public static Dictionary<int, ButtonSet> ToSets(IReadOnlyList<FgpButtonSet> fgpSets,
        FunctionTable conditions, FunctionTable actions, Func<int, uint> connectedUnits,
        List<string> report)
    {
        var result = new Dictionary<int, ButtonSet>();
        foreach (var set in fgpSets)
        {
            if (set.SetId >= Card.SetCount) { report.Add($"set {set.SetId}: no such set"); continue; }
            var buttons = new List<Button>();
            string? problem = null;
            foreach (var b in set.Buttons)
            {
                var condition = conditions.ByIndex(b.ConditionIndex);
                var action = actions.ByIndex(b.ActionIndex);
                if (condition is null) { problem = $"condition {b.ConditionIndex} has no address"; break; }
                if (action is null) { problem = $"action {b.ActionIndex} has no address"; break; }
                if (b.Position < 1 || b.Position > Card.MaxPosition) { problem = $"position {b.Position}"; break; }
                buttons.Add(new Button(b.Position, b.Icon, condition.Address, action.Address,
                    b.ConditionVar, b.ActionVar, b.EnabledString, b.DisabledString));
            }
            if (problem is not null) { report.Add($"set {set.SetId}: {problem}; kept vanilla"); continue; }
            result[set.SetId] = new ButtonSet(buttons, connectedUnits(set.SetId));
        }
        return result;
    }
}
```

- [ ] **Step 5: run.** Expected: `Passed: 15, Skipped: 0, Total: 15`.

- [ ] **Step 6: commit** `feat(editor): FireGraft function lists and the .fgp Buts import`.

---

### Task 3: Vanilla sets from `StarCraft.exe`

**Files:** create `Manifold.Core/Data/PeImage.cs`, `Manifold.Core.Tests/PeImageTests.cs`.

- [ ] **Step 1: write the tests.** A minimal PE is built in the test; the real exe is read only when `MANIFOLD_SC_DIR` is set. `Manifold.Core.Tests/PeImageTests.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class PeImageTests
{
    /// <summary>
    /// A minimal 32-bit PE at image base 0x400000: .text at RVA 0x1000 (code), .data at
    /// RVA 0x118000 holding the button set table at 0x5187E8 and buttons at 0x519400.
    /// </summary>
    internal static byte[] MinimalExe(Action<byte[], Func<uint, int>> fillData)
    {
        const int pe = 0x40, optional = pe + 24, optionalSize = 0xE0, sections = optional + optionalSize;
        const int textRaw = 0x200, dataRaw = 0x400, dataSize = 0x2000;
        var bytes = new byte[dataRaw + dataSize];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.TryWriteBytes(bytes.AsSpan(0x3C), pe);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe), 0x00004550u);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 6), (ushort)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(pe + 20), (ushort)optionalSize);
        BitConverter.TryWriteBytes(bytes.AsSpan(optional), (ushort)0x10B);
        BitConverter.TryWriteBytes(bytes.AsSpan(optional + 28), 0x400000u);
        void Section(int at, string name, uint va, uint vsize, uint raw, uint rawSize, uint flags)
        {
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, at);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 8), vsize);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 12), va);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 16), rawSize);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 20), raw);
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 36), flags);
        }
        Section(sections, ".text", 0x1000, 0x100, textRaw, 0x200, 0x60000020);
        Section(sections + 40, ".data", 0x118000, dataSize, dataRaw, dataSize, 0xC0000040);
        fillData(bytes, va => (int)(va - 0x400000 - 0x118000 + dataRaw));
        return bytes;
    }

    [Fact]
    public void Maps_addresses_to_file_offsets_and_finds_the_code()
    {
        var exe = new PeImage(MinimalExe((_, _) => { }));
        Assert.Equal(0x400000u, exe.ImageBase);
        Assert.Equal(0x400 + 0x7E8, exe.OffsetOf(0x5187E8));
        Assert.Equal(-1, exe.OffsetOf(0x700000));
        Assert.Equal((0x401000u, 0x401200u), exe.CodeRange());
    }

    [Fact]
    public void Reads_the_vanilla_table()
    {
        var bytes = MinimalExe((b, offsetOf) =>
        {
            int entry = offsetOf(VanillaSets.TableAddress + 5 * 12);
            BitConverter.TryWriteBytes(b.AsSpan(entry), 2u);
            BitConverter.TryWriteBytes(b.AsSpan(entry + 4), 0x519400u);
            BitConverter.TryWriteBytes(b.AsSpan(entry + 8), 5u);
            Make.Button(1, icon: 228).Write(b.AsSpan(offsetOf(0x519400)));
            Make.Button(11, icon: 236).Write(b.AsSpan(offsetOf(0x519414)));
        });
        var sets = VanillaSets.Read(new PeImage(bytes));
        Assert.Equal(250, sets.Length);
        Assert.Equal(5u, sets[5].ConnectedUnit);
        Assert.Equal(new ushort[] { 228, 236 }, sets[5].Buttons.Select(b => b.Icon));
        Assert.Empty(sets[0].Buttons);
    }

    [SkippableFact]
    public void Reads_StarCraft_exe()
    {
        Skip.If(Fixtures.StarCraftDir is null, "MANIFOLD_SC_DIR is not set");
        var exe = new PeImage(File.ReadAllBytes(System.IO.Path.Combine(Fixtures.StarCraftDir!, "StarCraft.exe")));
        var sets = VanillaSets.Read(exe);
        Assert.Empty(sets[228].Buttons);
        // The spec (§2): FireGraft's set 11 starts with vanilla Move.
        Assert.Equal(new Button(1, 228, 0x428DA0, 0x424440, 0, 0, 664, 0), sets[11].Buttons[0]);
        var (start, end) = exe.CodeRange();
        Assert.All(sets.SelectMany(s => s.Buttons), b => Assert.InRange(b.Condition, start, end - 1));
    }
}
```

- [ ] **Step 2: run, expect a build failure** (`PeImage`, `VanillaSets` missing).

- [ ] **Step 3: implement** `Manifold.Core/Data/PeImage.cs`:
```csharp
using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A 32-bit PE file read from disk: maps virtual addresses to file offsets, so tables
/// can be read from StarCraft.exe without running it.
/// </summary>
public sealed class PeImage
{
    const uint CodeFlag = 0x20; // IMAGE_SCN_CNT_CODE

    public sealed record Section(string Name, uint VirtualAddress, uint VirtualSize,
        uint RawOffset, uint RawSize, uint Characteristics);

    readonly byte[] bytes;
    public uint ImageBase { get; }
    public IReadOnlyList<Section> Sections { get; }

    public PeImage(byte[] bytes)
    {
        this.bytes = bytes;
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidDataException("not an exe");
        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe < 0 || pe + 24 > bytes.Length || BitConverter.ToUInt32(bytes, pe) != 0x00004550)
            throw new InvalidDataException("no PE header");
        int sectionCount = BitConverter.ToUInt16(bytes, pe + 6);
        int optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
        int optional = pe + 24;
        if (BitConverter.ToUInt16(bytes, optional) != 0x10B) throw new InvalidDataException("not a 32-bit exe");
        ImageBase = BitConverter.ToUInt32(bytes, optional + 28);
        var sections = new List<Section>();
        int at = optional + optionalSize;
        for (int i = 0; i < sectionCount; i++, at += 40)
            sections.Add(new Section(
                Encoding.ASCII.GetString(bytes, at, 8).TrimEnd('\0'),
                BitConverter.ToUInt32(bytes, at + 12), BitConverter.ToUInt32(bytes, at + 8),
                BitConverter.ToUInt32(bytes, at + 20), BitConverter.ToUInt32(bytes, at + 16),
                BitConverter.ToUInt32(bytes, at + 36)));
        Sections = sections;
    }

    /// <summary>The file offset of a virtual address, or -1 if no section's data holds it.</summary>
    public long OffsetOf(uint va)
    {
        uint rva = va - ImageBase;
        foreach (var s in Sections)
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.RawSize)
                return s.RawOffset + (rva - s.VirtualAddress);
        return -1;
    }

    public ReadOnlySpan<byte> Read(uint va, int length)
    {
        long offset = OffsetOf(va);
        if (offset < 0 || offset + length > bytes.Length)
            throw new InvalidDataException($"0x{va:X} is not in the file");
        return bytes.AsSpan((int)offset, length);
    }

    /// <summary>The first code section's address range [start, end).</summary>
    public (uint Start, uint End) CodeRange()
    {
        var code = Sections.First(s => (s.Characteristics & CodeFlag) != 0);
        uint start = ImageBase + code.VirtualAddress;
        return (start, start + Math.Max(code.VirtualSize, code.RawSize));
    }
}

/// <summary>StarCraft.exe 1.16.1's own button sets, read from its file image.</summary>
public static class VanillaSets
{
    /// <summary>buttonSetTable (GPTP scbwdata.h): 250 x {u32 count, BUTTON* first, u32 unit}.</summary>
    public const uint TableAddress = 0x005187E8;

    public static ButtonSet[] Read(PeImage exe)
    {
        var sets = new ButtonSet[Card.SetCount];
        var table = exe.Read(TableAddress, Card.SetCount * 12);
        for (int s = 0; s < Card.SetCount; s++)
        {
            uint count = BitConverter.ToUInt32(table[(s * 12)..]);
            uint first = BitConverter.ToUInt32(table[(s * 12 + 4)..]);
            uint unit = BitConverter.ToUInt32(table[(s * 12 + 8)..]);
            var buttons = new Button[first == 0 ? 0 : count];
            if (buttons.Length > 0)
            {
                var raw = exe.Read(first, buttons.Length * Button.Size);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i] = Button.Read(raw[(i * Button.Size)..]);
            }
            sets[s] = new ButtonSet(buttons, unit);
        }
        return sets;
    }
}
```

- [ ] **Step 4: run.** Expected: `Passed: 17, Skipped: 1, Total: 18`. On Windows with `MANIFOLD_SC_DIR=D:\Games\Starcraft 1.16.1`: 18 passed. If `Reads_StarCraft_exe` fails on set 11, the spec's "vanilla Move" claim is wrong: record what set 11 really holds in the spec; don't change the reader to fit.

- [ ] **Step 5: commit** `feat(editor): read the vanilla button sets from StarCraft.exe`.

---

### Task 4: Strings, icons and resource lookup

**Files:** create `Manifold.Core/Data/StatTxt.cs`, `Manifold.Core/Data/Grp.cs`, `Manifold.Core/Data/Archives.cs`, `Manifold.Core.Tests/ResourceTests.cs`; modify `Manifold.Core.Tests/TestSupport.cs`.

- [ ] **Step 1: test support.** Append to `TestSupport.cs`:
```csharp
sealed class FakeArchive(string name, Dictionary<string, byte[]>? files = null) : IWritableArchive
{
    public Dictionary<string, byte[]> Files { get; } = files ?? new();
    public bool Busy { get; set; }
    public int Writes { get; private set; }
    public string Name => name;
    public byte[]? TryRead(string path) => Files.TryGetValue(path, out var data) ? data : null;

    public void Write(string path, byte[] data)
    {
        if (Busy) throw new ArchiveBusyException("the exe is open in another program (FireGraft?)");
        Files[path] = data;
        Writes++;
    }
}
```

- [ ] **Step 2: write the tests** `Manifold.Core.Tests/ResourceTests.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class ResourceTests
{
    [Fact]
    public void Prefers_the_mod_exe_over_the_vanilla_mpqs()
    {
        var mod = new FakeArchive("SCManifold.exe", new() { ["rez\\stat_txt.tbl"] = new byte[] { 1 } });
        var patch = new FakeArchive("patch_rt.mpq", new() { ["rez\\stat_txt.tbl"] = new byte[] { 2 }, ["unit\\cmdbtns\\ticon.pcx"] = new byte[] { 3 } });
        var resolver = new ResourceResolver(new IArchive[] { mod, patch, new FakeArchive("BrooDat.mpq") });
        var found = resolver.Find("rez\\stat_txt.tbl")!.Value;
        Assert.Equal(new byte[] { 1 }, found.Data);
        Assert.Equal("SCManifold.exe", found.Source);
        Assert.Equal("patch_rt.mpq", resolver.Find("unit\\cmdbtns\\ticon.pcx")!.Value.Source);
        Assert.Null(resolver.Find("unit\\cmdbtns\\cmdicons.grp"));
    }

    [Fact]
    public void Stat_txt_ids_count_from_1_and_the_hotkey_is_the_first_character()
    {
        // count 2, offsets 6 and 10: "m\u0001<M>ove"-like strings
        var tbl = new List<byte> { 2, 0, 6, 0, 0, 0 };
        tbl.AddRange("m\u0001M\0"u8.ToArray());
        tbl.AddRange("s\u0001Stop\0"u8.ToArray());
        var bytes = tbl.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (ushort)10);
        var text = StatTxt.Parse(bytes);
        Assert.Equal(2, text.Count);
        Assert.Null(text.Get(0));
        Assert.Equal("m\u0001M", text.Get(1));
        Assert.Equal("s\u0001Stop", text.Get(2));
        Assert.Null(text.Get(3));
        Assert.Equal('S', StatTxt.HotkeyOf(text.Get(2)));
        Assert.Null(StatTxt.HotkeyOf(null));
    }

    [Fact]
    public void Decodes_a_grp_frame_with_skips_repeats_and_copies()
    {
        // One 4x2 frame at (0,0): row 0 = skip 1, repeat 7 x2, copy [9]; row 1 = copy [1,2,3,4]
        var grp = new List<byte> { 1, 0, 4, 0, 2, 0, 0, 0, 4, 2, 14, 0, 0, 0 };
        grp.AddRange(new byte[] { 4, 0, 9, 0 });                       // row offsets, relative to 14
        grp.AddRange(new byte[] { 0x81, 0x42, 7, 0x01, 9 });           // row 0
        grp.AddRange(new byte[] { 0x04, 1, 2, 3, 4 });                 // row 1
        var frame = new Grp(grp.ToArray()).DecodeFrame(0);
        Assert.Equal(new byte[] { 0, 7, 7, 9, 1, 2, 3, 4 }, frame);
    }

    [Fact]
    public void Reads_a_pcx_palette_and_keeps_index_0_transparent()
    {
        var pcx = new byte[128 + 10 + 769];
        int at = pcx.Length - 769;
        pcx[at] = 0x0C;
        pcx[at + 1 + 3 * 5] = 0x11; pcx[at + 2 + 3 * 5] = 0x22; pcx[at + 3 + 3 * 5] = 0x33;
        var palette = PcxPalette.Read(pcx);
        Assert.Equal(0xFF112233u, palette[5]);
        Assert.Equal(new[] { 0u, 0xFF112233u }, PcxPalette.ToArgb(new byte[] { 0, 5 }, palette));
    }

    [Fact]
    public void Icon_names_match_the_grp_frames()
    {
        var names = Fixtures.Text("Icons.txt").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        Assert.Equal(390, names.Length);
        Assert.Equal("Marine", names[0]);
    }
}
```

- [ ] **Step 3: run, expect a build failure.**

- [ ] **Step 4: implement** `Manifold.Core/Data/StatTxt.cs`:
```csharp
using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A .tbl string table (rez\stat_txt.tbl): u16 count, count u16 offsets, NUL-terminated
/// strings. Button string ids count from 1; 0 means none.
/// </summary>
public sealed class StatTxt
{
    readonly string[] strings;

    StatTxt(string[] strings) => this.strings = strings;

    public int Count => strings.Length;

    public static StatTxt Parse(byte[] bytes)
    {
        int count = BitConverter.ToUInt16(bytes, 0);
        var strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = BitConverter.ToUInt16(bytes, 2 + i * 2);
            int end = Array.IndexOf(bytes, (byte)0, start);
            if (end < 0) end = bytes.Length;
            strings[i] = Encoding.Latin1.GetString(bytes, start, end - start);
        }
        return new StatTxt(strings);
    }

    /// <summary>The string for a button's string id, or null for 0 or an id past the end.</summary>
    public string? Get(ushort id) => id >= 1 && id <= strings.Length ? strings[id - 1] : null;

    /// <summary>A button string's hotkey: its first character, as stat_txt stores it.</summary>
    public static char? HotkeyOf(string? buttonString) =>
        string.IsNullOrEmpty(buttonString) ? null : char.ToUpperInvariant(buttonString[0]);
}
```

`Manifold.Core/Data/Grp.cs`:
```csharp
namespace Manifold.Core.Data;

/// <summary>
/// A GRP image (unit\cmdbtns\cmdicons.grp): u16 frameCount, u16 width, u16 height, then
/// per frame u8 x, u8 y, u8 w, u8 h, u32 offset. At the offset, h u16 row offsets
/// (relative to it), each row run-length coded: 0x80|n skips n pixels, 0x40|n repeats
/// the next byte n times, n copies n bytes.
/// </summary>
public sealed class Grp
{
    readonly byte[] bytes;
    public int FrameCount { get; }
    public int Width { get; }
    public int Height { get; }

    public Grp(byte[] bytes)
    {
        this.bytes = bytes;
        FrameCount = BitConverter.ToUInt16(bytes, 0);
        Width = BitConverter.ToUInt16(bytes, 2);
        Height = BitConverter.ToUInt16(bytes, 4);
    }

    /// <summary>The frame as Width x Height palette indexes, 0 where transparent.</summary>
    public byte[] DecodeFrame(int frame)
    {
        if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        int header = 6 + frame * 8;
        int x = bytes[header], y = bytes[header + 1], w = bytes[header + 2], h = bytes[header + 3];
        int offset = BitConverter.ToInt32(bytes, header + 4);
        var pixels = new byte[Width * Height];
        for (int row = 0; row < h; row++)
        {
            int at = offset + BitConverter.ToUInt16(bytes, offset + row * 2);
            int col = 0;
            while (col < w)
            {
                byte c = bytes[at++];
                if ((c & 0x80) != 0) col += c & 0x7F;
                else if ((c & 0x40) != 0)
                {
                    byte value = bytes[at++];
                    for (int i = 0; i < (c & 0x3F) && col < w; i++) Set(col++, value);
                }
                else
                    for (int i = 0; i < c && col < w; i++) Set(col++, bytes[at++]);
            }

            void Set(int c, byte value)
            {
                int px = x + c, py = y + row;
                if (px < Width && py < Height) pixels[py * Width + px] = value;
            }
        }
        return pixels;
    }
}

/// <summary>The 256-colour palette at the end of a PCX file (0x0C, then 768 RGB bytes).</summary>
public static class PcxPalette
{
    /// <summary>ARGB, opaque, 256 entries.</summary>
    public static uint[] Read(byte[] pcx)
    {
        int at = pcx.Length - 769;
        if (at < 128 || pcx[at] != 0x0C) throw new InvalidDataException("no 256-colour palette");
        var palette = new uint[256];
        for (int i = 0; i < 256; i++)
            palette[i] = 0xFF000000u | (uint)pcx[at + 1 + i * 3] << 16 | (uint)pcx[at + 2 + i * 3] << 8 | pcx[at + 3 + i * 3];
        return palette;
    }

    /// <summary>A decoded frame as ARGB, index 0 transparent.</summary>
    public static uint[] ToArgb(byte[] indexes, uint[] palette) =>
        indexes.Select(i => i == 0 ? 0u : palette[i]).ToArray();
}
```

`Manifold.Core/Data/Archives.cs`:
```csharp
namespace Manifold.Core.Data;

/// <summary>An MPQ (or anything holding game files) the editor reads from.</summary>
public interface IArchive
{
    /// <summary>Shown in the status bar: where a resource came from.</summary>
    string Name { get; }
    byte[]? TryRead(string path);
}

/// <summary>The mod exe's MPQ, which the editor also writes to.</summary>
public interface IWritableArchive : IArchive
{
    /// <summary>
    /// Writes one file in a single open-write-close. Throws <see cref="ArchiveBusyException"/>
    /// when the exe is held by another program; nothing is written then.
    /// </summary>
    void Write(string path, byte[] data);
}

public sealed class ArchiveBusyException(string message) : Exception(message);

/// <summary>Looks a file up in the archives in order: the mod exe first, then the vanilla MPQs.</summary>
public sealed class ResourceResolver(IReadOnlyList<IArchive> archives)
{
    public (byte[] Data, string Source)? Find(string path)
    {
        foreach (var archive in archives)
            if (archive.TryRead(path) is { } data)
                return (data, archive.Name);
        return null;
    }
}
```

- [ ] **Step 5: run.** Expected: `Passed: 22, Skipped: 1, Total: 23`.

- [ ] **Step 6: commit** `feat(editor): stat_txt strings, GRP icons, PCX palette, resource lookup`.

---

### Task 5: Card edits and the document with undo

**Files:** create `Manifold.Core/Model/CardEdits.cs`, `Manifold.Core/Model/ButtonSetDocument.cs`, `Manifold.Core.Tests/ModelTests.cs`.

Every edit is a pure function of one set (`CardEdits`), and the document records each as `SetChange(setId, before, after)`. Undo puts `before` back, so "do then undo gives the exact set" holds by construction, and is tested for every edit. Moving a cell moves every button at that position (a cell with a count badge moves as a whole); a filled target swaps. Revert set is an ordinary step, so it can be undone too.

- [ ] **Step 1: write the tests** `Manifold.Core.Tests/ModelTests.cs`:
```csharp
using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class ModelTests
{
    static readonly Button Siege = Make.Button(1, icon: 236);
    static readonly Button Tank = Make.Button(2, icon: 237);
    static readonly Button Alt = Make.Button(1, icon: 238);

    static ButtonSetDocument Document(ButtonSet set5)
    {
        var sets = Make.EmptySets();
        sets[5] = set5;
        return new ButtonSetDocument(sets, Make.EmptySets());
    }

    public static TheoryData<string> Edits => new() { "move", "swap", "copy", "paste", "delete", "replace", "pasteSet", "revertVanilla" };

    static Func<ButtonSet, ButtonSet> EditNamed(string name) => name switch
    {
        "move" => s => CardEdits.MoveCell(s, 2, 12),
        "swap" => s => CardEdits.MoveCell(s, 1, 2),
        "copy" => s => CardEdits.CopyCell(s, 1, 15),
        "paste" => s => CardEdits.Paste(s, Tank, 9),
        "delete" => s => CardEdits.Delete(s, 0),
        "replace" => s => CardEdits.Replace(s, 1, Tank with { Icon = 99 }),
        "pasteSet" => s => CardEdits.PasteSet(s, Make.Set(Make.Button(4))),
        "revertVanilla" => _ => ButtonSet.Empty,
        _ => throw new ArgumentException(name),
    };

    [Theory, MemberData(nameof(Edits))]
    public void Every_edit_undoes_to_the_exact_set_and_redoes(string name)
    {
        var start = Make.Set(Siege, Tank, Alt);
        var document = Document(start);
        Assert.True(document.Apply(5, EditNamed(name), name));
        var after = document[5];
        Assert.False(after.SameAs(start));
        Assert.Equal(5, document.Undo());
        Assert.True(document[5].SameAs(start));
        Assert.Equal(5, document.Redo());
        Assert.True(document[5].SameAs(after));
    }

    [Fact]
    public void Moving_a_cell_moves_every_button_there_and_keeps_their_order()
    {
        var moved = CardEdits.MoveCell(Make.Set(Siege, Tank, Alt), 1, 11);
        Assert.Equal(new ushort[] { 11, 2, 11 }, moved.Buttons.Select(b => b.Position));
        var swapped = CardEdits.MoveCell(Make.Set(Siege, Tank, Alt), 1, 2);
        Assert.Equal(new ushort[] { 2, 1, 2 }, swapped.Buttons.Select(b => b.Position));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardEdits.MoveCell(Make.Set(Siege), 1, 16));
    }

    [Fact]
    public void Copy_and_paste_add_after_the_other_buttons()
    {
        var copied = CardEdits.CopyCell(Make.Set(Siege, Tank, Alt), 1, 15);
        Assert.Equal(new ushort[] { 1, 2, 1, 15, 15 }, copied.Buttons.Select(b => b.Position));
        Assert.Equal(new ushort[] { 236, 238 }, copied.Buttons.Skip(3).Select(b => b.Icon));
        var pasted = CardEdits.Paste(Make.Set(Siege), Tank, 7);
        Assert.Equal(Tank with { Position = 7 }, pasted.Buttons[1]);
    }

    [Fact]
    public void Paste_set_keeps_the_targets_connected_unit()
    {
        var target = new ButtonSet(new[] { Siege }, 30);
        var result = CardEdits.PasteSet(target, new ButtonSet(new[] { Tank }, 5));
        Assert.Equal(30u, result.ConnectedUnit);
        Assert.Equal(new[] { Tank }, result.Buttons);
    }

    [Fact]
    public void History_spans_sets_and_tracks_unsaved_changes()
    {
        var document = Document(Make.Set(Siege));
        Assert.False(document.IsDirty);
        document.Apply(5, s => CardEdits.MoveCell(s, 1, 11), "move");
        document.Apply(7, s => CardEdits.Paste(s, Tank, 3), "paste");
        Assert.True(document.IsDirty);
        Assert.True(document.DiffersFromVanilla(7));
        Assert.Equal(7, document.Undo());
        Assert.Equal(5, document.Undo());
        Assert.False(document.IsDirty);
        Assert.Null(document.Undo());

        document.Redo();
        document.MarkSaved();
        Assert.False(document.IsDirty);
        Assert.True(document.CanRedo);           // saving keeps the history
        document.Undo();
        Assert.True(document.IsDirty);
        document.Apply(9, s => CardEdits.Paste(s, Tank, 1), "paste");
        Assert.False(document.CanRedo);          // a new edit drops the redo branch
        Assert.True(document.IsDirty);           // and the saved state can't come back by undoing
        document.Undo();
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void An_edit_that_changes_nothing_is_not_a_step()
    {
        var document = Document(Make.Set(Siege));
        Assert.False(document.Apply(5, s => CardEdits.MoveCell(s, 3, 3), "move"));
        Assert.False(document.RevertToOpened(5));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void Revert_goes_to_the_opened_version_or_to_vanilla()
    {
        var document = Document(Make.Set(Siege));
        document.Apply(5, s => CardEdits.Delete(s, 0), "delete");
        Assert.True(document.RevertToOpened(5));
        Assert.True(document[5].SameAs(Make.Set(Siege)));
        Assert.True(document.RevertToVanilla(5));
        Assert.Empty(document[5].Buttons);
    }
}
```

- [ ] **Step 2: run, expect a build failure.**

- [ ] **Step 3: implement** `Manifold.Core/Model/CardEdits.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>
/// Edits of one set, as pure functions: each returns the new set and leaves its input
/// alone. Button order is kept: a move only changes positions.
/// </summary>
public static class CardEdits
{
    /// <summary>
    /// Drag a cell onto another: every button at <paramref name="from"/> goes to
    /// <paramref name="to"/>; if <paramref name="to"/> held buttons, they go to
    /// <paramref name="from"/> (a swap).
    /// </summary>
    public static ButtonSet MoveCell(ButtonSet set, ushort from, ushort to)
    {
        CheckPosition(from); CheckPosition(to);
        if (from == to) return set;
        return set with
        {
            Buttons = set.Buttons.Select(b =>
                b.Position == from ? b with { Position = to } :
                b.Position == to ? b with { Position = from } : b).ToArray()
        };
    }

    /// <summary>Ctrl+drag: the buttons at <paramref name="from"/> are copied to <paramref name="to"/>, after the others.</summary>
    public static ButtonSet CopyCell(ButtonSet set, ushort from, ushort to)
    {
        CheckPosition(from); CheckPosition(to);
        if (from == to) return set;
        var copies = set.Buttons.Where(b => b.Position == from).Select(b => b with { Position = to });
        return set with { Buttons = set.Buttons.Concat(copies).ToArray() };
    }

    /// <summary>Paste: the button goes to <paramref name="to"/>, after the set's other buttons.</summary>
    public static ButtonSet Paste(ButtonSet set, Button button, ushort to)
    {
        CheckPosition(to);
        return set with { Buttons = set.Buttons.Append(button with { Position = to }).ToArray() };
    }

    public static ButtonSet Delete(ButtonSet set, int index)
    {
        CheckIndex(set, index);
        return set with { Buttons = set.Buttons.Where((_, i) => i != index).ToArray() };
    }

    /// <summary>A field edit: the button at <paramref name="index"/> becomes <paramref name="button"/>.</summary>
    public static ButtonSet Replace(ButtonSet set, int index, Button button)
    {
        CheckIndex(set, index);
        CheckPosition(button.Position);
        return set with { Buttons = set.Buttons.Select((b, i) => i == index ? button : b).ToArray() };
    }

    /// <summary>Paste set: the target takes the source's buttons and keeps its own connected unit.</summary>
    public static ButtonSet PasteSet(ButtonSet target, ButtonSet source) =>
        target with { Buttons = source.Buttons.ToArray() };

    static void CheckPosition(ushort position)
    {
        if (position < 1 || position > Card.MaxPosition)
            throw new ArgumentOutOfRangeException(nameof(position), position, "positions are 1-15");
    }

    static void CheckIndex(ButtonSet set, int index)
    {
        if (index < 0 || index >= set.Buttons.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }
}
```

`Manifold.Core/Model/ButtonSetDocument.cs`:
```csharp
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
```

- [ ] **Step 4: run.** Expected: `Passed: 36, Skipped: 1, Total: 37`.

- [ ] **Step 5: commit** `feat(editor): card edits and the undoable 250-set document`.

---

### Task 6: The card checks

**Files:** create `Manifold.Core/Model/CardChecks.cs`, `Manifold.Core.Tests/CardChecksTests.cs`.

Buttons sharing a position are alternatives (one shows at a time, the first whose condition passes), so they never clash with each other; the same hotkey at two different positions is a warning. The editor can't know which conditions pass in game, so it flags every pair that *could* show together.

- [ ] **Step 1: write the test** `Manifold.Core.Tests/CardChecksTests.cs`:
```csharp
using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class CardChecksTests
{
    [Fact]
    public void Checks_find_hotkey_clashes_and_buttons_replays_hide()
    {
        var strings = new Dictionary<ushort, string> { [1] = "o\u0001Siege", [2] = "o\u0001Other", [3] = "t\u0001Tank" };
        var set = Make.Set(
            Make.Button(1, enabledString: 1),
            Make.Button(1, enabledString: 2),    // same position: an alternative, no clash
            Make.Button(12, enabledString: 2),   // O again at another position: a clash
            Make.Button(3, enabledString: 3));
        var clash = Assert.Single(CardChecks.Clashes(set, id => strings.GetValueOrDefault(id)));
        Assert.Equal('O', clash.Hotkey);
        Assert.Equal(new[] { 0, 1, 2 }, clash.ButtonIndexes);
        Assert.Equal(new[] { 2 }, CardChecks.HiddenInReplays(set));
    }
}
```

- [ ] **Step 2: run, expect a build failure.**

- [ ] **Step 3: implement** `Manifold.Core/Model/CardChecks.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Model;

public sealed record HotkeyClash(char Hotkey, IReadOnlyList<int> ButtonIndexes);

/// <summary>The check box under the card (spec §5.4).</summary>
public static class CardChecks
{
    /// <summary>Each button's hotkey (the enabled string's first character), or null.</summary>
    public static char?[] Hotkeys(ButtonSet set, Func<ushort, string?> strings) =>
        set.Buttons.Select(b => StatTxt.HotkeyOf(strings(b.EnabledString))).ToArray();

    /// <summary>
    /// Hotkeys shared by buttons at different positions. Buttons sharing a position are
    /// alternatives (one is shown), so they don't clash with each other.
    /// </summary>
    public static IReadOnlyList<HotkeyClash> Clashes(ButtonSet set, Func<ushort, string?> strings)
    {
        var keys = Hotkeys(set, strings);
        return Enumerable.Range(0, set.Buttons.Count)
            .Where(i => keys[i] is not null)
            .GroupBy(i => keys[i]!.Value)
            .Where(g => g.Select(i => set.Buttons[i].Position).Distinct().Count() > 1)
            .Select(g => new HotkeyClash(g.Key, g.ToArray()))
            .ToArray();
    }

    /// <summary>Buttons a replay's 3x3 card will not show (positions above 9).</summary>
    public static IReadOnlyList<int> HiddenInReplays(ButtonSet set) =>
        Enumerable.Range(0, set.Buttons.Count)
            .Where(i => set.Buttons[i].Position > Card.ReplayMaxPosition).ToArray();
}
```

- [ ] **Step 4: run.** Expected: `Passed: 37, Skipped: 1, Total: 38`.

- [ ] **Step 5: commit** `feat(editor): hotkey clashes and replay-hidden buttons`.

---

### Task 7: Open and save

**Files:** create `Manifold.Core/Model/EditorSession.cs`, `Manifold.Core.Tests/SessionTests.cs`.

Open: the exe's `Manifold\buttonsets.bin` when it has one and it passes the checks; otherwise vanilla plus the `.fgp`'s `Buts`, with the import report in the status. A file that fails its checks is reported and falls back the same way; the next save replaces it. Save: one `IWritableArchive.Write`; on `ArchiveBusyException` nothing changes and the reason is returned.

- [ ] **Step 1: write the tests** `Manifold.Core.Tests/SessionTests.cs`:
```csharp
using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class SessionTests
{
    const string FgpPath = "Firegraft\\SCManifold.fgp";

    static FakeArchive ModExe(bool withFgp = true) =>
        new("SCManifold.exe", withFgp ? new() { [FgpPath] = Fixtures.Bytes("SCManifold.fgp") } : null);

    static EditorSession.Opened Open(FakeArchive exe) =>
        EditorSession.Open(exe, FgpPath, Make.EmptySets(), Fixtures.Conditions(), Fixtures.Actions());

    [Fact]
    public void First_open_is_vanilla_plus_FireGrafts_sets()
    {
        var opened = Open(ModExe());
        Assert.Contains($"Imported 18 sets from {FgpPath}", opened.Status);
        Assert.Equal(18, Enumerable.Range(0, Card.SetCount).Count(opened.Document.DiffersFromVanilla));
        Assert.False(opened.Document.IsDirty);
    }

    [Fact]
    public void After_a_save_the_file_is_the_source()
    {
        var exe = ModExe();
        var document = Open(exe).Document;
        document.Apply(11, s => CardEdits.MoveCell(s, 1, 12), "move");
        Assert.Null(EditorSession.Save(exe, document));
        Assert.False(document.IsDirty);
        Assert.Equal(1, exe.Writes);

        exe.Files.Remove(FgpPath);
        var reopened = Open(exe);
        Assert.Contains($"Button sets from {ButtonSetFile.ArchivePath}", reopened.Status);
        Assert.Equal(12, reopened.Document[11].Buttons[0].Position);
    }

    [Fact]
    public void A_busy_exe_keeps_the_edits_unsaved()
    {
        var exe = ModExe();
        var document = Open(exe).Document;
        document.Apply(11, s => CardEdits.Delete(s, 0), "delete");
        exe.Busy = true;
        Assert.Equal("the exe is open in another program (FireGraft?)", EditorSession.Save(exe, document));
        Assert.True(document.IsDirty);
        Assert.False(exe.Files.ContainsKey(ButtonSetFile.ArchivePath));
    }

    [Fact]
    public void A_bad_file_is_reported_and_falls_back()
    {
        var exe = ModExe();
        exe.Files[ButtonSetFile.ArchivePath] = "MBTS"u8.ToArray();
        var opened = Open(exe);
        Assert.Contains(opened.Status, s => s.StartsWith(ButtonSetFile.ArchivePath + ": not a button set file"));
        Assert.Contains($"Imported 18 sets from {FgpPath}", opened.Status);
    }

    [Fact]
    public void No_project_means_vanilla()
    {
        var opened = Open(ModExe(withFgp: false));
        Assert.Contains($"No {FgpPath}: vanilla sets", opened.Status);
        Assert.Equal(0, Enumerable.Range(0, Card.SetCount).Count(opened.Document.DiffersFromVanilla));
    }
}
```

- [ ] **Step 2: run, expect a build failure.**

- [ ] **Step 3: implement** `Manifold.Core/Model/EditorSession.cs`:
```csharp
using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>Opening a mod exe's button sets, and saving them back (spec §4, §5, §6).</summary>
public static class EditorSession
{
    public sealed record Opened(ButtonSetDocument Document, IReadOnlyList<string> Status);

    /// <summary>
    /// The sets of the mod exe: its <c>Manifold\buttonsets.bin</c> if it has one; else
    /// vanilla plus the FireGraft project's <c>Buts</c> sets. A file that fails its
    /// checks is reported and the sets fall back the same way.
    /// </summary>
    public static Opened Open(IArchive modExe, string fgpPath, IReadOnlyList<ButtonSet> vanilla,
        FunctionTable conditions, FunctionTable actions)
    {
        var status = new List<string>();
        if (modExe.TryRead(ButtonSetFile.ArchivePath) is { } file)
        {
            var sets = ButtonSetFile.Read(file, out var error);
            if (sets is not null)
            {
                status.Add($"Button sets from {ButtonSetFile.ArchivePath}");
                return new Opened(new ButtonSetDocument(sets, vanilla), status);
            }
            status.Add($"{ButtonSetFile.ArchivePath}: {error}; opened vanilla and FireGraft's sets instead");
        }
        var opened = vanilla.ToArray();
        if (modExe.TryRead(fgpPath) is { } fgpBytes)
        {
            var report = new List<string>();
            var project = FgpProject.Parse(fgpBytes);
            if (project.Sections.TryGetValue("Buts", out var buts))
            {
                var imported = ButsImport.ToSets(ButsImport.ParseButs(buts), conditions, actions,
                    id => vanilla[id].ConnectedUnit, report);
                foreach (var (id, set) in imported) opened[id] = set;
                status.Add($"Imported {imported.Count} sets from {fgpPath}");
            }
            status.AddRange(report);
        }
        else
            status.Add($"No {fgpPath}: vanilla sets");
        return new Opened(new ButtonSetDocument(opened, vanilla), status);
    }

    /// <summary>
    /// Writes the 250 sets in one operation. On a busy exe nothing is written, the
    /// document stays dirty, and the reason is returned.
    /// </summary>
    public static string? Save(IWritableArchive modExe, ButtonSetDocument document)
    {
        try
        {
            modExe.Write(ButtonSetFile.ArchivePath, ButtonSetFile.Write(document.Sets));
        }
        catch (ArchiveBusyException e)
        {
            return e.Message;
        }
        document.MarkSaved();
        return null;
    }
}
```

- [ ] **Step 4: run.** Expected: `Passed!  - Failed: 0, Passed: 42, Skipped: 1, Total: 43`.

- [ ] **Step 5: commit** `feat(editor): open from the exe (file, or vanilla + FireGraft) and save in one write`.

---
### Task 8 (Windows build): The StormLib archive

**Files:** create `tools/ManifoldEditor/Manifold.Storm/Manifold.Storm.csproj`, `Manifold.Storm/StormArchive.cs`, `Manifold.Storm/Native.cs`; add the project to `ManifoldEditor.sln`.

`StormArchive` implements `IWritableArchive` for one file on disk. Every call opens the MPQ, works and closes it. **Saving never edits the exe in place:** it copies the exe to a temporary file next to it, adds `Manifold\buttonsets.bin` to the copy with StormLib, then swaps the copy in with `File.Replace`, keeping the previous exe as `<name>.bak`. A failure at any point leaves the exe untouched ("nothing is half-written", spec §6). When FireGraft or a repack holds the exe, `File.Replace` fails with a sharing violation, which becomes `ArchiveBusyException`.

- [ ] **Step 1: the project.** `dotnet new classlib -n Manifold.Storm -f net8.0-windows -o tools/ManifoldEditor/Manifold.Storm`, reference `Manifold.Core`, and set in the `.csproj`:
```xml
<PlatformTarget>x86</PlatformTarget>
<AllowUnsafeBlocks>false</AllowUnsafeBlocks>
```
Copy PyMS's 32-bit `StormLib.dll` next to the app at build time (a `<None Include="..\lib\StormLib.dll" CopyToOutputDirectory="PreserveNewest" />`; the dll goes in `tools/ManifoldEditor/lib/`, with a one-line `README.md` saying where it came from and its version).

- [ ] **Step 2: settle the [VERIFY] items first** in a scratch console app: open `StarDat.mpq` read-only and read `rez\stat_txt.tbl`. If `SFileOpenArchive` fails with a path containing only ASCII, the dll is the Unicode build: switch `Native`'s `CharSet` for the archive-name parameter to `CharSet.Unicode`. Archived names (`rez\…`) are always `char*`.

- [ ] **Step 3: `Native.cs`** (StormLib's API, stdcall):
```csharp
using System.Runtime.InteropServices;

namespace Manifold.Storm;

static class Native
{
    const string Dll = "StormLib.dll";
    public const uint STREAM_FLAG_READ_ONLY = 0x00000100;
    public const uint MPQ_FILE_COMPRESS = 0x00000200;
    public const uint MPQ_FILE_REPLACEEXISTING = 0x80000000;
    public const uint MPQ_COMPRESSION_ZLIB = 0x02;

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileOpenArchive(string mpqName, uint priority, uint flags, out IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCloseArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileFlushArchive(IntPtr mpq);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileOpenFileEx(IntPtr mpq, string name, uint scope, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern uint SFileGetFileSize(IntPtr file, IntPtr high);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileReadFile(IntPtr file, byte[] buffer, uint toRead, out uint read, IntPtr overlapped);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileCreateFile(IntPtr mpq, string name, ulong fileTime, uint size, uint locale, uint flags, out IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileWriteFile(IntPtr file, byte[] data, uint size, uint compression);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileFinishFile(IntPtr file);
    [DllImport(Dll, SetLastError = true, CallingConvention = CallingConvention.Winapi)]
    public static extern bool SFileSetMaxFileCount(IntPtr mpq, uint maxFileCount);
}
```

- [ ] **Step 4: `StormArchive.cs`:**
```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Manifold.Core.Data;

namespace Manifold.Storm;

/// <summary>An MPQ on disk (a vanilla .mpq, or the MPQ inside the mod exe), read and written through StormLib.</summary>
public sealed class StormArchive(string path, string? name = null) : IWritableArchive
{
    const int ERROR_FILE_NOT_FOUND = 2, ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32,
        ERROR_LOCK_VIOLATION = 33, ERROR_DISK_FULL = 112;

    public string Name { get; } = name ?? Path.GetFileName(path);

    public byte[]? TryRead(string file)
    {
        if (!Native.SFileOpenArchive(path, 0, Native.STREAM_FLAG_READ_ONLY, out var mpq))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Name}: cannot open its MPQ");
        try
        {
            if (!Native.SFileOpenFileEx(mpq, file, 0, out var handle)) return null;
            try
            {
                uint size = Native.SFileGetFileSize(handle, IntPtr.Zero);
                var data = new byte[size];
                if (!Native.SFileReadFile(handle, data, size, out var read, IntPtr.Zero) || read != size)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"{Name}: cannot read {file}");
                return data;
            }
            finally { Native.SFileCloseFile(handle); }
        }
        finally { Native.SFileCloseArchive(mpq); }
    }

    /// <summary>Adds or replaces one file: on a copy of the archive, swapped in at the end.</summary>
    public void Write(string file, byte[] data)
    {
        string temp = path + ".saving";
        try
        {
            try { File.Copy(path, temp, overwrite: true); }
            catch (IOException e) when (IsBusy(e)) { throw Busy(); }
            WriteInto(temp, file, data);
            try { File.Replace(temp, path, path + ".bak"); }
            catch (IOException e) when (IsBusy(e)) { throw Busy(); }
            catch (UnauthorizedAccessException) { throw Busy(); }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static void WriteInto(string archive, string file, byte[] data)
    {
        if (!Native.SFileOpenArchive(archive, 0, 0, out var mpq))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "cannot open the MPQ for writing");
        try
        {
            if (!Native.SFileCreateFile(mpq, file, 0, (uint)data.Length, 0,
                    Native.MPQ_FILE_COMPRESS | Native.MPQ_FILE_REPLACEEXISTING, out var handle))
            {
                int error = Marshal.GetLastWin32Error();
                // A full hash table: make room once, then try again.
                if (error != ERROR_DISK_FULL || !Native.SFileSetMaxFileCount(mpq, 4096) ||
                    !Native.SFileCreateFile(mpq, file, 0, (uint)data.Length, 0,
                        Native.MPQ_FILE_COMPRESS | Native.MPQ_FILE_REPLACEEXISTING, out handle))
                    throw new Win32Exception(error, $"cannot add {file}");
            }
            bool written = Native.SFileWriteFile(handle, data, (uint)data.Length, Native.MPQ_COMPRESSION_ZLIB);
            bool finished = Native.SFileFinishFile(handle);
            if (!written || !finished || !Native.SFileFlushArchive(mpq))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"cannot write {file}");
        }
        finally { Native.SFileCloseArchive(mpq); }
    }

    static bool IsBusy(IOException e) =>
        (e.HResult & 0xFFFF) is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION or ERROR_ACCESS_DENIED;

    ArchiveBusyException Busy() =>
        new($"{Name} is in use by another program (FireGraft, or a repack running). The edits are kept; save again once it is closed.");
}
```

- [ ] **Step 5: manual checks** (Windows, on **copies** in a scratch folder):
  1. `TryRead("Firegraft\\SCManifold.fgp")` on a copy of `SCManifold.exe` returns 43617 bytes equal to `fixtures/SCManifold.fgp`.
  2. `Write(ButtonSetFile.ArchivePath, …)` on the copy, then `TryRead` it back: equal bytes. **Start the copy** (`SCManifold.exe` copy): it reaches the menus and a game starts — this settles the "MPQ grows inside the exe" [VERIFY]. If the copy no longer starts, stop: the launcher stub needs its MPQ size, and saving must keep the MPQ's size (record this in the spec before going on).
  3. Open the copy in FireGraft, then `Write`: `ArchiveBusyException`, and the copy's bytes are unchanged (compare a hash before and after).
  4. Run Core's tests with `MANIFOLD_SC_DIR` set: `Reads_StarCraft_exe` passes.

- [ ] **Step 6: commit** `feat(editor): StormLib archive, saved through a swapped-in copy`.

---

### Task 9 (Windows build): The WinUI 3 window

**Files:** create `tools/ManifoldEditor/ManifoldEditor.App/` (WinUI 3 "Blank App, Packaged" template converted to unpackaged: `<WindowsPackageType>None</WindowsPackageType>`, `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`, `<SelfContained>true</SelfContained>`, `<Platforms>x86</Platforms>`, `<RuntimeIdentifier>win-x86</RuntimeIdentifier>`, `net8.0-windows10.0.19041.0`); reference `Manifold.Core` and `Manifold.Storm`; copy `fixtures/FireGraftConFunc.txt`, `FireGraftActFunc.txt` and `Icons.txt` into `ManifoldEditor.App/Data/` (content, copied to output).

All behaviour that can be tested lives in Core (Tasks 1-7); the app wires it to controls. Keep it thin: no logic in code-behind beyond calling `ButtonSetDocument`, `CardEdits`, `CardChecks` and `EditorSession`.

- [ ] **Step 1: `Services/Workspace.cs`** — everything the window opens with:
  - Settings: `%LOCALAPPDATA%\ManifoldEditor\settings.json` holding `StarCraftDir` and `LastExe`. `StarCraftDir` defaults to the registry: `HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\Starcraft`, value `InstallPath` [VERIFY the key on the user's machine], else `D:\Games\Starcraft 1.16.1`.
  - Archives in lookup order: `StormArchive(exe)`, then `patch_rt.mpq`, `BrooDat.mpq`, `StarDat.mpq` from `StarCraftDir` (each skipped when missing) → `ResourceResolver`.
  - Vanilla sets: `VanillaSets.Read(new PeImage(File.ReadAllBytes(<StarCraftDir>\StarCraft.exe)))`, and its `CodeRange()`.
  - Strings: `StatTxt.Parse` of `rez\stat_txt.tbl`; icons: `Grp` of `unit\cmdbtns\cmdicons.grp` and `PcxPalette.Read` of `unit\cmdbtns\ticon.pcx`, each frame turned into a `WriteableBitmap` once and cached. A missing resource leaves ids / numbers on screen and a note in the status bar (spec §6).
  - Open: `EditorSession.Open(exeArchive, $"Firegraft\\{Path.GetFileNameWithoutExtension(exe)}.fgp", vanilla, conditions, actions)`; an exe without an MPQ (`Win32Exception` from `TryRead`) shows a message and loads nothing.

- [ ] **Step 2: `MainWindow.xaml`** — three columns under a toolbar (spec §5): **Open exe**, **Save** (Ctrl+S), **Undo** (Ctrl+Z), **Redo** (Ctrl+Y) as `KeyboardAccelerator`s; a status bar at the bottom listing `Opened.Status` and each resource's source (`ResourceResolver.Find(...).Source`). Title: `Manifold Editor — <exe name>` with a trailing `*` while `document.IsDirty`. Closing with unsaved changes asks Save / Don't save / Cancel.

- [ ] **Step 3: the set list** — a `ListView` grouped Terran / Zerg / Protoss / Neutral and heroes / Menus (228-249), each row `id  name` and a dot when `document.DiffersFromVanilla(id)`; a search box filters by id or name. Names: unit sets 0-227 use `stat_txt` string `id + 1` (the unit names come first in stat_txt [VERIFY]); 228-249 are named from a small table in code (229-249 are the build menus and other cards, spec §2). Races come from `arr\units.dat`'s StarEdit group flags (Zerg 0x01, Terran 0x02, Protoss 0x04) [VERIFY the array's offset against PyMS's `DAT/UnitsDAT.py`]; if `units.dat` can't be read, one "Units" group.

- [ ] **Step 4: the card** — a 5x3 `Grid` of cells, position `p` at row `(p-1)/5`, column `(p-1)%5`. Each cell shows the icon of the first button at that position, and a count badge when more than one share it. Selecting a cell lists its buttons in order (selecting one opens it in the button panel). Drag and drop:
  - drop on any cell: `document.Apply(set, s => CardEdits.MoveCell(s, from, to), "Move")` (empty target moves, filled target swaps);
  - Ctrl held at drop: `CardEdits.CopyCell`;
  - context menu on a button: Copy (to an in-app clipboard holding a `Button`), Paste (on any cell: `CardEdits.Paste`), Delete (`CardEdits.Delete`);
  - set-level commands above the card: Copy set, Paste set (`CardEdits.PasteSet`), Revert set ▸ to opened (`RevertToOpened`) / to vanilla (`RevertToVanilla`).
  After `Undo()`/`Redo()`, select the returned set id in the list, so the change is visible (spec §5).

- [ ] **Step 5: the check box** under the card: for the shown set, `CardChecks.Hotkeys` next to each button, `CardChecks.Clashes` as warnings ("O: Siege Mode (1), Other (12)"), and `CardChecks.HiddenInReplays` ("not shown in replays: …").

- [ ] **Step 6: the button panel** for the selected button: icon (a flyout grid of all 390 icons with their `Icons.txt` names), condition and action (`ComboBox`es listing `FunctionTable` entries as `name (0xADDRESS)`; a button whose address is in neither list shows the raw address), the two vars, and the enabled and disabled string ids with their `stat_txt` text. A field change is one step on commit (focus lost or Enter): `document.Apply(set, s => CardEdits.Replace(s, index, edited), "Edit button")`.

- [ ] **Step 7: Save** — `EditorSession.Save(exeArchive, document)`. A returned message goes to a dialog; the title keeps its `*`.

- [ ] **Step 8: manual checks** (on a copy of `SCManifold.exe` first, then the real one):
  1. First open: the status bar reports "Imported 18 sets from Firegraft\SCManifold.fgp" and the sources of `stat_txt.tbl`, `cmdicons.grp`, `ticon.pcx`; 18 sets carry the dot.
  2. Set 11's first button shows the Move icon and the tooltip text "Move" (settles the stat_txt id base and the 0x10 field); icons look like the game's (settles the `ticon.pcx` palette).
  3. Drag, swap, Ctrl+drag, paste, delete, revert, paste set: each undoes and redoes; undoing a change in another set selects that set.
  4. Save, close, reopen: "Button sets from Manifold\buttonsets.bin", the edits are there, the `.fgp` is not read.
  5. Save while FireGraft has the exe open: the busy message; the title keeps `*`; saving again after closing FireGraft works.

- [ ] **Step 9: commit** `feat(editor): the WinUI 3 window`.

---

### Task 10 (Windows build): The GPTP loader

**Files:** create `GPTP/SCBW/buttonsets_file.h`, `GPTP/SCBW/buttonsets_file.cpp`, `GPTP/hooks/interface/buttonsets_loader.h`, `GPTP/hooks/interface/buttonsets_loader.cpp`, `tests/buttonsets_file_test.cpp`, `tests/buttonsets_file_test.bat`; modify `GPTP/GPTP.vcxproj`, `GPTP/hooks/selection_ext/sel_inject.cpp`, `tests/verify.ps1`.

The parser is pure and host-tested; the loader reads the file through Storm (which already sees the launcher's MPQ), parses it against `StarCraft.exe`'s code section, and only then repoints the table. It runs from the existing game-start wrapper (`0x4EED10`, `gameStartEntryWrapper`), so every game and replay gets it, after FireGraft's runtime applied the `.fgp` at plugin load.

- [ ] **Step 1: write the host test** `tests/buttonsets_file_test.cpp`:
```cpp
//Host test of the button set file's parser (SCBW/buttonsets_file.cpp). Build
//and run with buttonsets_file_test.bat (MSVC x86); it needs no game.
#include <SCBW/buttonsets_file.h>
#include <cstdio>
#include <cstring>
#include <vector>

using namespace bsfile;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

void put16(std::vector<u8>& v, u32 x) {
	v.push_back((u8)x);
	v.push_back((u8)(x >> 8));
}

void put32(std::vector<u8>& v, u32 x) {
	put16(v, x & 0xFFFF);
	put16(v, x >> 16);
}

const u32 CODE_START = 0x00401000;
const u32 CODE_END = 0x004FF000;
//The offset of set 5's first button in goodFile(): sets 0-4 are empty.
const u32 SET5_FIRST = HEADER_BYTES + 6 * SET_HEADER_BYTES;

//Set 5 has two buttons (positions 11 and 12), set 249 one; the rest are empty.
std::vector<u8> goodFile() {
	std::vector<u8> v;
	v.push_back('M'); v.push_back('B'); v.push_back('T'); v.push_back('S');
	put16(v, VERSION);
	put16(v, SET_COUNT);
	for (u32 s = 0; s < SET_COUNT; s++) {
		const u32 count = s == 5 ? 2 : s == 249 ? 1 : 0;
		put16(v, count);
		put16(v, 0);
		put32(v, s);
		for (u32 i = 0; i < count; i++) {
			put16(v, 11 + i);
			put16(v, 236);
			put32(v, 0x00428DA0);
			put32(v, 0x00424440);
			put16(v, 0);
			put16(v, 0);
			put16(v, 664);
			put16(v, 0);
		}
	}
	return v;
}

const char* parseOf(const std::vector<u8>& v, SetEntry* entries) {
	return parse(v.data(), (u32)v.size(), CODE_START, CODE_END, entries);
}

template <typename Spoil>
void refused(Spoil spoil, const char* reason, u32 line) {
	static SetEntry entries[SET_COUNT];
	std::vector<u8> v = goodFile();
	spoil(v);
	const char* got = parseOf(v, entries);
	check(got != NULL && strcmp(got, reason) == 0, line);
}

#define REFUSED(spoil, reason) refused(spoil, reason, __LINE__)

} //unnamed namespace

int main() {
	static SetEntry entries[SET_COUNT];
	const std::vector<u8> good = goodFile();
	CHECK(parseOf(good, entries) == NULL);
	CHECK(entries[5].buttonCount == 2 && entries[5].firstButton == SET5_FIRST && entries[5].connectedUnit == 5);
	CHECK(entries[0].buttonCount == 0 && entries[249].buttonCount == 1);

	REFUSED([](std::vector<u8>& v) { v.resize(4); }, "not a button set file");
	REFUSED([](std::vector<u8>& v) { v[0] = 'X'; }, "not a button set file");
	REFUSED([](std::vector<u8>& v) { v[4] = 2; }, "unknown version");
	REFUSED([](std::vector<u8>& v) { v[6] = 249; }, "set count is not 250");
	REFUSED([](std::vector<u8>& v) { v.pop_back(); }, "a set is cut short");
	REFUSED([](std::vector<u8>& v) { v.resize(HEADER_BYTES + 3); }, "a set is cut short");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST] = 0; }, "a position outside 1-15");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST] = 16; }, "a position outside 1-15");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST + 7] = 0x60; }, "an address outside the code");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST + 11] = 0x60; }, "an address outside the code");
	REFUSED([](std::vector<u8>& v) { v.push_back(0); }, "data after the last set");

	if (failures != 0) {
		printf("FAIL: %u checks (first at buttonsets_file_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
```

`tests/buttonsets_file_test.bat`:
```bat
@echo off
rem Builds and runs the host test of the button set file's parser.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\buttonsets_file_test.exe buttonsets_file_test.cpp ..\GPTP\SCBW\buttonsets_file.cpp > out\buttonsets_build.log
if errorlevel 1 (
	type out\buttonsets_build.log
	exit /b 1
)
out\buttonsets_file_test.exe
```

- [ ] **Step 2: run, expect a build failure** (`SCBW/buttonsets_file.h` missing). On Linux the same test builds with g++, since `types.h` only needs its MSVC integer names:
```bash
g++ -std=c++17 -Wall -Wextra -D_MSC_VER=1900 -D__int32=int -D__int16=short -D__int8=char \
    -IGPTP -o /tmp/bsfile_test tests/buttonsets_file_test.cpp GPTP/SCBW/buttonsets_file.cpp && /tmp/bsfile_test
```

- [ ] **Step 3: implement the parser** `GPTP/SCBW/buttonsets_file.h`:
```cpp
//Manifold\buttonsets.bin, written by the Manifold Editor
//(docs/superpowers/specs/2026-10-05-button-set-editor-design.md §4). Pure: the
//host test tests/buttonsets_file_test.bat builds it on its own.
#pragma once
#include "../types.h"

namespace bsfile {

const u32 SET_COUNT = 250;
const u16 VERSION = 1;
const u32 MAX_POSITION = 15;
const u32 HEADER_BYTES = 8;
const u32 SET_HEADER_BYTES = 8;
const u32 BUTTON_BYTES = 20;	//GPTP's BUTTON

struct SetEntry {
	u32 buttonCount;
	u32 firstButton;	//byte offset of the set's first button in the file
	u32 connectedUnit;
};

//Checks the whole file before anything is changed. Returns NULL and fills
//entries[SET_COUNT] when it is good, else the reason (a static string).
//Condition and action addresses must lie in [codeStart, codeEnd).
const char* parse(const u8* data, u32 size, u32 codeStart, u32 codeEnd, SetEntry* entries);

} //bsfile
```

`GPTP/SCBW/buttonsets_file.cpp`:
```cpp
#include "buttonsets_file.h"
#include <cstddef>

namespace {

u32 u16At(const u8* p) {
	return (u32)p[0] | (u32)p[1] << 8;
}

u32 u32At(const u8* p) {
	return (u32)p[0] | (u32)p[1] << 8 | (u32)p[2] << 16 | (u32)p[3] << 24;
}

} //unnamed namespace

namespace bsfile {

const char* parse(const u8* data, u32 size, u32 codeStart, u32 codeEnd, SetEntry* entries) {
	if (size < HEADER_BYTES || data[0] != 'M' || data[1] != 'B' || data[2] != 'T' || data[3] != 'S')
		return "not a button set file";
	if (u16At(data + 4) != VERSION)
		return "unknown version";
	if (u16At(data + 6) != SET_COUNT)
		return "set count is not 250";
	u32 at = HEADER_BYTES;	//always <= size
	for (u32 s = 0; s < SET_COUNT; s++) {
		if (size - at < SET_HEADER_BYTES)
			return "a set is cut short";
		const u32 count = u16At(data + at);
		entries[s].buttonCount = count;
		entries[s].connectedUnit = u32At(data + at + 4);
		at += SET_HEADER_BYTES;
		if ((size - at) / BUTTON_BYTES < count)
			return "a set is cut short";
		entries[s].firstButton = at;
		for (u32 i = 0; i < count; i++, at += BUTTON_BYTES) {
			const u8* const button = data + at;
			const u32 position = u16At(button);
			if (position < 1 || position > MAX_POSITION)
				return "a position outside 1-15";
			const u32 condition = u32At(button + 4);
			const u32 action = u32At(button + 8);
			if (condition < codeStart || condition >= codeEnd || action < codeStart || action >= codeEnd)
				return "an address outside the code";
		}
	}
	if (at != size)
		return "data after the last set";
	return NULL;
}

} //bsfile
```

- [ ] **Step 4: run** `tests\buttonsets_file_test.bat` (or the g++ line). Expected: `PASS`. (Checked with g++ while writing this plan, including that a parser accepting position 16 makes it fail.)

- [ ] **Step 5: the loader** `GPTP/hooks/interface/buttonsets_loader.h`:
```cpp
//Applies Manifold\buttonsets.bin, the Manifold Editor's button sets, at each
//game start. Spec: docs/superpowers/specs/2026-10-05-button-set-editor-design.md §4.
#pragma once

namespace bsloader {

//Reads and checks the file; only a good file changes the button set table.
//A missing file changes nothing and prints nothing; a bad one prints its
//reason once.
void applyAtGameStart();

} //bsloader
```

`GPTP/hooks/interface/buttonsets_loader.cpp`:
```cpp
#include "buttonsets_loader.h"
#include <SCBW/buttonsets_file.h>
#include <SCBW/api.h>
#include <SCBW/scbwdata.h>
#include <windows.h>
#include <cstdio>
#include <vector>

namespace {

//storm.dll's file API, by ordinal as BWAPI's storm.h has it [VERIFY].
typedef BOOL (__stdcall* SFileOpenFileExFn)(HANDLE mpq, const char* name, DWORD scope, HANDLE* file);
typedef DWORD (__stdcall* SFileGetFileSizeFn)(HANDLE file, DWORD* high);
typedef BOOL (__stdcall* SFileReadFileFn)(HANDLE file, void* buffer, DWORD toRead, DWORD* read, LPOVERLAPPED overlapped);
typedef BOOL (__stdcall* SFileCloseFileFn)(HANDLE file);
const WORD ORDINAL_CLOSE_FILE = 253;
const WORD ORDINAL_GET_FILE_SIZE = 265;
const WORD ORDINAL_OPEN_FILE_EX = 268;
const WORD ORDINAL_READ_FILE = 269;

const char* const FILE_NAME = "Manifold\\buttonsets.bin";

//The applied sets: the table points into this buffer until the next apply.
std::vector<u8> applied;
bool reported;

void report(const char* reason) {
	if (reported)
		return;
	reported = true;
	char line[128];
	sprintf_s(line, sizeof(line), "buttonsets.bin: %s", reason);
	scbw::printText(line, GameTextColor::Yellow);
}

//Fills out with the file. False when it isn't there (no report) or can't be read.
bool readFile(std::vector<u8>& out) {
	const HMODULE storm = GetModuleHandleA("storm.dll");
	const SFileOpenFileExFn openFileEx = storm == NULL ? NULL :
		(SFileOpenFileExFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_OPEN_FILE_EX));
	const SFileGetFileSizeFn getFileSize = storm == NULL ? NULL :
		(SFileGetFileSizeFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_GET_FILE_SIZE));
	const SFileReadFileFn readBytes = storm == NULL ? NULL :
		(SFileReadFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_READ_FILE));
	const SFileCloseFileFn closeFile = storm == NULL ? NULL :
		(SFileCloseFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_CLOSE_FILE));
	if (openFileEx == NULL || getFileSize == NULL || readBytes == NULL || closeFile == NULL) {
		report("storm.dll's file functions were not found");
		return false;
	}
	HANDLE file = NULL;
	if (!openFileEx(NULL, FILE_NAME, 0, &file))
		return false;
	const DWORD size = getFileSize(file, NULL);
	bool ok = size != 0xFFFFFFFF;
	if (ok) {
		out.resize(size);
		DWORD read = 0;
		ok = size == 0 || (readBytes(file, out.data(), size, &read, NULL) && read == size);
	}
	closeFile(file);
	if (!ok)
		report("could not be read");
	return ok;
}

//StarCraft.exe's first code section, from its PE headers in memory.
void codeRange(u32* start, u32* end) {
	const u8* const base = (const u8*)GetModuleHandleA(NULL);
	const IMAGE_NT_HEADERS32* const nt =
		(const IMAGE_NT_HEADERS32*)(base + ((const IMAGE_DOS_HEADER*)base)->e_lfanew);
	const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
	for (u32 i = 0; i < nt->FileHeader.NumberOfSections; i++, section++)
		if (section->Characteristics & IMAGE_SCN_CNT_CODE) {
			const u32 size = section->Misc.VirtualSize > section->SizeOfRawData
				? section->Misc.VirtualSize : section->SizeOfRawData;
			*start = (u32)base + section->VirtualAddress;
			*end = *start + size;
			return;
		}
	*start = 0;
	*end = 0;
}

} //unnamed namespace

namespace bsloader {

void applyAtGameStart() {
	std::vector<u8> file;
	if (!readFile(file))
		return;
	static bsfile::SetEntry entries[bsfile::SET_COUNT];
	u32 codeStart, codeEnd;
	codeRange(&codeStart, &codeEnd);
	const char* const error = bsfile::parse(file.data(), (u32)file.size(), codeStart, codeEnd, entries);
	if (error != NULL) {
		report(error);
		return;
	}
	//Every entry is repointed before the old buffer goes, at the end of this function.
	applied.swap(file);
	for (u32 s = 0; s < bsfile::SET_COUNT; s++) {
		buttonSetTable[s].buttonsInSet = entries[s].buttonCount;
		buttonSetTable[s].firstButton = (BUTTON*)(applied.data() + entries[s].firstButton);
		buttonSetTable[s].connectedUnit = entries[s].connectedUnit;
	}
}

} //bsloader
```

The loader reads `entries[s].firstButton` as an offset into the file; `BUTTON` is the file's 20-byte record (`SCBW/structures.h`, condition and action as 32-bit pointers on Win32), so the table can point straight into it.

- [ ] **Step 6: call it at game start.** In `GPTP/hooks/selection_ext/sel_inject.cpp`, add `#include "../interface/buttonsets_loader.h"` and, in `gameStartEntryWrapper`, call it after `sellocal::gameStartClear();` (a call with no arguments and no temporaries, as the naked-wrapper rule wants):
```cpp
void __declspec(naked) gameStartEntryWrapper() {
	__asm PUSHAD
	sellocal::gameStartClear();
	bsloader::applyAtGameStart();
	__asm {
		POPAD
		PUSH EDI
		XOR EAX, EAX
		MOV ECX, 0x0C
		JMP Back_GameStartEntry
	}
}
```

- [ ] **Step 7: the project and the checks.** In `GPTP/GPTP.vcxproj` (CRLF; edit byte-safe), add `<ClCompile Include="SCBW\buttonsets_file.cpp" />` next to `SCBW\selection_ext_core.cpp`, `<ClCompile Include="hooks\interface\buttonsets_loader.cpp" />` next to the other `hooks\interface` entries, and the two `<ClInclude>`s likewise. In `tests/verify.ps1`, after the selection host test:
```powershell
& "$PSScriptRoot\buttonsets_file_test.bat" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output "button set file test FAILED"; exit 1 }
```

- [ ] **Step 8: run** `powershell -ExecutionPolicy Bypass -File tests\verify.ps1`. Expected: `plugin build succeeded` (host tests, plugin build and the naked-wrapper check all pass).

- [ ] **Step 9: commit** `feat: apply Manifold\buttonsets.bin at game start`.

---

### Task 11: Docs and the in-game round

**Files:** modify `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`, `docs/resolution.md` (CRLF), `CLAUDE.md`.

- [ ] **Step 1: the spec.** In §2 and §4, swap the two string names to `u16 enabledStringId, u16 disabledStringId` (0x10 is the tooltip; see Verified facts). Add the sections past `SUni` to §2's list. Record what Tasks 8-10 settled for each [VERIFY] in "Open questions" above (Storm ordinals, MPQ growth, StormLib's char set, the `.fgp` name, stat_txt's id base, the icon palette, the registry key, the `units.dat` offset). Change the status line to "built" once the in-game round passes.

- [ ] **Step 2: `docs/resolution.md` §6.** Under "Data edits, the user's to make": **add `Manifold\buttonsets.bin` to the repack tool's list**, or a repack returns the game to FireGraft's sets. Under the planned features: the button set editor, built, with the spec's path. Edit the file byte-safe (CRLF).

- [ ] **Step 3: `CLAUDE.md`.** Under "The fork's own modules": the Manifold Editor (`tools/ManifoldEditor/`, Core testable on Linux with `dotnet test tools/ManifoldEditor/ManifoldEditor.sln` after `apt-get install -y dotnet-sdk-8.0`; the app and Storm are Windows builds) and the loader (`hooks/interface/buttonsets_loader.cpp`, at game start). Under "Building": the second host test in `tests/verify.ps1`.

- [ ] **Step 4: the in-game round** (spec §7), by the user:
  1. Open `SCManifold.exe` in the editor with FireGraft closed: the status bar reports 18 imported sets.
  2. Move Siege Mode to 11 and Tank Mode to 12 on the Siege Tank's card, save, run: both show; their hotkeys work. (Vanilla puts Siege Mode on set 5, Tank Mode, and Tank Mode on set 30, Siege Mode, if memory serves: check which set holds which before moving, and adjust the step.)
  3. A replay shows the 3x3 card without them.
  4. An exe with a corrupt `buttonsets.bin` (save one, then flip its first byte with a hex editor in a copy) prints `buttonsets.bin: not a button set file` and plays with FireGraft's sets.
  5. Save while FireGraft has the exe open: the editor reports it and keeps the edits.

- [ ] **Step 5: commit** `docs: Manifold Editor part 1 built`.
