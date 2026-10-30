# Manifold Editor: the .dat files, part 2 (PyDAT's links)

Status: Built 2026-10-25 (plan `docs/superpowers/plans/2026-10-25-dat-links.md`): the
rules are tested on Linux, the window compiles; all of §8 passed on Windows 2026-10-26. Design 2026-10-25. Part 2 of the PyDAT port planned in
`2026-10-24-dat-editor-design.md` (part 1, the fields, passed on Windows 2026-10-25):
Jump buttons, "Used by", copy and paste of an entry or a sub-tab, name overrides, and
PyDAT's TXT import and export. The user's answers: name overrides are kept in the exe;
the TXT reader and writer, and the "Used by" references, are data tables in Core
(approach A), as part 1's formats are.

The reference is PyMS at `bfc5d3a` (MIT): `PyMS/FileFormats/DAT/AbstractDAT.py`
(`parse_text`, `_export`, `import_entries`), `DATCoders.py`, each `*DAT.py`'s
`_export_data`/`_import_data` and `_*PropertyCoder`, `PyMS/PyDAT/DATRef.py`, `DATTab.py`,
`EntryNameOverrides.py`, and the `DATRefs`/`Jump` uses in `PyMS/PyDAT/*Tab.py`.

## 1. Goal

What the user does in PyDAT to move between entries and to move data in and out works
here the same way, with PyDAT's own text so the two tools can swap entries. Success: §8's
list passes on Windows; a unit copied in the editor pastes into PyDAT and the reverse; a
file exported here imports into PyDAT unchanged and the reverse.

## 2. The TXT format (`DatText`, Core)

PyMS's text, exactly. An entry is a header line, then one line per value, then a blank
line:

```
Unit(0):
	graphics 78
	hit_points.whole 40
	hit_points.fraction 0
	unknown_flags.flag_0x01 True
	...
```

- **Headers** (PyMS's `EXPORT_NAME`): `Unit`, `Weapon`, `Flingy`, `Sprite`, `Image`,
  `Upgrade`, `Technology`, `Sound`, `Portraits`, `Map`, `Order`; `Name(id):`.
- **Lines:** a tab, the key, a space, the value. Properties in PyMS's export order (each
  `_export_data`, which follows the format's order).
- **Coders** (PyMS's `DATCoders`): plain numbers; bools (`True`/`False` for 0/1, other
  values as numbers); flags, one line per bit up to the coder's bit count, with PyMS's
  names (`special_ability_flags.building`), else `flag_0x%02X`; hit points
  (`.whole`, `.fraction`); supply (`.whole`, `.half` as `True`/`False`); size (`.width`,
  `.height`); position (`.x`, `.y`); extents (`.left`, `.up`, `.right`, `.down`); portraits'
  `idle.*` and `talking.*` (`portrait_file`, `smk_change`, `unknown`).
- **Absent values:** a property the entry doesn't cover (sprites' health bar under 130 in
  a vanilla file) is left out, as PyMS does.

`DatText` holds, per file, a table: property → TXT key and coder, copied from PyMS's
`_*PropertyCoder` classes. It offers:

- `Export(DatFile file, IEnumerable<int> entries, IReadOnlyCollection<string>? properties)`
  → text (a subset of properties for a sub-tab copy);
- `Parse(string text)` → entries, by PyMS's `parse_text` rules (comments after `#`,
  `Name(id):` headers, `key value` / `key.sub value` lines, values numbers or
  `True`/`False`), refusing as PyMS refuses: a property before any header, an unexpected
  line, an entry repeated, an empty entry, no entries;
