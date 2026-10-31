# Manifold Editor: the Requirements page, FireGraft's way

Status: [PROPOSED] Design 2026-10-31. Improvements the user asked for after the first
Requirements page (`2026-10-30-dat-requirements-design.md`, merged 2026-10-31, not yet run on
Windows), to work as FireGraft's Dat Requirements editor does: the tables as tabs, FireGraft's
tree, one row per word edited in place, and copy and paste of records. The user's answers: the
tree comes from FireGraft's own lists; rows switch between Opcode and Parameter with a guard
that keeps each opcode's parameter in place.

The file, the plugin and the model's words are unchanged: this is the page, and the Core
logic it needs.

## 1. Goal

A FireGraft user edits requirements here as fast as in FireGraft, with the same tree and the
same rows, and fewer clicks: a row is changed by its own dropdown. Success: §6's Windows list
passes.

## 2. The tabs and the tree

**Tabs.** The table selector becomes five tabs: Units, Upgrades, Tech research, Tech use,
Orders. A tab reads "Units ●" while its table differs from what the next Save writes over
(`ButtonSetDocument.RequirementTableDirty(ReqTable)`); the page's own tab keeps its ●.

**The tree.** The left side shows FireGraft's tree, as in FireGraft: Terran > Ground Units >
[0] Marine.

