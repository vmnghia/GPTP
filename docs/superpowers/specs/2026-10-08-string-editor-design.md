# Manifold Editor part 2: stat_txt.tbl strings

Status: design 2026-10-08, from the user's three answers that day (edit from the button
panel and from a Strings view; save into the exe and mirror to `to-repack\`; add strings
and copy shared ones). Not yet approved or planned. Builds on part 1,
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

- `rez\stat_txt.tbl`: `u16 count`, `count` × `u16 offset`, then the string bytes. Ids
  count from 1 (id N is offset N−1). Offsets are 16-bit, so every string must **start**
  below 65,536: the table's size budget. The mod's copy is in `SCManifold.exe`'s MPQ and
  in `SCManifold\to-repack\rez\`, from where the user adds it with PyMPQ.
- Byte values are kept as they are (read and written as Latin-1, one char per byte).
- **A button's enabled string** (button field 0x10) is: the hotkey, one "kind" byte, then
  the text. Move is `m`, `<0>`, `<3>M<1>ove`. Part 1 found the hotkey and saw the `<0>`;
  the kind byte's other values and what the game does with them (which costs the tooltip
  shows) are **[VERIFY]** (§8, step 0). The **disabled string** (0x12) is plain text
  (the screenshot of 2026-10-07: "Stim Packs:", a line break, "Research at Academy").
- Bytes 1-31 are control codes: colours (`<1>` default, `<3>` yellow for the hotkey
  letter, ...) and layout. Which byte breaks a line is **[VERIFY]** (step 0).
- Some entries hold several NUL-separated parts (unit names are thought to carry
  StarEdit's group names after a NUL) **[VERIFY]**. Part 1's reader keeps only the first
  part except for the hotkey-NUL case, so its text is not safe to write back.
- The editor knows two kinds of reference to a string id: button fields (all 250 sets)
  and unit names (id = unit id + 1, units 0-227). `.dat` labels (weapons, upgrades,
  techs, ...) and ids hardcoded in the exe and GPTP (777 "Damage:", 1301 "per rocket")
  also point into the table; they are not tracked in this part.
- Whether the game reads ids past vanilla's count without trouble is **[VERIFY]** (§8,
  step 4). The table carries its own count, so it is expected to.

## 3. The table model

Each entry's bytes are its **segment**: from its offset up to the next distinct offset
(the last one to the end of the file). Segments are what the editor keeps and writes:

- Unedited entries are written back byte for byte, extra NUL parts and all. Entries that
  shared an offset still share one.
- An edited entry gets new bytes: its text, one NUL. Hotkey and kind stay in front for an
  enabled string.
- An entry whose text runs on into the next one (its segment has no NUL) is
  **overlapping**. Before the one it runs into changes, the overlapping entry gets its full
  text as a segment of its own, so it never changes without being edited.
- A new string gets the next id, at the end.
- Ids are never removed or renumbered: other files refer to them.

Writing lays the segments out in id order and checks the 65,536 budget. The panel and the
Strings view show the bytes left. An edit that doesn't fit is refused with that message
and changes nothing.

**Self-check on save**: the written table is parsed again. Every unedited entry must read
back the same bytes from its offset to its first NUL, and through the end of its old
segment when that held more. Otherwise nothing is saved, and the message names the first
id that differs.

## 4. Editing text

Text shows as one line, with control codes and `<` as `<N>` (decimal), as PyMS's TBL
editor writes them. For example Move is `<3>M<1>ove`. Typing `<N>` with N from 1 to 255
gives that byte. A `<` that doesn't start such a code stays literal and is saved as
`<60>`. Below the field a preview draws the text with its colours and line breaks, as far
as they are known.

For an enabled string the panel has three fields: **hotkey** (one key; Esc allowed, as
vanilla's Cancel uses), **kind** (a dropdown of the values found in the table, labelled
once step 0 settles them, raw numbers until then), and **text**. A disabled string, or a
string that no button uses, has text only.

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
0. **Facts first.** A test reads the mod's `stat_txt.tbl` and writes
   `stat_txt-report.txt` next to the test output. The report has:
   - the count and size;
   - the kind byte's values, each with the buttons that use it;
   - which control bytes appear in disabled strings;
   - the multi-NUL, shared and overlapping entries.

   The user commits the report, and the `[VERIFY]` facts in §2 are settled from it before
   §4's labels are written.
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
- A full replica of the game's tooltip drawing: the preview is a guide.
