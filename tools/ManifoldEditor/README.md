# Manifold Editor

A FireGraft replacement for the Manifold mod. Part 1: button sets on the 5x3 command card
(spec `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`, plan
`docs/superpowers/plans/2026-10-05-button-set-editor.md`). Part 2: the strings of
`rez\stat_txt.tbl`, written as PyMS's PyTBL writes them (spec
`docs/superpowers/specs/2026-10-08-string-editor-design.md`, plan
`docs/superpowers/plans/2026-10-08-string-editor.md`).

| Project | What | Builds on |
|---|---|---|
| `Manifold.Core` | Data (the `MBTS` file, the `.fgp` import, vanilla sets from `StarCraft.exe`, `stat_txt.tbl`, GRP icons, `units.dat`) and the model (edits, undo, checks, open/save), plus the window's logic (`Editor/`: set list, card cells, commands, settings, resources) | anywhere (.NET 8) |
| `Manifold.Core.Tests` | xunit tests of all of it | anywhere |
| `Manifold.Storm` | `IWritableArchive` over PyMS's 32-bit `StormLib.dll` | compiles anywhere, runs on Windows |
| `ManifoldEditor.App` | the WinUI 3 window, built in C# (no `.xaml` files) | compiles anywhere, builds and runs on Windows |

## Commands

Tests (Linux or Windows):

```
dotnet test tools/ManifoldEditor/Manifold.Core.Tests
```

`MANIFOLD_SC_DIR=<StarCraft folder>` also runs the test that reads the real `StarCraft.exe`.
`MANIFOLD_STAT_TXT=<a stat_txt.tbl>` runs the report test, which writes
`tools/ManifoldEditor/reports/stat_txt-report.txt` (the string table's size, shared and
run-on entries, the mod's own strings; with `MANIFOLD_SC_DIR` too, the tooltip types of
vanilla's buttons). In PowerShell:

```
$env:MANIFOLD_STAT_TXT = "D:\SC Modding\SCManifold\to-repack\rez\stat_txt.tbl"
$env:MANIFOLD_SC_DIR = "D:\Games\Starcraft 1.16.1"
dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter StatTxtReport
```
Don't run `dotnet test` on the whole `.sln` outside Windows: the app's packaging steps need
Windows tools.

Compile check of the window without Windows (the same C# compiler and WinUI types; only the
Windows-only packaging steps are skipped):

```
cd tools/ManifoldEditor/ManifoldEditor.App
dotnet msbuild -restore -t:Compile -p:WindowsAppSDKSelfContained=false
```

On Windows: open `ManifoldEditor.sln` in Visual Studio, pick the **x86** platform, set
`ManifoldEditor.App` as the startup project, and run. Put PyMS's 32-bit `StormLib.dll` in
`lib/` first (see `lib/README.md`).

The MSIX/PRI build tasks come from the `Microsoft.Windows.SDK.BuildTools.MSIX` package, so
no extra Visual Studio component is needed. (Without it, the build fails with MSB4062:
`Microsoft.Build.Packaging.Pri.Tasks.dll` not found in Visual Studio's `AppxPackage` folder.)

## Using it

The first exe opened is remembered (`%LOCALAPPDATA%\ManifoldEditor\settings.json`), with the
StarCraft folder (from the registry, else `D:\Games\Starcraft 1.16.1`; "StarCraft folder..."
changes it). An exe without `Manifold\buttonsets.bin` opens as vanilla plus its FireGraft
project's button sets; saving writes the file, which is then the source.

FireGraft numbers its button sets its own way and links units to them (its `Unit`
section); the editor gives each unit its own copy of the set FireGraft links it to. An exe
saved before 2026-10-08 got FireGraft's sets on the wrong units: **Re-import FireGraft's
sets...** (toolbar) puts every set back to vanilla plus the FireGraft project, as one undo
step, after listing what changes. Edits made in the editor to those sets have to be made
again.

**Moving buttons.** A drag moves one button: the selected one, or the icon you press in a
stack (shared slots fan out, the button the game prefers in front). Dropping on a filled
slot swaps; **Alt** stacks onto it, **Ctrl** copies the button, **Shift** moves the whole
slot (**Ctrl+Shift** copies it). The panel's **Slot** box puts the selected button in an
exact slot.

**Shortcuts** (on the button sets page, outside text fields): Ctrl+C / Ctrl+V copy the
selected button and paste it into the selected slot (sharing the slot if it is taken),
Delete removes it; Ctrl+Shift+C / Ctrl+Shift+V copy and paste the whole set; Ctrl+S,
Ctrl+Z, Ctrl+Y save, undo, redo. The right-click menu acts on the icon clicked in a stack.

**The set list** names a unit's set by its name and subname ("Terran Siege Tank (Siege
Mode)", "Edmund Duke (Siege Tank)"), and filters by race, by type (units, buildings,
add-ons, heroes, turrets and subunits, cards 228-249, from `units.dat`) and to sets that
have buttons.

One set per unit: **Copy set to units...** gives other sets a copy of this one (one undo
step). A unit's set is named by the unit: **Rename...** opens the unit's name string.

Saving adds the file to a copy of the exe and swaps the copy in, keeping the previous exe as
`<name>.bak`. The file is PKWARE-imploded, the compression StarCraft 1.16.1's Storm reads
(an earlier build used zlib, which the game rejects with "The file data is corrupt": save
once more with this build to fix such an exe). Adding files with PyMPQ afterwards keeps it;
just don't save while PyMPQ has the exe open.

**Strings.** The button panel edits a button's two strings: the tooltip (hotkey, tooltip
type, text) and the disabled text, with Pick... to point at another string and New string
for an empty field. A string other buttons share is read-only until **Edit for all** or
**Make a separate copy**. The **Strings** page lists the whole table as PyTBL does, with
search (`#id` goes to an id), filters, the code list, a preview in the game's colours
(`game\tfontgam.pcx`), where each string is used, Add and Revert. Text uses PyTBL's `<N>`
codes, so strings copy between the two.

A save that changed strings writes `rez\stat_txt.tbl` into the exe with the button sets (both
or neither). The first such save asks whether to also write it to
`<exe folder>\to-repack\rez\stat_txt.tbl` (kept as `.bak`), so a later PyMPQ add of the
to-repack copy doesn't bring the old strings back; the answer is kept per exe. When an exe
opens and its to-repack copy differs (edited in PyTBL, say), the editor asks which to edit.