- **Groups** come from FireGraft's lists, shipped in the editor's `Data\firegraft\`
  (`unitlist.txt`, `upgradelist.txt`, `techlist.txt`, `orderlist.txt`, copied from FireGraft
  0.93's `list\` folder into `fixtures/firegraft-lists/` on 2026-10-31). A line `<id>Entry=Group,Subgroup,
  ...,Name` puts the id under that path; other lines (`<id>Set=`, ...) are ignored. A list
  whose lines have no groups (orders) gives a flat list.
- **Leaves** are named by the editor ("[0] Marine": stat_txt and the name overrides, as on the
  Data page), not by FireGraft's list, and carry the page's marks: "(off)", "(none)", the
  vanilla • and the unsaved colour.
- An id the list doesn't hold goes under "Unused", FireGraft's group for such ids. Ids past
  the table's count (the lists name up to 255: "Extended Order 189", units 228-249 as
  Buttons) are not shown.
- Groups nest as deep as a line goes (Terran > Buildings > Addons > Comsat Station), in the
  order they first appear in the file; a group's leaves are in id order. A group of its own
  with a leaf (All Races > Map Revealer) works the same.
- The lists' group is FireGraft's, whatever the mod made of an id: unit 91 (the Viking in
  Manifold) stays under Unused, named by the editor.
- **Tree / ID**: two buttons under the list switch between the tree and a flat list by id,
  as FireGraft's; the choice is kept in the settings.
- **Find** filters either view; in the tree, a match shows with its groups opened.
- A list file that is missing or unreadable gives that table the ID view, with a status
  line naming the file.

## 3. The rows

**One row per word**, as FireGraft shows a record:

- **Opcode row**: an Opcode/Parameter switch and a dropdown of the opcodes the table's parser
  reads (requirements spec §3), e.g. "Current unit is".
- **Its parameter row**, right under an opcode that takes a unit or a tech (units FF02, FF03,
  FF04, FF25; other tables FF02, FF03): indented, a unit or tech dropdown, e.g. "Terran
  Barracks". It has no switch: the game reads that word as the opcode's parameter whatever it
  is.
- **Free parameter row** (a unit on its own: "Must have"): the switch and a unit dropdown.
- **Level headers** of an upgrade ("Level 1"-"Level 3"), with their rows indented under them.
- **"==End of List=="** at the end of the record and of each level: its dropdown is empty;
  choosing a value appends a row there (an opcode, or with the switch on Parameter, a unit).

**Editing in place.** Each dropdown change is one undo step ("Edit Marine's requirements").
There is no Change button and no line dialog.

- Opcode → Parameter: an opcode with a parameter becomes that parameter on its own ("Current
  unit is Barracks" → "Barracks", Must have Barracks); one without becomes "Must have" unit 0.
- Parameter → Opcode (a free row): it becomes "Current unit is" that unit.
- Choosing an opcode that takes a parameter adds its parameter row (unit 0, or tech 0);
  choosing one that takes none removes it. A tech parameter on units changes nothing else.
- The dropdowns list "[id] name"; typing jumps to a match (the list's text search).

**Row actions** (buttons, and the rows' right-click menu):

- **Insert**: an empty row above the selection, shown until a value is chosen; nothing is
  written until then (leaving it empty removes it).
- **Delete**, **Move up**, **Move down**: on an opcode, its parameter row goes with it; on a
  parameter row, they act on its opcode. Moves stay within the record's own rows or the level.
- **Add levels** (an upgrade without levels): Levels 1-3; **Delete** on a level header removes
  all three with their rows (they come as a set, requirements spec §6).
- Used, Add record, Delete record and Revert to vanilla stay.

The line dialog (`ReqLineDialog`) is removed.

## 4. Copy and paste

- **Copy record** (Ctrl+Shift+C, and the tree's right-click menu) keeps the selected record:
  its rows, levels and Used flag. The copy is the editor's own (not the Windows clipboard) and
  lasts across tabs and pages.
- **Paste record** (Ctrl+Shift+P, and the menu) puts it on the selected id, replacing its
  record or adding one: one undo step, "Paste onto Firebat's requirements".
- It pastes into any table whose parser reads every opcode in the record; levels only into
  Upgrades. A refusal says why: "Orders can't read FF04 (Has add-on)", "Only upgrades have
  levels".
- The keys work only while the Requirements page shows (the Data page keeps them otherwise).

## 5. Units

- **Core**: `FireGraftList` (parses a list: id → group path), `ReqTree` (the tree for a table
  from a list and names, with Find), `RequirementTableDirty`, the row edits on words
  (`RequirementEdits`: the per-word rows, switching kinds, setting an opcode or a parameter,
  insert and append, delete and move with the parameter), and `ReqPaste` (can a record go into
  a table, and why not).
- **App**: `RequirementsPage` rebuilt (tabs, `TreeView` with the ID view, the rows as
  inline `ComboBox`es, the menus and keys); `ReqLineDialog` removed; `EditorSettings` keeps
  the Tree/ID choice; the csproj ships `Data\firegraft\*.txt`.

## 6. Testing

**Core (Linux):**
- `FireGraftList`: groups from `unitlist.txt` (Marine under Terran > Ground Units), the flat
  orders list, `Set=` lines ignored, an id it lacks under Unused.
- `ReqTree`: the build, and Find opening only the matching groups.
- `RequirementTableDirty`: per table, after an edit, an undo, and Save.
- Rows: the rows of Marine (opcode, parameter, opcode, opcode) and Ghost (two free
  parameters); switching kinds both ways; an opcode change adding and removing its parameter
  row; delete and move taking the parameter along; append at End of List and in a level.
- Paste: within Units; Tech research → Tech use; refused into Orders with FF04 named; levels
  refused outside Upgrades.

**Windows (the user):**
1. The tabs, and a tab's ● after an edit, gone after Save.
2. Tree and ID views; Find in both; the choice kept after reopening.
3. Change a row's dropdown in place; Undo puts it back.
4. Switch "Current unit is Barracks" to Parameter and back.
5. Insert (then leave empty), Delete and Move on an opcode with its parameter.
6. Add levels on an upgrade without them; delete a level header.
7. Copy the Marine, paste onto the Firebat; a paste into Orders refused with its reason.

## 7. Out of this part

FireGraft's "Alphabetical" and "Lock" view buttons, a Windows-clipboard format for records,
and the deferred minors of the first part (listed in its final review) unless they fall out of
the rebuild.
