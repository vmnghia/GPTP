# Manifold Editor part 4: the .dat files (PyDAT's fields)

Status: design 2026-10-24. The user asked for the editor to edit the .dat files next:
"Porting the entirety of PyDAT is what I want, then add improvements on top", keeping the
plugin's .dat extension in mind. PyDAT is split into five parts, each with its own spec,
plan and test round, in this order (the user's choice):

1. **This spec:** the 11 formats (vanilla and expanded), and every field of every PyDAT
   tab on a Data page, with undo, revert and Save into the exe.
2. Links: Jump buttons, "Used by", copy and paste of an entry or a sub-tab, name
   overrides, PyDAT's TXT import and export.
3. Previews: icons, GRP frames with palettes, selection circles, the placement box,
   health bars, sound playback.
4. Entry counts: Add entry and Set entry count, for the tables the plugin can extend.
5. Improvements on top.

The user's answers: the parts in that order; the Data page follows PyDAT's layout (its tabs,
sub-tabs, groups and labels), layout changes waiting for part 5; a table-driven reader and
writer with hand-built tabs (approach C); per-entry revert in part 1.

The reference is PyMS at `bfc5d3a` (2026-06-23), MIT licence: `PyMS/FileFormats/DAT/*`
for the formats, `PyMS/PyDAT/*Tab.py` for the tabs, `Data\*.txt` for the name lists and
hints. Builds on the editor's parts 1-3 and the QoL changes
(`2026-10-05-button-set-editor-design.md`, `2026-10-08-string-editor-design.md`,
`2026-10-14-repack-design.md`, `2026-10-24-editor-qol-design.md`).

## 1. Goal

The user stops opening PyDAT for a field edit: every value PyDAT shows on its tabs can be
seen and changed here, and saving puts the file in the exe (and its to-repack copy) with
the button sets and strings. Success: §10's list passes on Windows, a file saved here opens
in PyDAT with the same values, and a game started on it uses them.

Replaced, not ported: PyDAT's New, Open, Open Directory, Open MPQ, Save MPQ, Save SEMPQ,
the MPQ and TBL settings, and "Set as default *.dat editor". The mod exe is the document,
as for the button sets and strings. Reload entry (PyDAT's Ctrl+R) becomes Revert entry
(§7).

## 2. The plugin's .dat extension

The extension is on `origin/feat/dat-extender` (327a021, ported from
saintofidiocy/GPTP@DatExtender), not yet on master and not yet run in game. It accepts
PyMS's expanded layout (§3) for **images.dat, sprites.dat and flingy.dat** only: a file of
vanilla's size loads as vanilla, an expanded file of those three moves the table, and an
expanded file of any other table is refused with the game's invalid-file warning. Its
entry sizes (images 38, sprites 7, flingy 15 bytes) and vanilla sizes are PyMS's.

So this part reads and writes the expanded layout of every file (an exe may already hold
an expanded images.dat), and says when a file is one the plugin refuses (§4). Making more
entries is part 4.

## 3. The formats (`Manifold.Core/Data/Dat/`)

`DatFormat` describes one file as PyMS's `DATFormat` does: its vanilla entry count, the
expanded layout's limits, and its properties in file order. A property has a name (PyMS's),
a type, and optionally:

- the entries it covers in the vanilla layout (`entry_offset`, `entry_count`), when not
  all: in units.dat infestation and add-on position cover 106-201, the ready, annoyed and
  yes sounds 0-105; in sprites.dat health bar, selection circle image and selection
  circle offset cover 130-516. In the expanded layout every property covers every entry.
- a different type in the expanded layout: units.dat `graphics` is a byte in vanilla, a
  short expanded.

Types: byte, short, long (unsigned, little-endian); size (2 shorts), position (2 shorts),
extents (4 shorts); hit points (a long: whole `>> 8`, fraction `& 0xFF`); supply (a byte:
whole `>> 1`, half `& 1`). A value is stored as the type's raw unsigned number; the
compound types are split for display only (§6).

| File | Vanilla entries | Bytes | Expanded entries |
|---|---|---|---|
| units.dat | 228 | 19876 | 250-912, multiple of 12, 228-250 reserved |
| weapons.dat | 130 | 5460 | up to 255 |
| flingy.dat | 209 | 3135 | up to 65536 |
| sprites.dat | 517 | 3229 | up to 65536 |
| images.dat | 999 | 37962 | up to 65536 |
| upgrades.dat | 61 | 1281 | up to 256 |
| techdata.dat | 44 | 836 | up to 255 |
| sfxdata.dat | 1144 | 10296 | up to 65536 |
| portdata.dat | 110 | 1320 | up to 65535 |
| mapdata.dat | 65 | 260 | none |
| orders.dat | 189 | 4158 | up to 255 |

The property lists are PyMS's `FORMAT` tables, copied in order with their names.

**Reading.** `DatFile.Parse(format, bytes)`:
- A file of vanilla's size is vanilla.
- Otherwise, a file that is a whole number of expanded entries is expanded, with that many
  entries, when the count is within the table's limits.
- Anything else is refused with a message naming both sizes, e.g. "images.dat is 38001
  bytes: not 37962, nor a whole number of 38-byte entries".

Values are held as one array per property. An entry outside a property's vanilla range has
no value there (`null`).

**Writing.** `DatFile.Save()` writes the arrays back in the file's own layout, so a file
read and not edited saves identical to its bytes.

**Edits.** `DatFile` is immutable, as `StringTable` is. `With(entry, property, value)`
returns a new file sharing the untouched arrays. A value outside the property's type range
is refused (an `ArgumentOutOfRangeException` the page never triggers, since its boxes
check first), and so is a value for an entry the property doesn't cover.

`UnitsDat` (the set list's race and kind) becomes a view over the units `DatFile`, so it
reads the edited file (§5).

## 4. Opening

Opening an exe also reads these, each through the existing resolver (the exe's MPQ first,
then vanilla's MPQs), each named in the status bar with where it came from:

- the 11 `arr\*.dat` files;
- `arr\images.tbl`, `arr\sfxdata.tbl`, `arr\portdata.tbl`, `arr\mapdata.tbl`;
- `rez\unitnames.tbl`, when present (PyMS's extended unit names);
- `scripts\iscript.bin`, for its list of script ids only (the header's id table).

The vanilla copy of each .dat file is read from vanilla's MPQs alone, for Revert to vanilla
(§7).

A missing or unreadable TBL or iscript.bin is a note, as `cmdicons.grp` is today: the
fields that name from it show numbers. A .dat file the exe holds that fails §3's checks
opens as vanilla's copy, with the status line "arr\images.dat: the exe's copy was refused
(reason); vanilla's is shown". Saving then writes it only if it is edited.

An expanded file of a table the plugin can't extend (any but images, sprites and flingy)
opens and edits normally, with the status line "arr\units.dat is extended (250 entries):
the plugin refuses it and the game will show its invalid-file warning". The first Save
that writes such a file asks once per session: "Save anyway / Cancel".

## 5. The document, history and saving

**Document.** `ButtonSetDocument` holds the 11 `DatFile`s, their opened copies and their
vanilla copies, beside the sets and strings. `DatDirty(file)` is "differs from the file
as the exe holds it" (compared as values, so an edit undone by hand is clean; a
to-repack copy chosen on opening counts as unsaved); `IsDirty` includes them.

**History.** A `Change` can hold .dat edits `(file, entry, property, before, after)`,
alongside its set and string edits: still one history for the whole exe. `Place` gains
`(DatId, Entry)`, so Undo and Redo show the Data page, the file's tab (and the units
sub-tab that holds the property) and the entry. One field edit is one step. A number box
commits when it loses focus or on Enter, not per key, and a value equal to the current one
records nothing. A compound display (hit points' whole and fraction, supply's whole and
half) commits as one edit of its one property. A flag checkbox is one step.

**Saving.** Save writes each .dat file that is `DatDirty` into the exe's MPQ, in the same pass as `Manifold\buttonsets.bin` and `rez\stat_txt.tbl` (the existing
`FilesToSave`). After the exe is saved, it writes `<to-repack>\arr\<name>.dat` for each,
as the strings copy is written (`StringMirror.Write`: a temporary file, then replace,
keeping `.bak`), using the exe's to-repack folder setting. A copy that fails to write is a
status line naming it; the exe keeps the edit. Save as copies the files along with the
rest, as today, and writes no to-repack copies (as for strings, QoL spec §9).

**On opening.** The existing to-repack check covers the .dat copies: when
`<to-repack>\arr\<name>.dat` differs from the file the editor opened, the opening dialog
lists every such file (strings and .dat files together), says for each which side was
changed more recently, and offers the existing choices (the exe's, or to-repack's for all
of them).

**Other pages.** The set list's race and kind grouping reads the current units `DatFile`:
editing a unit's StarEdit race or its hero, building, add-on or subunit flags regroups its
set on the next refresh.

## 6. The Data page

A third page, "Data", after Button sets and Strings in the page bar. `LastPage` gains
"data"; the settings also remember the last file tab, units sub-tab and entry per file.

**Layout (PyDAT's).**

- **Left:** the entry list of the current file, "ID  Name" with the id padded as PyDAT pads
  it, in a virtualised ListView (65536 entries stay quick). Above it, PyDAT's Find box (a
  case-insensitive search of the names; Enter or the button selects the next match after
  the current entry, wrapping) and Jump to ID (decimal, or hex with `0x`; Enter jumps).
- **Top:** a tab per file, PyDAT's names and order: Units, Weapons, Flingy, Sprites,
  Images, Upgrades, Techdata, Sound Effects, Portraits, Campaign, Orders. A tab whose file
  is `DatDirty` shows ●.
- **Units:** PyDAT's sub-tabs: Basic, Advanced, Sounds, Graphics, StarEdit, AI Actions.
- **Each tab:** PyDAT's groups, labels and field order, from its `*Tab.py`. For example
  Basic has Vital Statistics (hit points, shields, armor, armor upgrade), Build Cost,
  Weapons, Supply, Space, Score and Other. The places of PyDAT's Jump buttons, "Used by"
  lists and previews are left out (parts 2 and 3), not stubbed.
- **Context menu on the list:** Revert entry to opened, Revert entry to vanilla (§7).

**Fields.**

- **Number:** a box limited to the property's type (0-255, 0-65535, 0-4294967295); a value
  outside it is refused, the box red with the range as its tooltip, and nothing is
  recorded.
- **Reference (dropdown + id box),** as PyDAT draws them: picking an entry sets the box,
  typing an id selects the entry, and an id past the list's end is kept and shown as
  "#300 (no name)". Lists longer than a screen (units, weapons, flingy, sprites, images,
  sounds, orders, iscript ids, the TBLs) use the QoL filter-as-you-type box
  (`FunctionBox`'s pattern). "None" entries follow PyDAT (e.g. a weapon of 130 is None).
- **Fixed lists:** dropdowns from PyMS's lists (behaviours, damage types, explosions, unit
  sizes, right-click actions, elevation levels, draw functions, remapping, flingy control,
  races, animations, selection circle sizes, shield sizes and the rest).
- **Flags:** a checkbox grid with PyMS's flag names: special ability flags, the unknown
  flags, AI internal flags, StarEdit group and availability flags, target flags, sound
  flags, and the orders.dat booleans. A property split across tabs (StarEdit group
  flags: the races on Basic, the rest on StarEdit) is one property: each checkbox changes
  its own bit.
- **Derived displays,** PyDAT's: build, research and cooldown times in seconds at Fastest
  (PyDAT's `TicksPerSecond.fastest`); flingy's top speed in pixels per frame and halt
  distance in pixels; hit points' whole and fraction; supply's whole and "+0.5"; the
  sound's portrait length adjust as PyDAT shows it.
- **Not covered:** a field whose property doesn't cover the entry in the vanilla layout
  (§3) is disabled and empty, as in PyDAT.
- **Tooltips:** PyMS's `Hints.txt`, per field.

**`DatTabs` (Core).** The table of which properties each tab and sub-tab shows (the groups
are layout, kept in the hand-built tabs). The hand-built tabs ask it for their fields' properties, so a name can't be
misspelt in the App; part 2's copy sub-tab reads it. A property may show on two tabs, as in
PyDAT (the weapons on Basic and AI Actions, the placement size on Graphics and StarEdit,
the group flags on Basic and StarEdit). A test checks that every property of every format
shows on at least one tab.

**Names (`DatNames`, Core),** following PyMS's `DATEntryName`, with PyDAT's **Names**
dropdown under the list (Basic, TBL Based, Combined; Basic by default) and its **Simple TBL
Names** checkbox (units, weapons, upgrades, techdata and orders), both remembered per file:
- Basic: PyMS's list (`Units.txt`, `Weapons.txt`, `Flingy.txt`, `Sprites.txt`,
  `Images.txt`, `Upgrades.txt`, `Techdata.txt`, `Sfxdata.txt`, `Portdata.txt`,
  `Mapdata.txt`, `Orders.txt`);
- TBL Based: units, `rez\unitnames.tbl` when present, else `stat_txt.tbl`'s first 228
  strings (the Strings page's strings, so a rename there shows here); weapons, upgrades,
  techdata and orders, their `label` in `stat_txt.tbl`; images, their `grp_file` in
  `images.tbl`; sprites, their image's; flingy, their sprite's; sounds, portraits and maps,
  their file in `sfxdata.tbl`, `portdata.tbl` (idle) and `mapdata.tbl`;
- Combined: "list name (TBL name)", or one of them when they are the same or one is
  missing;
- without a name: "Image #1000", or "Expanded Image #1000" past vanilla's count; units
  228-250 of an expanded units.dat are "Reserved Unit #N";
- the TBL dropdowns (images.tbl, sfxdata.tbl, portdata.tbl, mapdata.tbl) show the string,
  with 0 as "None" where PyDAT does.

PyMS's lists and hints ship in `Data\` with PyMS's MIT notice, beside `Icons.txt`.

**Keys.** Ctrl+F focuses Find, Ctrl+G focuses Jump to ID; Ctrl+Z, Ctrl+Y and Ctrl+S as on
the other pages.

## 7. Revert entry

Two commands on the entry list's context menu (and Ctrl+Shift+R for the first):

- **Revert entry to opened:** every property of the entry back to the exe's file as opened.
- **Revert entry to vanilla:** every property back to vanilla's file. Disabled for an entry
  past vanilla's count.

Each is one undo step, labelled "Revert entry" / "Revert entry to vanilla"; nothing is
recorded when the entry already matches. The entry list marks an entry that differs from
vanilla with •, so a changed entry is easy to find.

## 8. Checks

The Data page lists check lines under its tabs, for every file (PyDAT checks none of
these; more in part 5):

- a reference id past its target's entry count (a units `ground weapon` of 140 with 130
  weapons, unless it is that field's None);
- a sprite's selection circle image that names no image (the field holds `image - 561`,
  so it names images 561-816; the check is that the image exists);
- an expanded file of a table the plugin refuses (§4).

Each line names the file, entry and field, and is a link that shows that entry.

## 9. Errors

- A .dat file that fails §3: opened as vanilla's (§4).
- A save that fails leaves the exe as it was, as today (work copy, then `File.Replace`);
  the to-repack copies are written only after the exe is saved.
- A value outside its type: refused in the box (§6), never written.
- iscript.bin or a TBL that can't be read: the fields show numbers; the note says which.

## 10. Testing

**Core (Linux, `dotnet test`):**
- Every vanilla .dat file reads and saves byte for byte, and each property's values are
  spot-checked (Marine's hit points 40, its ground weapon 0, Gauss Rifle's damage 6, and
  so on). These read `MANIFOLD_ARR_DIR` (a folder of vanilla's `arr\*.dat`) and are skipped
  without it, as `MANIFOLD_SC_DIR` is; the Linux session runs them on PyMS's copies.
- Built files test the expanded layout of each table (round trip, every property covering
  every entry, units' short `graphics`), refused sizes, units' expanded limits, and
  properties with a vanilla range.
- Edits: type ranges, uncovered entries, sharing; the history with `Place`, undo and redo;
  dirty marks; revert to opened and to vanilla; Save's file list and the to-repack copies;
  the opening check for newer copies; the units view regrouping after an edit.
- Names: unitnames.tbl first, stat_txt labels, lists running short, expanded ids.
- `DatTabs`: every property of every format shows on at least one tab; every PyMS list
  the editor reads is in `Data\`.
- Checks: each case in §8.

**App:** the compile check on Linux. On Windows, the user:
1. Opens the exe: the status bar names each .dat file's source; the Data page shows the
   Units tab with the Marine.
2. On each tab and units sub-tab, changes one field of each kind (number, reference, fixed
   list, flag, derived display) and sees ● on the tab.
3. Undo and Redo return to the right tab, sub-tab and entry.
4. Revert entry to opened and to vanilla.
5. Saves, reopens: the values hold; `to-repack\arr\` has the files.
6. Opens the saved files in PyDAT: the same values.
7. Starts a game: a changed value shows (e.g. the Marine's hit points).
8. Find and Jump to ID, including hex.
9. A set's race changed in StarEdit flags regroups the set list.

## 11. Out of this part

Parts 2-5 (links, previews, entry counts, improvements); PyDAT's TXT import and export
(part 2); a blank or new file; editing TBL names from the Data page (the Strings page does
that; a shortcut is a part 5 idea).