- `Apply(DatFile file, parsed, int? onto)` → the new file, or a refusal: the wrong type
  ("Invalid type (expected 'Unit', got 'Weapon')"), an unknown property ("Unrecognized
  property 'foo'"), an unknown flag name, a compound value missing a part ("Hit Points
  missing `whole` value"), an id past the file's end, a value too big for its field. A
  refusal names the line where it can. Nothing is half-applied. `onto` puts a single
  entry on that entry, ignoring its header's id (paste).

## 3. Copy, paste, import and export

On the entry list's right-click menu, PyDAT's names:

- **Copy Entry to Clipboard** (Ctrl+Shift+C): the entry's text, all its properties, on the
  Windows clipboard.
- **Copy Sub-Tab to Clipboard** (Ctrl+Shift+Y; PyDAT's Ctrl+Y is the editor's Redo): only
  the properties the shown sub-tab lists in `DatTabs`. Only the Units tab has sub-tabs;
  elsewhere the command copies the entry.
- **Paste from Clipboard** (Ctrl+Shift+P): the text must hold exactly one entry of this
  file's type; it goes onto the selected entry. Only the properties in the text change.
  One undo step, "Paste entry". A clipboard without text, or text that isn't an entry:
  "The clipboard holds no PyDAT entry".

A **TXT ▾** menu above the tabs:

- **Export to TXT...** (Ctrl+E): the current file, every entry, through a save dialog
  suggesting `units.txt`; written to a temporary file, then replaced.
- **Import from TXT...** (Ctrl+I): the entries the file lists replace those entries of the
  current file. One undo step, "Import units.txt (12 entries)". Any refusal refuses the
  whole file, with the message and line. Ids past the end are refused (adding entries is
  part 4).

Undo of a paste or an import shows that file and the first entry it changed. These keys
work only while the Data page shows.

## 4. Jump buttons

PyDAT's 29 "Jump ->" buttons, beside the same reference fields: Basic (armor upgrade,
ground and air weapon), Advanced (infestation, subunit 1 and 2), Sounds (all seven),
Graphics (graphics, construction, portrait), AI Actions (all five orders), Weapons
(graphics, upgrade), Flingy (sprite), Sprites (image, and the selection circle, which
jumps to image 561 + its value), Orders (targeting, energy, obscured order). A button
shows the target file's tab and selects the entry; disabled when the field holds its None
or an id past the target's end. It is not an edit: nothing is recorded. `DatFields.ReferenceField` gains an optional jump
setting.

## 5. "Used by"

Under every tab (the Units sub-tabs share one), PyDAT's collapsible **Used By (N):** list:
every entry of every file whose field points at the shown entry. Rows are PyDAT's text:
`units.dat, Ground Weapon field (Basic sub-tab), entry 0: Terran Marine`, with names as
the Names setting gives them. Double-click or Enter shows that file, sub-tab and entry. The
list's open or collapsed state is remembered.

`DatRefs` (Core) is PyDAT's table, each row (from file, property, label, sub-tab, optional
range-end property):

| Shown file | Used by |
|---|---|
| units | units subunit1 "Subunit 1", subunit2 "Subunit 2", infestation "Infestation" (Advanced) |
| weapons | units ground_weapon "Ground Weapon", air_weapon "Air Weapon" (Basic); orders targeting "Targeting" |
| flingy | units graphics "Graphics" (Graphics); weapons graphics "Graphics" |
| sprites | flingy sprite "Sprite" |
| images | units construction_animation "Construction" (Graphics); sprites image "Image" |
| upgrades | units armor_upgrade "Upgrade" (Basic); weapons damage_upgrade "Upgrade" |
| techdata | orders technology_energy "Energy"; weapons unused_technology "Unused" |
| sfxdata | units ready_sound "Ready"; what_sound_start..what_sound_end "What"; pissed_sound_start..pissed_sound_end "Annoyed"; yes_sound_start..yes_sound_end "Yes" (Sounds) |
| portdata | units portrait "Portrait" (Graphics) |
| orders | units comp_ai_idle "Computer Idle", human_ai_idle "Human Idle", return_to_idle "Return to Idle", attack_unit "Attach Unit", attack_move "Attack Move" (AI Actions) |

A range row matches an entry from its start to its end, inclusive. Images also has PyDAT's
two **Check** buttons, beside GRP File and IScript ID: they fill the list with the other
images whose `grp_file` (or `iscript_id`) is the shown one's, and open it if collapsed.
The list is worked out when an entry is shown, by one pass over the referring files.

