# Manifold Editor

A FireGraft replacement for the Manifold mod. Part 1: button sets on the 5x3 command card
(spec `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`, plan
`docs/superpowers/plans/2026-10-05-button-set-editor.md`). Part 2: the strings of
`rez\stat_txt.tbl`, written as PyMS's PyTBL writes them (spec
`docs/superpowers/specs/2026-10-08-string-editor-design.md`, plan
`docs/superpowers/plans/2026-10-08-string-editor.md`). Part 4: the `.dat` files, a C# port of
PyMS's PyDAT (spec `docs/superpowers/specs/2026-10-24-dat-editor-design.md`, plan
`docs/superpowers/plans/2026-10-24-dat-editor.md`).

| Project | What | Builds on |
|---|---|---|
| `Manifold.Core` | Data (the `MBTS` file, the `.fgp` import, vanilla sets from `StarCraft.exe`, `stat_txt.tbl`, GRP icons, the 11 `.dat` files in `Data/Dat/`) and the model (edits, undo, checks, open/save), plus the window's logic (`Editor/`: set list, card cells, commands, settings, resources) | anywhere (.NET 8) |
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
`MANIFOLD_ARR_DIR=<a folder of vanilla's arr\*.dat>` runs the tests that read the real `.dat`
files (every file reads and saves byte for byte, and some values are spot-checked); they are
skipped when it is unset. PyMS's own copies of the vanilla files do.
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

**The toolbar** (QoL spec `docs/superpowers/specs/2026-10-24-editor-qol-design.md`): Open
exe..., Save, Save as..., Undo, Redo, Repack plugin..., Repack files..., Launch (F5), and
**More** (StarCraft folder..., Re-import FireGraft's sets...). Save shows ● with unsaved
edits; Repack plugin shows ● when `GPTP.qdp` differs from the exe's plugin (its tooltip says
how); Repack files shows how many to-repack files are new or changed. They look again on
opening, after a save or repack, and when the window comes back into focus. **Launch**
starts the exe from its folder, after asking to save unsaved edits and to repack a newer
plugin or pending files. **Save as...** saves the edits into a copy of the exe at a new path
and goes on editing that one (undo history kept); the original stays as it was, and the new
exe asks again where to write its strings. A click on the status bar lists every status
line, with Copy. The window opens where it was left, on the page and set left.

**Adding and editing buttons.** Right-click a slot: **Add new button...** picks Blank (icon
0, Always, Move) or any button of the exe's sets or vanilla's, searched by tooltip, action,
condition or icon; it lands in the slot, sharing it when taken. Condition and Action filter
as you type (a name or an address); Enter or a click picks, Esc keeps the old one. The icon
picker searches by name or number, and **Unused** shows only icons no button uses. Icons are
centred on their drawn pixels.

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

