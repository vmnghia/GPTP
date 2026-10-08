# Manifold Editor part 2: stat_txt.tbl strings

Status: design 2026-10-08, from the user's three answers that day (edit from the button
panel and from a Strings view; save into the exe and mirror to `to-repack\`; add strings
and copy shared ones); approved 2026-10-08, with the instruction to follow PyMS's PyTBL.
Built 2026-10-08 (plan `docs/superpowers/plans/2026-10-08-string-editor.md`): the table,
history, save, to-repack copy and preview are tested on Linux; the window compiles but has
not run. §8 step 0 done 2026-10-09; the rest not yet tested on Windows or in game. Builds on part 1,
`docs/superpowers/specs/2026-10-05-button-set-editor-design.md`, whose §5 listed editing
`stat_txt.tbl` as out of scope.

## 1. Goal

Change the game's text without leaving the editor: a button's tooltip, its hotkey and its
"requires" text from the button panel, and any other string (unit names, messages) from a
Strings view. Buttons that need their own text get a new string instead of changing one
that other buttons share.

Success: rename Siege Mode's tooltip and change its hotkey from the button panel, give
Tank Mode a new string of its own, save, run the game, and see both, with every other
string unchanged.

## 2. Facts this rests on

PyMS's PyTBL is the reference (`PyMS/FileFormats/TBL.py`, `PyMS/PyTBL/`,
`Docs/pytbl.txt`; MIT, (c) Zach Zahos). Facts marked "PyTBL" come from it, and the counts
from the vanilla `stat_txt.tbl` that PyMS ships (`PyMS/MPQ/rez/`), read on 2026-10-08:
1,547 strings, 40,398 bytes, offsets in id order, none shared, every entry ending in a NUL.

- `rez\stat_txt.tbl`: `u16 count`, `count` × `u16 offset`, then the string bytes. Ids
  count from 1 (id N is offset N−1). Offsets are 16-bit, so every string must **start**
  below 65,536: the table's size budget. The mod's copy is in `SCManifold.exe`'s MPQ and
  in `SCManifold\to-repack\rez\`, from where the user adds it with PyMPQ.
- Byte values are kept as they are (read and written as Latin-1, one char per byte).
- **An entry is the bytes from its offset to the next distinct offset** (the last one to
  the end of the file), NULs included: PyTBL's loader. Every entry ends in a NUL; PyTBL
  adds a missing one when it saves.
- **Hotkey strings** (a button's enabled string, field 0x10; PyTBL): the hotkey (`<27>`
  for Esc), the tooltip type, then the text. The types:

  | Type | Tooltip shows |
  |---|---|
  | `<0>` | the label only, no costs |
  | `<1>` | minerals, gas, supply (units and buildings) |
  | `<2>` | upgrade costs, with "Next Level" |
  | `<3>` | energy (spells) |
  | `<4>` | minerals, gas (technology research) |
  | `<5>` | minerals, gas, no supply (Guardian and Devourer Aspect) |

  Examples: `m<0><3>M<1>ove<0>` (vanilla id 664), `m<1>Train <3>M<1>arine<0>`. The
  **disabled string** (0x12) is plain text, by default one requirement per line indented
  three spaces: `Stim Packs:<10>   Research at Academy<0>`.
- **Unit strings**, the first 228 (id = unit id + 1; PyTBL): `Name<0>Subname<0>StarEdit
  Group<0>`, as `Terran Marine<0>*<0>Ground Units<0>`. 274 vanilla entries hold more than
  one NUL. Part 1's reader keeps only the first part, except in the hotkey case, so its
  text is not safe to write back.
- **Control codes** (PyTBL's reference): `<9>` tab, `<10>` newline, `<18>` right align,
  `<19>` centre; in game `<1>`/`<2>` cyan, `<3>` yellow, `<4>` white, `<5>` grey, `<6>`
  red, `<7>` green, `<8>` and `<14>`-`<27>` player colours, `<28>`-`<31>` more colours,
  `<11>`/`<20>` invisible, `<12>` truncate. After `<5>`, `<11>` or `<20>` the game ignores
  further colour codes (PyMS's `COLOR_OVERPOWER`). The menus map some codes to other colours.
- The editor knows two kinds of reference to a string id: button fields (all 250 sets)
  and unit names (id = unit id + 1, units 0-227). `.dat` labels (weapons, upgrades,
  techs, ...) and ids hardcoded in the exe and GPTP (777 "Damage:", 1301 "per rocket")
  also point into the table; they are not tracked in this part.
- Vanilla uses 40,398 of the 65,536 bytes. The mod's table is larger by its own strings
  (§8, step 0 measures it). **Measured 2026-10-09** (`tools/ManifoldEditor/reports/stat_txt-report.txt`):
  the mod's `to-repack\rez\stat_txt.tbl` is as regular as vanilla's. It has 1,547 strings
  and 40,398 bytes (25,147 left), offsets in id order, none shared, none running on, and
  274 with several NULs. The editor writes it back byte for byte and passes the self-check.
  The 238 strings vanilla's buttons show when enabled all use PyTBL's types 0-5
  (42/91/50/29/24/2).
- Whether the game reads ids past vanilla's count without trouble is **[VERIFY]** (§8,
  step 4). The table carries its own count, so it is expected to.

## 3. The table model

Each entry's bytes are its **segment**: from its offset up to the next distinct offset
(the last one to the end of the file), as PyTBL loads it. Segments are what the editor
keeps and writes:

- Unedited entries are written back byte for byte, extra NUL parts and all. Entries that
  shared an offset still share one.
- An edited entry gets new bytes: its text and a final NUL, which is added when missing, as
  PyTBL does. Hotkey and type stay in front for a hotkey string.
- An entry whose text runs on into the next one (its segment has no NUL) is
  **overlapping**. Before the one it runs into changes, the overlapping entry gets its full
  text as a segment of its own, so it never changes without being edited.
- A new string gets the next id, at the end.
- Ids are never removed or renumbered: other files refer to them.

Writing keeps the unedited segments in their original order, then puts the edited and new
ones after them, shortest first (as built, 2026-10-08). Only a string's *start* must be
below 65,536, so the longest string goes last and may run past it. The panel and the
Strings view show the bytes left before the last string would start too late. An edit
that doesn't fit is refused with that message and changes nothing.

**Self-check on save**: the written table is parsed again. Every unedited entry must read
back the same bytes from its offset to its first NUL, and through the end of its old
segment when that held more. Otherwise nothing is saved, and the message names the first
id that differs.

## 4. Editing text

Text is written as PyTBL writes it (`decompile_string` and `compile_string`):
- bytes 0-31, `#`, `<` and `>` show as `<N>` (decimal); `<N>` with N from 0 to 255 is
  read back as that byte, and anything else is literal;
