# Manifold Editor: the Dat Requirements

Status: [BUILT] 2026-10-31 (plan `docs/superpowers/plans/2026-10-31-dat-requirements.md`):
Core tested on Linux, the window compiles, the plugin's loader edited and its host test run
under g++ (not yet built with MSVC); §8's Windows and in-game list not yet run. Design 2026-10-30. A port of FireGraft's Dat Requirements editor: the
five requirement tables (units, upgrades, tech research, tech use, orders) edited on their
own page, saved as `Manifold\requirements.bin` in the mod exe's MPQ, and applied by the
plugin's requirements loader (`docs/superpowers/specs/2026-10-29-fg-requirements-design.md`,
tested in game 2026-10-30), which reads that file before FireGraft's project. The user's
answers: an own file, like the button sets, with FireGraft's requirements imported on the
first open; an own page.

Sources: the FireGraft project in the exe (`fixtures/SCManifold.fgp`), and two read-only
probes of StarCraft.exe 1.16.1 run on Windows 2026-10-30 (tables, parsers, the 39 code
sites; scripts kept outside the repo). The probes agree with the loader's spec on every
address.

## 1. Goal

The requirements are edited in the Manifold Editor instead of FireGraft, and reach the game
through the plugin. Success: §8's Windows list passes, including a game where an edited
requirement takes effect and the load line names `Manifold\requirements.bin`.

## 2. How the game holds requirements

Five tables of u16 words in StarCraft.exe's data, back to back with no room between them:

| Table | Address | Offsets (u16 per id) | Ids | Parser |
|---|---|---|---|---|
| Units | 0x514178 | 0x660A70 | 228 | `canMakeUnit` (0x46E1C0), its own copy |
| Upgrades | 0x5145C0 | 0x6558C0 | 61 | 0x46D610 |
| Tech research | 0x514908 | 0x656198 | 44 | 0x46D610 |
| Tech use | 0x514A48 | 0x6562F8 | 44 | 0x46D610 |
| Orders | 0x514CF8 | 0x665580 | 189 | 0x46D610 |

- A table is word 0, then records, then 0xFFFF. A record is its id, then opcode words up
  to 0xFFFF. An upgrade's record has level blocks, each opened by FF1F, FF20 or FF21 and
  closed by its own 0xFFFF.
- The .dat files hold 0xFFFF in every requirements field ("not known yet"). On first use
  the game walks the table (0x46E100 for units, 0x46D450-0x46D5A0 for the rest). It stores
  each id's first-opcode word index, counted from the table's address. An id the table
  doesn't list gets 0, "never allowed".
- The plugin's loader points the exe's 39 references at its own copies and resets the
  offsets to 0xFFFF, so a table may grow without limit.

FireGraft's project keeps each table as a section (UntR, UpgR, TecR, TecU, OrdR): a u16
count, then per record u8 id, u8 word count, the words (the last 0xFFFF), and a u8 "Used"
flag. A record that is off is left out of the game's table. The current project matches
vanilla except tech use 24 and 34 (Healing and Restoration), where SCVs are allowed too.

## 3. The model (Core)

**`Requirements`**: five `RequirementTable`s, each an ordered list of
`RequirementRecord(int Id, bool Used, IReadOnlyList<RequirementLine> Lines, IReadOnlyList<Level> Levels)`.

- A **line** is an opcode with its parameter, if any, or a bare unit id ("Must have").
  The parameter is a unit for FF02, FF03 and FF04, and a tech for FF25. A word the model
  doesn't know is an unknown line, kept as it is.
- An upgrade's **levels** (1-3, each optional) are their own line lists. FF1F, FF20 and
  FF21 are not lines.
- **Words ⇄ lines.** Parsing reads lines up to the record's 0xFFFF, then any level blocks.
  Writing puts back the words with every 0xFFFF in its place. Every vanilla record and
  every record of the current project round-trips word for word.
- The order of records is kept as imported. A new record goes at the end. The game only
  uses the order for its offsets.
- **Vanilla** comes from StarCraft.exe at the five addresses (`PeImage`, as `VanillaSets`
  reads the button sets). It gives the vanilla • and Revert to vanilla. A table that can't
  be read there has no vanilla marks; editing still works.

