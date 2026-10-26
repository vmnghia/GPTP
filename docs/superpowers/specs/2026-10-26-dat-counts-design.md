# Manifold Editor: the .dat files, part 4 (entry counts, and the unsaved rows)

Status: Built 2026-10-26 (plan `docs/superpowers/plans/2026-10-26-dat-counts.md`): the rules
are tested on Linux, the window compiles; not yet run on Windows. Design 2026-10-26. Part 4 of the PyDAT port planned in
`2026-10-24-dat-editor-design.md` (parts 1-3, the fields, links and previews, passed on
Windows 2026-10-25 and 2026-10-26): PyDAT's **Add Entry** and **Set Entry Count**, and a mark
the user asked to fold in: entries changed since the last save are drawn in an accent
colour. The user's answers: every file whose format has an expanded layout can grow, as in
PyDAT (not only the three the plugin extends); the mark is coloured row text; Core does the
rules (approach A).

The plugin's extension (`GPTP/DatExtend/`) is on master since 2026-10-26. It loads an
expanded **images.dat, sprites.dat or flingy.dat**; an expanded file of any other table is
refused with the game's invalid-file warning (`2026-10-24-dat-editor-design.md` §2).

The reference is PyMS at `bfc5d3a` (MIT): `PyMS/FileFormats/DAT/AbstractDAT.py`
(`expanded_count`, `can_expand`, `expand_entries`), each `*DAT.py`'s entry `expand()`,
`PyMS/PyDAT/DATTab.py` (`add_entry`, `set_entry_count`), `EntryCountDialog.py`, and the
list menu in `PyDAT.py`.

## 1. Goal

A mod can have more images, sprites and flingy than vanilla's, made in the editor as in
PyDAT, and the list shows at a glance what the next Save writes. Success: §6's Windows list
passes, including a game that loads a grown images.dat and draws a new entry.

## 2. The rules (Core)

**`DatFormat.ExpandedCount(DatFile file, int wanted)`**, PyMS's `expanded_count`:

- a file not yet expanded goes up to the format's minimum (units.dat: 250);
- the count rounds up to the format's step (units.dat: 12), so units.dat's first growth
  gives 252;
- a result above the format's maximum is refused.

Maximums (the formats' `ExpandedMax`): units 912, weapons 255, techdata 255, orders 255,
upgrades 256, portdata 65535, flingy, sprites, images and sfxdata 65536. mapdata.dat has no
expanded layout and can't grow.

**`DatFile.WithCount(int count)`** returns the grown file, or refuses a count below the current
one, or past the maximum after rounding:

- **First growth** rewrites a vanilla-layout file in the expanded layout. Every property
  then covers every entry; a value it didn't cover before is 0, as PyMS's `expand()` sets it
  (sprites' health bar and selection circle under 130; units' infestation, add-on position
  and sounds outside their ranges). A property with a wider expanded type (units'
  `graphics`, byte to short) keeps its values.
- **New entries** are all zeros, as PyMS's new entries are.
- An expanded file only gets more zero entries.

**Document** (`ButtonSetDocument`, in its one undo history):

- `AddEntry(DatId file)` and `SetEntryCount(DatId file, int count)`, one step each: "Add entry
  to images.dat", "Set images.dat to 1200 entries". After either the page shows the file's new
  last entry, as PyDAT does. Undo puts back the whole file as it was, layout included, and
  shows the entry selected before; redo grows it again.
- `EntryChangedSinceSave(DatId file, int entry)`: the entry differs from the saved file's, or
  the saved file doesn't have it (an entry past the saved count, or a file the exe didn't
  hold). After Save nothing is marked; an undo back to the saved values clears the mark.

**What follows the count with no change of its own:** names (a new entry is PyDAT's
"Expanded Image #999"; units 228-250 of an expanded units.dat are "Reserved Unit #N"), the
reference dropdowns and Jump buttons, "Used by", the previews, the TXT import's id limit,
and the check line that flags an expanded file the plugin refuses.