- the final `<0>` is not shown, and is added on save;
- inner `<0>`s are shown, so a unit's subname and group stay editable;
- a string copied from PyTBL can be pasted unchanged.

Next to the field, **PyTBL's code reference** (§2) lists the codes; clicking one inserts
it. Below the field, a **preview** draws the text in the game's colours, taken from
`game\tfontgam.pcx` through PyMS's `COLOR_CODES_INGAME` map (code to row and column of
8-pixel colour ramps), as PyTBL does; the brightest pixel of each ramp is the colour. Line breaks and alignment are drawn too. For a hotkey
string the preview adds the cost line its type shows, as PyTBL's previewer does (sample
values). The previewer uses a system font, not the game's `font8.fnt`/`font10.fnt`.

For a hotkey string the panel has three fields:
- **hotkey**: one key, with Esc allowed for Cancel;
- **tooltip type**: a dropdown with the six types of §2, and the raw number for any other
  value;
- **text**.

A disabled string, or a string that no button uses, has the text field only. The Strings
view's list shows every entry whole, hotkey and type included, exactly as PyTBL's list
does.

A field edit is one undo step when committed, as in part 1. String steps share the one
history with set edits. Undoing one shows where it was made: the set and button, or the
Strings view's row.

## 5. Button panel

The enabled and disabled string rows each show:
- the string id, with **Pick...**: the Strings view as a picker, filtered to strings used
  as that field elsewhere first;
- the fields of §4;
- **"Also used by N other buttons"**, a list of set and position, when the string is
  shared. The fields are then read-only, with two buttons:
  - **Edit for all**: unlocks the fields for this string;
  - **Make a separate copy**: adds a new string with the same bytes, points this button
    at it, and unlocks the fields. This is one undo step.

Unit names (ids 1-228) used by a button count as shared with that unit's name.
- **New string**, for an empty field (id 0): adds an empty string and points the field at
  it.

The check box keeps flagging duplicate hotkeys on the card, from the edited text, as you
type.

## 6. Strings view