**The opcodes** (names from the parsers; [VERIFY] = to be checked against FireGraft's names):

| Opcode | Units (`canMakeUnit`) | Upgrades, tech, orders (0x46D610) |
|---|---|---|
| FF01 | or | or |
| FF02 ‹unit› | Current unit is | Current unit is |
| FF03 ‹unit› | Must have, counting ones being made [VERIFY] | same |
| bare id | Must have ‹unit› | same |
| FF04 ‹unit› | Has add-on | — |
| FF05 | Is not lifted off | Is not lifted off |
| FF06 | — | Is lifted off |
| FF07 | Is not training, researching or upgrading | same |
| FF08 | Is not constructing an add-on [VERIFY] | same [VERIFY] |
| FF09 | Is not researching | same |
| FF0A | Is not upgrading | same |
| FF0B | Is not constructing a building | — |
| FF0C | Has no add-on | — |
| FF0D | — | Has no Nydus exit |
| FF0E | Can hold another Interceptor or Scarab | — |
| FF0F | — | Has researched this tech |
| FF10 | Silo has no nuke | — |
| FF11 | Is not burrowed | Is not burrowed [VERIFY] |
| FF12 | — | Responds to Battle Orders |
| FF13 | — | Can set a rally point |
| FF14 | — | Can move |
| FF15 | — | Can attack [VERIFY] |
| FF16 | — | Is a worker |
| FF17 | — | Can lift off |
| FF18 | — | Is a transport |
| FF19 | — | Is a powerup |
| FF1A | — | Is a subunit |
| FF1B | — | Has spider mines |
| FF1C | — | Is a hero |
| FF1D | — | Can hold position |
| FF1E | — | Allow on hallucinations |
| FF1F-FF21 | — | Level 1-3 (upgrades only) |
| FF22 | Shown but disabled | same |
| FF23 | Never available | same |
| FF24 | Brood War only | same |
| FF25 ‹tech› | Has researched | — |
| FF26 | Is burrowed | same |

A "—" opcode is not offered for that table: its parser reads it as a unit id. One already in
a record shows as an unknown line.

## 4. The file

**`Manifold\requirements.bin`** in the exe's MPQ uses the layout of a FireGraft project:
"FgPa", u32 version, a NUL-terminated game version, then sections (`char[4]` tag, u32
length, data). It holds exactly the five sections, in FireGraft's record format. So the
plugin's parser (`fgreq::findSection`, `fgreq::buildTable`) reads it unchanged, and records
that are off round-trip. The header is always version 7 and "1.16.1", the
project's values (the plugin reads neither).

- Limits of the format: ids up to 255 and up to 255 words per record. The five vanilla
  tables fit (units 228 ids, orders 189). The editor never writes 0xFF00.
- **Opening:** with the file in the exe, the editor reads it. Without it, the editor imports
  the five sections from the FireGraft project (`EditorSession.FindFgp`). FireGraft's
  blank items (0xFF00) are dropped, with a status line naming the table. The import counts
  as unsaved until Save. With neither, the tables start as vanilla.
- **Saving:** the editor's one Save writes the file into the exe when the requirements
  changed, in the same write as the button sets, strings and .dat files. Like
  `buttonsets.bin`, it has no to-repack copy; Repack files leaves it in place.

## 5. The plugin

`reqloader::applyOnce` (`hooks/interface/requirements_loader.cpp`) reads
`Manifold\requirements.bin` first:

- **Present and good:** its five tables are applied, as the .fgp's are today. The line
  reads "Requirements: N tables from Manifold\requirements.bin".
- **Present and bad:** one yellow line with the reason, and every table stays vanilla. It
  does not fall back to FireGraft's copy, which would hide the problem.
- **Missing:** today's path, `Firegraft\datalist.txt` and then its .fgp.

Everything else stays: the 39 sites and their check, the offset reset, the blank-item line.
The host test `tests/fg_requirements_test.cpp` gains a case for a file in this layout.

## 6. The Requirements page (window)

A fourth page after Data, under the editor's one Save and undo history. Its tab shows ●
while the requirements are unsaved.

**Left side.**
- A selector: Units, Upgrades, Tech research, Tech use, Orders.
- A find box.
- The entry list: every id of that type (228, 61, 44, 44, 189), named as on the Data page
  (stat_txt, name overrides). A row shows "(off)" when its record's Used is off, and is
  greyed "(none)" when it has no record.
- The vanilla • and the unsaved colour work as on the Data page.

**Right side: the record.**
- **Used** checkbox. Tooltip: "Off: the game leaves this record out and allows it nowhere".
- **Lines**, one per row, in words: "Current unit is Terran Barracks", "Must have Terran
  Academy", "or", "Has researched Lurker Aspect", "Unknown 0xFF15". An upgrade's levels
  show as headers ("Level 1", "Level 2", "Level 3") with their lines indented. The 0xFFFF
  terminators are never shown.
- **Add, Insert, Change, Remove, Move up, Move down.** Add, Insert and Change open a dialog:
  the opcode (only those §3 offers for the table), then a searchable unit or tech list when
  it takes one. For an upgrade with no levels, the dialog also offers "Levels 1-3"; Remove on
  a level header removes all three with their lines. Levels come as a set, as every vanilla
  upgrade has them: the game's parser looks past FF1F for the player's level's marker, so a
  missing one would send it into the next record (final review 2026-10-31). Moving a line
  can't cross a level header.
- **Add record** (for a "(none)" id), **Delete record**, and **Revert to vanilla** (greyed
  when the record is vanilla, or vanilla isn't known).

**Undo.** One step per change, labelled like "Edit Marine's requirements", "Turn off
Marine's requirements", "Delete Marine's requirements record". Undo and redo select the
entry they change.

## 7. Errors

- **A bad `requirements.bin` in the exe:** the status line says why. The page shows, read-only,
  what an exe without the file opens as (the .fgp's import, else vanilla), which is what
  Replace it gives; Save doesn't write the file until the user chooses "Replace it" on the
  page.
- **StarCraft.exe's tables can't be read:** a status line says so; the exe still opens, with
  no vanilla marks and no Revert to vanilla, and empty tables where vanilla would fill in.
- **A bad FireGraft project on import:** the status line says why, and the tables start as
  vanilla.
- **Save** checks every record against the plugin's rules (ids, word counts, 0xFFFF in
  place). A failure names the record, and nothing is written. The editor can't build such
  a record, so this guards against bugs.
- **In game:** a bad file gives one yellow line, and every table stays vanilla (§5).

## 8. Testing

**Core (Linux):**
- The five sections of `fixtures/SCManifold.fgp`: parsed into records and written back
  byte for byte.
- Words ⇄ lines for every vanilla record (a fixture taken from the probe's dump of
  StarCraft.exe) and every project record: upgrade levels, FF01, parameters, unknown words.
- The import: blanks dropped and counted; records that are off kept.
- `requirements.bin`: written and read back; its sections are the bytes the plugin's
  parser reads.
- The document: each edit is one undo step with its label; dirty until Save; the vanilla
  • and the unsaved mark follow edits, undo and Save.
- Opcode offers: units offer FF04 and not FF12; orders offer FF1E and not FF04.

**Plugin (Windows, `tests/verify.ps1`):** the host test's new case for a file in this
layout.

**Windows and in game (the user):**
1. The first open imports the project: tech use 24 and 34 list SCVs. Save, repack, play:
   the line names `Manifold\requirements.bin`, and SCVs can still Heal and Restore.
2. Make the Marine need an Academy: the Barracks can't train one until an Academy stands.
3. Turn a record off: the game no longer offers it. Turn it back on.
4. Edit an upgrade's Level 2: the level block keeps its place.
5. Undo, redo, Revert to vanilla; reopen the editor: the edits hold.

## 9. Out of this part

Copy and paste of records, TXT import and export, editing the Data page's raw requirements
offset fields (they stay as they are), expanded units.dat (ids past 255), FireGraft's
other sections (Exe Edits, the Unit tab).