## 3. The window

**Commands**, on the entry list's right-click menu with PyDAT's names:

- **Add Entry (DatExtend)**, Ctrl+Shift+A;
- **Set Entry Count (DatExtend)...**, Ctrl+Shift+S (the editor's Save as has no key).

Both are disabled for mapdata.dat, for a file found nowhere, and (Add Entry) for a file at its
maximum. They work only while the Data page shows.

**The first growth of a file asks first** (PyDAT's question, with ours for the plugin):

- images, sprites, flingy: "Expanded .dat files need the plugin's DatExtend. Expand
  images.dat?"
- any other: "The plugin can't load an expanded units.dat: the game will refuse it. Expand it
  anyway?"

Cancel changes nothing. A file already expanded isn't asked about again.

**Set Entry Count** is PyDAT's dialog: "How many entries for images.dat?", a number box, the
file's constraints (minimum, maximum, step, "Some entries are reserved" for units) and
"Resulting entry count: N", updated as the box changes. OK below the current count is
refused with PyDAT's message, "The entry count can't be set to less than the current entries
(999).", and the box goes back to the current count; above the maximum is refused naming the
maximum. OK applies the count as one undo step; Cancel changes nothing.

**The unsaved rows.** An entry changed since the last save is drawn in the accent colour; the
vanilla • stays, so a row can show both. The colour follows edits, undo and redo, and clears
on Save. The rows stay plain text: each row's colour is set as the list shows it (the list's
container event), so there is no .xaml and the list still updates only the rows that change.

**After a count change** the list is made anew with the new rows and selects the new last
entry; the tab's ● shows; the dropdowns that list the file grow; for a file the plugin
refuses, the check line under the tabs appears.

## 4. Errors

- A count past the maximum or below the current one is refused in the dialog; nothing changes.
- Add Entry at the maximum is disabled; reached by key anyway, it changes nothing.
- A file found nowhere: both commands are disabled.
- Save writes a grown file like any other, into the exe and `to-repack\arr\`; for a file the
  plugin refuses, the first Save that writes it asks to confirm, as today.

## 5. Out of this part

Removing entries (PyDAT can't either; undo is the way back); growing mapdata.dat; changes to
the plugin's extension (it stays at images, sprites and flingy); part 5's improvements.

## 6. Testing

**Core (Linux):**
- `ExpandedCount`: units 228 + 1 → 252 (250 rounded to 12), 251 → 252, past 912 refused; weapons 130 + 1 → 131,
  256 refused; images 999 + 1 → 1000, 65537 refused; mapdata can't grow.
- `WithCount` on each format's vanilla file: the expanded layout; old values kept (units'
  graphics included); uncovered values 0 (sprites' health bar on 5, units' infestation on 0);
  new entries 0; it saves and parses back expanded at the new count. Below the current count
  is refused.
- With `MANIFOLD_ARR_DIR`: every vanilla file that can grow grows by one and round-trips.
- Document: Add and Set are one undo step each with their labels; undo gives back the vanilla
  bytes; redo grows again; the file is dirty until Save.
- `EntryChangedSinceSave`: an edited entry, an untouched one, a new one past the saved count;
  none after Save; none after an undo back to the saved values.
- Names: a grown units.dat names 228-250 "Reserved Unit #N"; new ids "Expanded <Type> #N".

**Windows (the user):**
1. images.dat: Add Entry, confirm; entry 999 selected and coloured. Set Entry Count 1200: the
   dialog shows the result; fewer is refused. Undo twice: 999 entries, nothing coloured.
2. units.dat: Set Entry Count 251 → 252, with the warning that the game refuses it; the check
   line appears.
3. Edit two entries: both coloured. Undo one: its colour goes. Save: none coloured; the •
   marks stay.
4. Save a grown images.dat, reopen: the count holds, the new entries keep their values.
5. In game, with images.dat grown and a sprite using a new image: the game loads and draws it
   (the first recorded in-game run of the plugin's extension).
