# Button set editor (Manifold Editor, part 1: core and button sets)

Status: design approved 2026-10-05; built 2026-10-06 (plan `docs/superpowers/plans/2026-10-05-button-set-editor.md`). The editor is tested on Windows (2026-10-07, §7 steps 1, 2 and 5's save side; see "Settled in testing"); the GPTP loader is built and tested in game (2026-10-07, §7 steps 1-3; step 4, a corrupt file, not yet run). Replaces the abandoned FireGraftEx WPF
skeleton (`D:\SC Modding\FireGraftEx`, outside git; only its `Data\FireGraft\*Func.txt`
lists are reused).

## 1. Goal

A FireGraft replacement for the Manifold mod, one part at a time. This spec is part 1:
a lean core and the button set editor. Requirements, exe edits, `.dat` editing and
repacking `GPTP.qdp`/`rez\` are later specs.

What the user asked for:
- Button sets on the 5x3 command card (positions 1-15, `docs/resolution.md` §5a), which
  FireGraft cannot place or show.
- Drag and drop on the card instead of typing positions; easy copy and paste of buttons
  and of whole sets; undo and redo.
- Edits reach the game quickly, stored in the mod exe, without ever locking it. FireGraft
  holds the exe open while running, so PyMPQ and other tools can read it but not save.
- What the exe does not hold falls back to the vanilla MPQs, as in FireGraft.

Success: open `SCManifold.exe`, drag Siege Mode to 11 and Tank Mode to 12 on the Siege
Tank's card, save, start `SCManifold.exe`, and see the new card, with no FireGraft and no
repack step.

Scope: StarCraft 1.16.1 and the Manifold mod only.

## 2. Facts this rests on (checked 2026-10-05)

- `SCManifold.exe` is an MPQDraft-style launcher: a small stub, FireGraft's runtime plugin
  in its resources, and an MPQ at file offset 0x39B600 holding `GPTP.qdp`, `rez\*` and
  `Firegraft\*`. The real `D:\Games\Starcraft 1.16.1\StarCraft.exe` is never modified.
- FireGraft's project is inside that MPQ as `Firegraft\SCManifold.fgp` ("FgPa", version 7,
  "1.16.1"), a list of sections, each `char[4] tag, u32 length, data`: `Unit`, `Buts`,
  `UntR`, `UpgR`, `TecR`, `TecU`, `OrdR`, `ExEd`, `SUni`, then `SBut`, `SUnD`, `SUpg`, `SRes`,
  `SUse`, `SOrd`, `SExe` (only `Buts` is used). FireGraft's runtime applies it
  when the plugins load, before any game starts. This is assumed, not traced: the plan's
  loader task confirms it with an on-screen trace of the table entry before and after.
- `Buts` holds only the changed sets: `u16 setCount`, then per set `u8 setId, u8
  buttonCount`, then 20-byte buttons `u16 position, u16 icon, u32 conditionIndex, u32
  actionIndex, u16 conditionVar, u16 actionVar, u16 enabledStringId, u16
  disabledStringId` (corrected 2026-10-06: 0x10 is the enabled tooltip, whose first
  character is the hotkey; 0x12 the disabled text; every button has the first, only
  buttons that can be disabled the second). The real file parses exactly (18 sets, 2438 of 2438 bytes): sets 11,
  12, 13, 15, 22, 67-70, 73-80, 90, positions 1-9 only.
- **Corrected 2026-10-08: those set ids are FireGraft's own numbers, not unit ids.** The
  first import put them on table entries 11, 12, ... (the Dropship, the Battlecruiser,
  ...), which was wrong. FireGraft keeps a tree of named, shared sets (`SBut`: "[11] Mixed
  Group", "[68] Marine/Firebat + Heroes", "[76] Siege Tank + Heroes", ...), and its `Unit`
  section links table entries to them: `u16 count`, then per record `u8 entry, u8
  buttonCount, u8 FireGraft set + 1, u16 connectedUnit (0xFFFF none), u8 n`, then `n`
  three-byte items of unknown meaning. The user's project has 64 records: Marine, Gui
  Montag, Firebat and the Firebat hero use 68; the Siege Tanks and Duke use 76; the
  mixed-group card (244) uses 11. The import now gives each linked entry its own copy of
  the set (one set per unit, the user's choice on 2026-10-08), and **Re-import
  FireGraft's sets** repairs an exe saved with the first import.
- The indexes count from 0 in FireGraft's lists. `FireGraftConFunc.txt` (67 lines) and
  `FireGraftActFunc.txt` (60 lines) give name and address per index: set 11's first
  button is condition 6 (`Mixed Group - Move/Patrol/Hold Position`, 0x428DA0), action 9
  (`Move`, 0x424440), string 664, icon 228, which is vanilla Move. FireGraft's own
  `Firegraft\reqlist.txt` has 68 condition entries, one more than the address list; the
  largest index the project uses is 65.
- The vanilla table `buttonSetTable` (0x5187E8, `BUTTON_SET {u32 buttonsInSet; BUTTON
  *firstButton; u32 connectedUnit}`) has 250 entries, 0-249; 228 is empty, 229-249 are
  the build menus and other non-unit cards. `BUTTON` (GPTP `structures.h`) is the same
  20-byte layout as above with function addresses in place of indexes.
- FireGraft's inputs (its Options page): the StarCraft exe path (from the registry),
  `rez\stat_txt.tbl`, `unit\cmdbtns\cmdicons.grp`, `Firegraft\iconlist.tbl`, each "from
  MPQ". `SCManifold.exe` has none of the three, so they come from the vanilla MPQs.

### Settled in testing (2026-10-07)

- **Saving grows the MPQ inside the exe safely**: a copy of `SCManifold.exe` saved by the
  editor still starts and plays a custom game.
- **PyMS's `StormLib.dll`**: stdcall, ANSI paths, and its success flags are C++ `bool` (one
  byte), not Win32 `BOOL`; read as four bytes, a failed open came back true.
- **`stat_txt.tbl`** ids count from 1; many button strings are the hotkey, a NUL, then the
  text (Move: `m`, NUL, `\x03M\x01ove`).
- **Icons** are drawn with PyMS's `Icons.pal`, not `ticon.pcx`'s palette.
- **The `.fgp`** is found by listing `Firegraft\*.fgp` when it isn't named after the exe (a
  renamed copy). The `.fgp` holds only the sets changed in FireGraft: 18 here, used by
  64 table entries (see the 2026-10-08 correction in §2).
- **The window** works built in C# without `.xaml`.
- **Compression**: StarCraft 1.16.1's Storm has no zlib. A zlib-compressed
  `Manifold\buttonsets.bin` let the exe start (Storm only decompresses on read) but stopped
  the game at the loader's read with "The file data is corrupt". The editor now implodes
  it (PKWARE), as the game's own MPQs do.
- **The loader**: storm.dll's file functions by ordinal (253, 265, 268, 269) work, and Storm
  finds `Manifold\buttonsets.bin` in the launcher's MPQ at game start. A moved button
  shows on the card with its enabled and disabled strings, its hotkey works, and a replay
  keeps the 3x3 card.
- **Repacking** is done by hand with PyMPQ, which adds files in place, so the editor's file
  survives adding a new `GPTP.qdp`.

### Settled in testing (2026-10-08, in the editor)

- **Re-import FireGraft's sets** lists and replaces the right sets: Marine, Firebat, both
  Siege Tank modes, SCV, Ghost and the mixed-group card take FireGraft's layouts, the
  Dropship and Battlecruiser vanilla's.
- **Copy set to units** changes every set picked, and one undo takes them all back.
- **Rename...** opens the unit's name string; the set list follows the edit.
- **The cards 228-249** show their names.
- **Set names** carry the subname (both Siege Tank modes, Edmund Duke's two), and the race,
  type and "only sets with buttons" filters work.
- In game (2026-10-09), once sets were written sorted: every set checked matches the
  editor, re-imported ones included. Copy and paste shortcuts passed.
- Not yet run: separating buttons that share a slot (the 2026-10-08 change in §5).

## 3. Pieces

**`tools/ManifoldEditor/`** in this repo: a WinUI 3 app (C#, .NET, unpackaged and
self-contained, x86 so it can load the 32-bit `StormLib.dll` that PyMS ships). Layers:
- **Data** (no UI): MPQ access through StormLib; the button set file (§4); the `Buts`
  import; vanilla sets read from `StarCraft.exe`'s file image at 0x5187E8; `stat_txt.tbl`;
  `unit\cmdbtns\cmdicons.grp` drawn with PyMS's `Palettes/Icons.pal`, as PyMS's
  `PyDAT/DataContext.py` does (corrected 2026-10-07: `ticon.pcx`'s pixels only recolour
  highlighted icons). Button strings are often stored as the hotkey, a NUL, then the text. Resources are looked up in the mod exe's MPQ first,
  then `patch_rt.mpq`, `BrooDat.mpq`, `StarDat.mpq`. Icon names come from PyMS's
  `Data/Icons.txt`, copied into the repo (in place of FireGraft's `iconlist.tbl`).
- **Model**: 250 sets, each an ordered list of buttons. Move, swap, copy, paste, delete,
  revert and field edits are operations here, each with its inverse, so undo, redo and
  tests do not need the UI.
- **UI**: the window in §5. Built (2026-10-06) in C# with no `.xaml` files, so it compiles
  outside Windows too; its logic (set list, card cells, commands, check box text) lives in
  `Manifold.Core/Editor/` with the other tested code.
- **Tests**: a C# test project for Data and Model (§7).

Settings are the StarCraft folder (from the registry, overridable) and the last exe
opened. No splash screen, update check or autosave.

**The button set file** `Manifold\buttonsets.bin`, stored in the mod exe's MPQ (§4).

**GPTP loader**: a new file in `hooks/interface/`, applying the file at game start (§4).

The condition and action tables (index, name, address) are built from FireGraft's two
`*Func.txt` lists, copied into the repo under `tools/ManifoldEditor/`.

## 4. The button set file and the loader

Little-endian:

```
header  char[4] "MBTS", u16 version = 1, u16 setCount = 250
per set u16 buttonCount, u16 reserved = 0, u32 connectedUnit
        buttonCount x button (20 bytes)
button  u16 position, u16 iconID, u32 condition, u32 action,
        u16 conditionVar, u16 actionVar, u16 enabledStringID, u16 disabledStringID
```

- The button record is GPTP's `BUTTON` byte for byte; condition and action are
  `StarCraft.exe` 1.16.1 addresses. All 250 sets are written, not only the changed ones,
  so the game never mixes these sets with FireGraft's or vanilla's.
- Buttons may share a position; the first whose condition passes is shown, as in vanilla
  (a second one that passes too spills into the next slot).
- **Corrected 2026-10-09: the file holds each set sorted by position**, buttons sharing
  one in their order. The game's card loop (0x4591D0) walks slots 1-15 and the buttons
  together and places a button when the slot reaches its position, so a button listed
  after a higher position lands in a later slot. The editor's moves only change
  positions, and writing the list as it stood put Attack and Patrol at 6-7 on the
  Zergling, the Hive's research on the bottom row, and so on. The editor still keeps its
  own order while editing; the file is sorted when written. Tested in game 2026-10-09:
  re-saving made every set match the editor.
- Saving: StormLib opens the exe's MPQ, writes the file in one operation, and closes it.
  The exe is not held open between opening and saving.

The loader, at each game start (replays included):
- Reads `Manifold\buttonsets.bin` through Storm, which already sees the launcher's MPQ.
- Checks before changing anything: magic, version, `setCount == 250`, every set's size
  inside the file, every position 1-15, every condition and action address inside
  `StarCraft.exe`'s code section.
- On success copies the records into one buffer it owns and points all 250 table entries
  at their slices, with `buttonsInSet` and `connectedUnit` from the file. Running after
  FireGraft's runtime has applied the `.fgp`, it wins. Applying again rebuilds the same
  thing.
- On failure changes nothing and prints `buttonsets.bin: <reason>` once with
  `scbw::printText`; the game keeps FireGraft's sets. A missing file prints nothing.
- Replays use the 3x3 card, which already hides positions above 9 (`docs/resolution.md`
  §5a).

Every player needs the same exe, as with FireGraft today. The card only decides what can
be clicked; commands are still checked when received, so a mismatch shows in the UI and
does not desync.

## 5. The editor window

Mock-up shown to the user on 2026-10-05 (dark theme, three columns):

1. **Toolbar**: Open exe, Save (Ctrl+S), Undo (Ctrl+Z), Redo (Ctrl+Y).
2. **Set list**: all 250 sets, grouped Terran, Zerg, Protoss, Neutral/heroes, Menus
   (228-249), with id and a search box. A dot marks sets that differ from vanilla; the
   title bar shows when there are unsaved changes.
3. **The card**: the 5x3 grid with icons from `cmdicons.grp`.
   - Drop on an empty cell moves the button; on a filled cell swaps them; Ctrl+drag
     copies.
   - Right-click a button: Copy, Paste, Delete. Paste on an empty cell adds the button
     there.
   - Copy set / Paste set work on whole sets, between any two sets.
   - Revert set: to the version opened from the exe, or to vanilla.
   - A cell holding several buttons shows a count badge; selecting it lists them in order.
   - **Changed 2026-10-08 (the user's choice), for slots several buttons share:** a drag
     moves one button, not the slot: the selected button if it is in the dragged cell,
     else the icon pressed. A filled target swaps (its buttons go where the button came
     from); Alt+drop stacks onto it; Ctrl copies the button; Shift moves the whole slot
     and Ctrl+Shift copies it. Rows of the button list drag onto the card the same way.
     A shared cell draws its first three buttons as a fanned stack, the first (the one the
     game prefers) in front; clicking or dragging an icon picks that button. The button
     panel has a **Slot** box (1-15) that moves the selected button alone, stacking when
     the slot is taken, with a note naming what shares it and slots replays hide.
4. **Check box**: the card's hotkeys, a warning when two shown buttons share one, and the
   buttons a replay's 3x3 card will not show.
5. **Button panel**: icon (picked from a grid of icons), condition and action (dropdowns
   with FireGraft's names and their addresses), the two vars, the two string ids with
   their text from `stat_txt.tbl`. The hotkey is the first character of the enabled
   string.

Status bar: where each resource came from, and the import summary.

**First open of an exe without `Manifold\buttonsets.bin`**: the sets are vanilla plus the
`.fgp`'s `Buts` sets, and the status bar reports how many were imported. Saving writes
the file; from then on the file is the source and the `.fgp` is not read again.

**Undo and redo**: every operation of the model is one step, across all sets. Undoing a
change in another set shows that set. A field edit is one step when it is committed, not
one per keystroke. Saving does not clear the history; it ends when the program closes.
Closing with unsaved changes asks first.

Out of scope: editing `stat_txt.tbl`, adding sets beyond the 250, conditions or actions
written in GPTP (the u32 fields leave room for these later).

## 6. Errors

- The exe cannot be opened or has no MPQ: a message, nothing is loaded.
- The exe is busy at save (FireGraft open, a repack running): a message naming the likely
  cause; the edits stay unsaved in the editor. Nothing is half-written.
- A resource is missing everywhere: the editor still opens and shows string ids and icon
  numbers in place of text and pictures, with a note in the status bar.
- A `Buts` set that cannot be imported (an index with no address, such as condition 67,
  or a position outside 1-15): that set stays vanilla and is listed in an import report.
- The loader's checks in §4.

## 7. Testing

C# tests (Data and Model):
- The button set file round-trips byte for byte.
- The import of the real `SCManifold.fgp` gives the 18 sets in §2, and set 11's first
  button decodes to vanilla Move.
- The vanilla sets read from `StarCraft.exe` match the counts and the first buttons of a
  few known sets.
- Every operation: doing it then undoing it gives back the exact set.
- Resource lookup prefers the mod exe's MPQ over the vanilla MPQs.

GPTP host test for the loader's parser, in the style of `sel_selftest.cpp`: a good file is
accepted; each kind of bad file (magic, version, set count, truncated set, position 0 or
16, an address outside the code section) is refused with its reason.

In-game round:
1. Open `SCManifold.exe` in the editor with FireGraft closed; the status bar reports 18
   imported sets.
2. Move Siege Mode to 11 and Tank Mode to 12 on set 5, save, run: both show; hotkeys work.
3. A replay shows the 3x3 card without them, as expected.
4. An exe with a corrupt `buttonsets.bin` prints the reason and plays with FireGraft's
   sets.
5. Save while FireGraft has the exe open: the editor reports it and keeps the edits.