## 6. Name overrides

Kept in the exe's MPQ, one file per .dat, in PyDAT's format: `Manifold\names\units.txt`,
`Manifold\names\weapons.txt`, ... (the .dat's name with `.txt`). A line is `id:name`
(replace) or `id+:name` (append), PyMS's `\s*(\d{1,5})\s*(\+?)\s*:(.*)`; written
`%d%s:%s` in id order.

- **Opening:** read with the exe; a missing file is no overrides; a line that doesn't
  parse is a status note naming the file and line, the rest loads.
- **Effect:** PyMS's rule in every Names mode: a replace override is the name; an append
  override adds `' ' + text` to the name the mode gives. Units 228-250 of an expanded
  units.dat stay "Reserved Unit #N". Overrides show everywhere a name shows: the list,
  dropdowns, "Used by" rows.
- **Editing:** PyDAT's **Name Overrides** dialog for the current file, from the list's
  right-click menu ("Override Name", Ctrl+Shift+N, the selected entry filled in): the
  overrides list, ID, Name, Append, Update, Remove, Open... and Save As... (a PyDAT `.txt`;
  Open replaces this file's overrides with the file's). OK applies the dialog's changes as
  one undo step, "Edit name overrides"; Cancel drops them.
- **Document:** the overrides sit in the document beside the .dat files: dirty marks,
  Save and Save as cover them; Save writes only the files whose overrides changed. They are
  not written to `to-repack\` (Repack files never touches them, as with `buttonsets.bin`).
  A tab's ● shows when its .dat file or its overrides are unsaved.

## 7. Errors

- Paste and import apply wholly or not at all, with PyMS's message and the line.
- Export writes a temporary file first; a failure leaves an old `.txt` whole.
- An overrides file that doesn't parse: a status note; the parsed lines load.
- An overrides file the exe can't read: a status note naming it; the exe opens without those
  overrides. An empty file is no overrides.
- A Jump to an id past the target's end: the button is disabled.

## 8. Testing

**Core (Linux):**
- TXT export of random-byte files of all 11 formats (built by the tests' fixture script,
  no Blizzard data, committed with PyMS's exports of them as goldens in
  `fixtures/pydat-txt/`) matches PyMS's text character for character; applying the
  goldens rebuilds the identical files; with `MANIFOLD_ARR_DIR`, the vanilla files survive
  export then apply unchanged; every property has a TXT key; sub-tab copies hold only that
  sub-tab's properties; each refusal in §2.
- `DatRefs`: every row names a real property of its file; each reference finds what it
  should, sound ranges included; the two Check lookups.
- Name overrides: the file format both ways; replace and append in each Names mode;
  reserved units; Save writes only changed files; the undo step.
- Undo and dirty marks for paste, import and overrides.

**Windows (the user):**
1. Copy a unit, paste it into PyDAT's units tab; copy one in PyDAT, paste it here.
2. Copy Sub-Tab on Basic, paste onto another unit: only Basic's fields change. Undo.
3. Export units.dat to TXT, import it into PyDAT; export from PyDAT, import here. Undo.
4. Jump from a unit's ground weapon to the weapon, from its graphics to the flingy; in the
   weapon's "Used by", double-click the unit to come back.
5. Images: Check beside GRP File and IScript ID.
6. Add an override (replace and append), Save, reopen: it shows in the list and dropdowns.
7. An import with a mistake: the message names the line and nothing changes.
8. Remove every override of a file, Save, reopen: none show and nothing breaks (the exe
   holds an empty overrides file).
9. With the Name Overrides dialog open, Ctrl+Z and Ctrl+Y: they act on the dialog's text
   boxes only, not on the document behind it.

## 9. Out of this part

Previews and sound playback (part 3); adding entries (part 4); a Back button after a
jump (a part 5 idea); PyMS's JSON export (not offered by PyDAT's window).
