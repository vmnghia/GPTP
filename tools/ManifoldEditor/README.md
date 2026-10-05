# Manifold Editor

A FireGraft replacement for the Manifold mod, part 1: button sets on the 5x3 command card.
Spec: `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`; plan:
`docs/superpowers/plans/2026-10-05-button-set-editor.md`.

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

## Using it

The first exe opened is remembered (`%LOCALAPPDATA%\ManifoldEditor\settings.json`), with the
StarCraft folder (from the registry, else `D:\Games\Starcraft 1.16.1`; "StarCraft folder..."
changes it). An exe without `Manifold\buttonsets.bin` opens as vanilla plus its FireGraft
project's button sets; saving writes the file, which is then the source.

Saving adds the file to a copy of the exe and swaps the copy in, keeping the previous exe as
`<name>.bak`. **Add `Manifold\buttonsets.bin` to the repack tool's list**, or the next repack
drops it and the game goes back to FireGraft's sets.