**Repack** (spec `docs/superpowers/specs/2026-10-14-repack-design.md`), in place of FireGraft
and PyMPQ, in two commands. **Repack plugin...** packs the plugin (`<exe folder>\GPTP.qdp`,
where the GPTP build copies it) into FireGraft's exe; **Repack files...** adds files in a
subfolder of the to-repack folder (`<exe folder>\to-repack\`; files at its top and `*.bak`
are left out) to the MPQ, imploded, with a filter by path and **Select all** (which leaves
the two warned files below to be ticked one at a time).
Both paths have **Change...** and **Default**, kept per exe; a changed folder also moves the
strings' to-repack copy for an exe not yet asked. The dialog lists each file as new, changed or
the same as the exe's; `rez\stat_txt.tbl` and `Manifold\buttonsets.bin` start unticked, with a
warning, when they differ from the exe's, since they would replace the editor's work. Unsaved
edits are saved first. The repack works on `<exe>.repacking`, checks that the plugin, every
added file and the editor's files read back, and swaps it in keeping `<exe>.bak`; any failure
leaves the exe as it was. The status line compares the exe's plugin with `GPTP.qdp`. Only
FireGraft's one-plugin layout is known; any other is refused. Tested in game 2026-10-24
(all of the spec's §8), including a plugin of another size.

## The Data page

The **Data** page (after Button sets and Strings) edits the 11 `.dat` files of the exe's
`arr\` folder, as PyMS's PyDAT does: units, weapons, flingy, sprites, images, upgrades,
techdata, sfxdata, portdata, mapdata and orders. It follows PyDAT's layout: a tab per file,
the units' sub-tabs (Basic, Advanced, Sounds, Graphics, StarEdit, AI Actions), PyDAT's groups,
labels and tooltips (its `Hints.txt`). Every field is there: numbers, references (a dropdown
and an id box), fixed lists, flags, and the derived displays (hit points' fraction, supply's
half, times in seconds at Fastest, flingy's speed in pixels per frame). Files in the expanded
layout (more entries than vanilla) open and save too.

The exe is the document, as on the other pages: there is no Open or New. Edits share the one
undo history with the button sets and strings, and Undo and Redo show the page, tab and entry
they changed. A tab with unsaved edits shows ●, and an entry that differs from vanilla shows
• in the list. A number box commits when it loses focus or on Enter; a value outside the
field's type is refused (the box goes red, its tooltip has the range). A field that does not
cover the entry (units.dat has some for entries 106-201 only) is disabled.

**The entry list.** Find (Ctrl+F) searches the names and goes to the next match; Jump to ID
(Ctrl+G) takes decimal or hex with `0x`. The **Names** box under the list is PyDAT's: Basic
(PyMS's lists), TBL Based (from `stat_txt.tbl`, `unitnames.tbl`, `images.tbl`, `sfxdata.tbl`,
`portdata.tbl` or `mapdata.tbl`, so a string renamed on the Strings page shows here) or
Combined, with **Simple TBL Names**; both are kept per file. The list's right-click menu has
**Revert entry** (to the file as opened, Ctrl+Shift+R) and **Revert entry to vanilla**, each
one undo step. Check lines under the tabs list a reference past the end of its table, a
sprite's selection circle image that names no image, and an expanded file the plugin refuses;
each is a link to the entry.

**Save and the to-repack copies.** Save writes every edited `.dat` file into the exe's MPQ
with the button sets and strings, then writes `<to-repack>\arr\<name>.dat` (kept as `.bak`),
so a later Repack files doesn't bring the old file back. A copy that fails to write is a
status line; the exe keeps the edit. Save as writes no to-repack copies. When an exe opens
and a to-repack `.dat` differs from the exe's, the opening dialog lists it with the strings
copy and asks which to edit. An expanded file of a table the plugin cannot extend (the plugin
reads images, sprites and flingy only) is flagged on opening, and the first Save that writes
one asks to confirm. The plugin's extension is on `origin/feat/dat-extender`, not on master.

**`Data\pydat\` must ship beside the exe.** It holds PyMS's name lists and `Hints.txt`
(MIT; `LICENSE.md` is in it). The build copies it from `fixtures\pydat\`.

**Links** (part 2 of the port, spec `docs/superpowers/specs/2026-10-25-dat-links-design.md`).

- **Jump buttons.** PyDAT's 29 "Jump ->" buttons sit beside the same reference fields. One
  shows the target file's tab and selects the entry. It is disabled when the field holds
  its None or an id past the target's end. It is not an edit, so nothing goes on the undo
  history. The Sprites tab's selection circle jumps to image 561 + its value.
- **Used by.** A collapsible **Used By (N):** list under every tab that other files point into (all
  but Campaign; the Units sub-tabs share one) shows each entry of each file whose field points at the shown entry, in PyDAT's
  words: `units.dat, Ground Weapon field (Basic sub-tab), entry 0: Terran Marine`. Double-click
  or Enter shows that file, sub-tab and entry. Sound ranges count both ends. Whether the
  list is open is remembered. On the Images tab the **Check** buttons beside GRP File and
  Iscript ID fill it with the other images that share the shown one's value.
- **Copy and paste.** The entry list's right-click menu has **Copy Entry to Clipboard**
  (Ctrl+Shift+C), **Copy Sub-Tab to Clipboard** (Ctrl+Shift+Y) and **Paste from Clipboard**
  (Ctrl+Shift+P). The clipboard holds PyDAT's text for the entry, so a unit copied here pastes
  into PyDAT and the reverse. Copy Sub-Tab copies only the shown Units sub-tab's fields (on
  the other tabs it copies the entry). Paste needs exactly one entry of the file's type and puts
  it on the selected entry, changing only the properties the text lists; it is one undo step.
  Text that is not an entry says "The clipboard holds no PyDAT entry".
- **TXT.** The **TXT** menu above the tabs has **Export to TXT...** (Ctrl+E), the whole current
  file as PyDAT's text, and **Import from TXT...** (Ctrl+I), which replaces the entries the file
  lists as one undo step. A mistake in the file refuses all of it, with the message and the line.
  Ids past the end of the file are refused (adding entries is a later part). An export is
  written to a temporary file first, so a failure leaves an old `.txt` whole.
- **Name overrides.** PyDAT's **Name Overrides** dialog, from the list's right-click menu
  (**Override Name**, Ctrl+Shift+N, the selected entry filled in). A line is `id:name` (replace) or
  `id+:name` (append a space and the text). **Open...** and **Save As...** read and write a PyDAT
  `.txt`. OK is one undo step. Overrides show wherever a name shows: the list, dropdowns and "Used by"
  rows. They are kept in the exe's MPQ, one file per .dat, as
  `Manifold\names\units.txt`, `Manifold\names\weapons.txt` and so on. Save writes only the
  files whose overrides changed (one emptied is written empty), and a tab's ● also shows for
  unsaved overrides. Repack files never touches them, as with `buttonsets.bin`. A line of an
  overrides file that does not parse is a status note; the other lines load.

**Previews** (part 3 of the port, spec `docs/superpowers/specs/2026-10-26-dat-previews-design.md`).

- **Pictures.** PyDAT's 257×257 preview, each behind its **Show Preview** box: Units Graphics
  (the unit's picture through graphics → flingy → sprite → image, with **Show StarEdit
  Placement Box (Red)**, **Show Dimensions Box (Green)** and **Show Addon Placement (Yellow)**
  on a parent building, 106 by default, for units that have an add-on position), Units
  StarEdit (the picture and its placement box), Sprites (the image over its selection circle
  and health bar, where the file has them) and Images (the image with its drawing function
  and remapping). Pictures are frame 0, drawn with PyMS's palettes (`Data\*.pal`: Units,
  Terrain for `thingy\tileset\`, and the fire remappings). Every box is off until ticked,
  and each is remembered. A preview follows its fields as they are edited and undone. What
  can't be drawn (a GRP not found or damaged, an id past a file's end) leaves the square
  black with the reason under it.
- **Icons.** Weapons, Upgrades, Techdata and Orders draw the icon beside its field, and
  **Select...** opens the button page's icon picker (search, and Unused: no button set uses it).
  A pick is one undo step.
- **Sound.** ▶ beside Sound File plays `sound\<the sfxdata.tbl string>` from the exe's MPQ,
  then vanilla; a new play stops the last. It is disabled for "No sound" and for a file that
  isn't found (its tooltip says which); a sound Windows can't play says why beside it.

The copy, paste, TXT and override keys work only while the Data page shows. Ctrl+Shift+Y
replaces PyDAT's Ctrl+Y because Ctrl+Y is Redo here.

Differences from PyDAT, each on purpose:

- The Jump gate lets the last entry be jumped to; PyDAT's stops one short.
- A "Used by" sound range written backwards (start above end) matches nothing.
- Text that a flags line cannot carry (bits above the coder's count) is kept on import: the
  entry's own bits there stay.
- The icon picker is the editor's own, and the order's highlight icon is drawn plain, as
  PyDAT's Orders tab draws it.

**Not yet** (the later parts of the port, in the spec's header): Add entry and Set count for images, sprites and flingy; improvements. Two
more differences from PyDAT: its seconds boxes are editable, here they are read-only text; and a
box is limited to its property's type, not to PyDAT's tighter ranges. Part 1 (the fields) passed on Windows 2026-10-25. Parts 2 (the links) and 3 (the
previews) passed on Windows 2026-10-26. Known limit: a GRP with more than 2400 frames (PyMS's limit; the Viking's assault-mode GRP) shows "damaged" though the game draws it.