A second page next to the button sets (a tab at the top), on the same history and save.
- **List**: id, the text as in §4, and where it is used ("Siege Tank: 11, 12", "unit name:
  Terran Marine").
- **Search**: text (case-insensitive) or `#id`. Filters: all, used by buttons, unit names,
  edited since opening.
- **Edit pane**: the fields of §4 for the selected string, with the same shared-string
  rule as the panel. Its "Used by" entries jump to that set and button.
- **Add string**; **Revert string** (to the version opened, as for sets).
- **Status**: the bytes left of the 65,536 budget, and the table's source.
- **Go to** an id and **Find** (next and previous, case optional), as in PyTBL.

## 7. Saving and the to-repack copy

**Save** writes everything that changed in one copy-and-swap of the exe (part 1's save):
`Manifold\buttonsets.bin`, and `rez\stat_txt.tbl` when strings changed. Both go in or
neither does. The table is imploded like the button set file, since 1.16.1's Storm has
no zlib.

**The to-repack copy**: so a later PyMPQ add of `to-repack\rez\stat_txt.tbl` doesn't put
old strings back, each save that changes strings also writes the table there.
- The path is kept per exe in the settings, so a test copy such as `SCManifold -
  Copy.exe` never overwrites the real mod's file without being asked.
- The first save asks once: "Also write stat_txt.tbl to `<exe folder>\to-repack\rez\`?",
  with Yes for this exe, Choose folder, and Not for this exe.
- The previous file is kept as `stat_txt.tbl.bak`.
- It is written after the exe swap succeeds. If it fails, the exe is still saved and the
  message says the two now differ.

**On opening**, when the exe has a to-repack path and that file differs from the exe's
`rez\stat_txt.tbl` (for example after an edit in PyMS), a dialog says so and names the
newer file. The user picks which one to edit: the exe's, or to-repack's. Choosing
to-repack's marks the strings as changed, so the next save puts that table into the exe.

## 8. Testing

C# tests (no game files needed; tables built in the test):
- Text: for every entry of a test table, our `<N>` form equals PyTBL's
  `decompile_string`, and reading it back gives the same bytes (cases taken from
  PyTBL's docs).
- Parse then write gives the same bytes, for a table with shared offsets, hotkey-NUL
  entries, multi-NUL entries, an overlapping entry and trailing bytes.
- Editing one entry changes only that entry: every other entry reads back the same, and
  the self-check passes.
- An overlapping entry keeps its text when the entry it runs into is edited.
- The budget: an edit that would push a string start past 65,535 is refused, and the
  table is unchanged.
- `<N>` round trip: every byte 1-255 and a literal `<`.
- Copy-on-edit: the copy has the same bytes, the button points at it, other buttons don't.
  Undo restores both.
- The save writes both files in one swap. A failing to-repack write leaves the exe saved
  and reports the difference.

With `MANIFOLD_SC_DIR` set (skipped otherwise), as part 1's real-file tests are.

On Windows, in order:
0. **The mod's table.** PyTBL and the vanilla table settled the format. A test reads the
   mod's `stat_txt.tbl` and writes `stat_txt-report.txt` next to the test output. The
   report has:
   - the count, size and bytes left;
   - the shared and overlapping entries, and any entry without a final NUL;
   - every hotkey string whose type isn't 0-5;
   - the ids past vanilla's 1,547.

   The user commits the report. It shows whether the mod's table is as regular as
   vanilla's.
1. Open `SCManifold.exe`: the strings show as before, and Strings lists the whole table.
2. Edit Siege Mode's tooltip and hotkey, then give Tank Mode a separate copy and edit it.
   Save, then run the game: both show and both hotkeys work. Other buttons sharing the
   old string are unchanged.
3. `to-repack\rez\stat_txt.tbl` is the same as the exe's. Add `GPTP.qdp` with PyMPQ and
   run again: the edits stay.
4. A string added past vanilla's count shows in game: this is the new-id test.
5. Edit `to-repack\`'s table in PyMS and open the exe in the editor: the dialog of §7
   appears.

## 9. Out of scope

- Following `.dat` and exe references to strings: this part comes with `.dat` editing.
- Removing strings, and compacting the table to win back space.
- Other `.tbl` files (`network.tbl`, map strings).
- A full replica of the game's tooltip drawing (the game's fonts, PyTBL's FNT
  rendering): the preview is a guide.
- Reading and writing PyTBL's `.txt` form of a whole table: PyTBL itself does that.
