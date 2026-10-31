# Requirements Page UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Requirements page works as FireGraft's does: tables as tabs with an unsaved ●,
FireGraft's tree (and an ID view), one row per word edited by its own dropdown with a guarded
Opcode/Parameter switch, and copy and paste of records.

**Architecture:** Core gains the list parser and tree, a per-table dirty check, the per-word
rows and their edits, and the paste rules; the window's `RequirementsPage` is rebuilt on them
and the line dialog goes. The file, the plugin and the record model are unchanged.

**Tech Stack:** C# .NET 8, Manifold.Core (xUnit), WinUI 3 built in code (no .xaml).

**Spec:** `docs/superpowers/specs/2026-10-31-requirements-page-ux-design.md` (and the first
part, `docs/superpowers/specs/2026-10-30-dat-requirements-design.md`, for the opcodes).

## Global Constraints

- Lists: `fixtures/firegraft-lists/{unitlist,upgradelist,techlist,orderlist}.txt`, shipped as
  `Data\firegraft\*.txt`; lines `<id>Entry=Group,...,Name` count, every other line is ignored;
  CRLF and LF both occur.
- The tree: groups in first-appearance order, leaves in id order; ids past the table's count
  (228, 61, 44, 44, 189) not shown; ids the list lacks under "Unused".
- Leaves are titled by the editor ("[0] Marine" plus the page's marks), never by the list.
- Tab text: the table's title plus " ●" while `RequirementTableDirty`.
- Labels: "Edit {name}'s requirements" (every row edit), "Paste onto {name}'s requirements".
- Paste refusals, exact: "{target title} can't read FF04 (Has add-on)" (the word as 4 hex digits,
  the opcode's name in the source table), "Only upgrades have levels".
- Keys: Ctrl+Shift+C copy record, Ctrl+Shift+P paste record, only while the Requirements page shows.
- Checks: `MANIFOLD_ARR_DIR=<scratchpad>/pyms/src/PyMS/MPQ/arr dotnet test tools/ManifoldEditor/Manifold.Core.Tests`
  (520 pass, 3 skip before this plan) and `bash <scratchpad>/appcheck.sh` (0 errors).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01771DQsv5DDEKm1LjYbN2zB`.

## Review Focus

- A word the editor doesn't know (FF15 in units) on an opcode row: shown as "Unknown 0xFF15",
  kept by edits elsewhere in the record. Test in Task 2.
- Switching an opcode with a tech parameter (units FF25) to Parameter: "Must have" unit 0, not
  the tech's id read as a unit. Test in Task 2.
- Pasting a record onto the id it was copied from (or one already equal): no undo step, no
  refusal. Test in Task 3.
- Find text that matches only a group's name: the tree shows no leaf (Find matches leaves).
  Test in Task 1.
- A list with LF endings, a blank line and a trailing space in a name parses as the CRLF ones
  do. Test in Task 1.

---

### Task 1: FireGraft's lists and the tree

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/FireGraftList.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/ReqTree.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqTreeTests.cs`

**Interfaces:**
- Produces:
  - `static class FireGraftList` (namespace `Manifold.Core.Data`): `IReadOnlyDictionary<int, IReadOnlyList<string>> Parse(string text)`
    — id → its group path (the Entry's comma parts without the last, the name); a flat line gives an empty path.
  - `sealed record ReqTreeNode(string Title, int? Id, IReadOnlyList<ReqTreeNode> Children)` (namespace
    `Manifold.Core.Editor`; a leaf has `Id` and no children; a group has `Id` null).
  - `static IReadOnlyList<ReqTreeNode> ReqTree.Build(int idCount, IReadOnlyDictionary<int, IReadOnlyList<string>>? groups, Func<int, string> leafTitle, string query)`
    — top-level nodes; `groups` null gives the flat ID view (leaves only, in id order); `query`
    keeps only leaves whose title contains it (any case) or whose id equals it, and drops empty groups.
  - `const string ReqTree.Unused = "Unused"`.

- [ ] **Step 1: Write the failing tests** in `ReqTreeTests.cs` (lists read from `Fixtures.Text("firegraft-lists/unitlist.txt")` etc.):
  - `MarineUnderGroundUnits`: `Parse(unitlist)[0]` is `["Terran", "Ground Units"]`; `[107]` is `["Terran", "Buildings", "Addons"]`; `[101]` is `["All Races"]`.
  - `OrdersAreFlat`: every id of `Parse(orderlist)` has an empty path; 256 ids.
  - `SetLinesIgnored`: `Parse("0Set=X,Y\r\n0Entry=A,B\r\n")[0]` is `["A"]`.
  - `LineEndingsAndBlanks`: `Parse("1Entry=A,B \n\n2Entry=A,C\r\n")` has ids 1 and 2, both `["A"]`.
  - `TreeShape`: `Build(228, Parse(unitlist), id => $"[{id}]", "")`: top level titles start Terran, Zerg, Protoss (first appearance); Terran's first child is "Ground Units" whose leaves start `[0] [1] [2] [3] [5] [7]`; no leaf has id ≥ 228; unit 91 is a leaf under "Unused".
  - `MissingIdsGoToUnused`: with a list holding only `0Entry=Terran,Marine`, `Build(3, ..)` gives Terran (leaf 0) and Unused (leaves 1, 2).
  - `FindKeepsMatchingLeaves`: query "marine" keeps leaves whose title contains it and their groups only; query "Ground Units" (a group's name, no leaf title) gives no node; query "7" keeps leaf 7.
  - `FlatView`: `Build(5, null, t, "")` is five leaves 0-4 in order.

- [ ] **Step 2: Run them to verify they fail.** `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter ReqTreeTests`. Expected: build errors (types missing).

- [ ] **Step 3: Implement** `FireGraftList.Parse` and `ReqTree.Build`. Ids are visited in ascending order; a group is created the first time an id reaches it.

- [ ] **Step 4: Run them to verify they pass.** Expected: all pass.

- [ ] **Step 5: Commit.** Message "feat(editor): FireGraft's lists and the requirements tree".

### Task 2: Per-table dirt, the per-word rows and their edits

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/ButtonSetDocument.cs` (`RequirementTableDirty`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/RequirementEdits.cs` (rows and the new edits; `ReqRow` replaced)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqRowTests.cs`; update `ReqDocumentTests.RowsAndLevelEdits` to the new rows

**Interfaces:**
- Consumes: `ReqRecord`, `ReqLine`, `ReqTables.ParamOf`, `ReqLineAt`, the existing `Insert`, `Remove`, `Move`, `AddLevels`, `RemoveLevels`.
- Produces:
  - `bool ButtonSetDocument.RequirementTableDirty(ReqTable table)`: false without requirements or while a problem stands; true when nothing is saved; else the table differs from the saved one.
  - `enum ReqRowKind { Opcode, Param, MustHave, LevelHeader, End }`;
    `sealed record ReqRow(ReqRowKind Kind, ReqLineAt? At, int Level, ReqLine? Line)` replacing the old `ReqRow`
    (`At` on Opcode, Param and MustHave rows; `Level` 0 for the record's own rows, 1-3 in a level).
  - `IReadOnlyList<ReqRow> RequirementEdits.Rows(ReqRecord r)`: per line an Opcode row followed by a Param row when it has a parameter, or a MustHave row for a bare unit; then End(0); then per level a LevelHeader, its rows, End(level).
  - `ReqRecord SetOpcode(ReqRecord r, ReqTable t, ReqLineAt at, ushort opcode)`: the line's word becomes `opcode`; its parameter is kept when the new opcode takes the same kind (`ParamOf`), set to 0 when it takes another or had none, dropped when it takes none.
  - `ReqRecord SetValue(ReqRecord r, ReqLineAt at, ushort value)`: an opcode line's parameter, or a bare line's unit.
  - `ReqRecord ToParameter(ReqRecord r, ReqTable t, ReqLineAt at)`: an opcode line becomes a bare unit: its parameter when that is a unit, else 0.
  - `ReqRecord ToOpcode(ReqRecord r, ReqLineAt at)`: a bare unit becomes `FF02` with that unit.
  - `ReqLine RequirementEdits.NewLine(ReqTable t, ushort opcodeOrUnit, bool parameter)`: what an End or Insert row's choice makes: a bare unit when `parameter`, else the opcode with parameter 0 when it takes one.

- [ ] **Step 1: Write the failing tests** in `ReqRowTests.cs` (vanilla records from `VanillaRequirements.FromBlock(Fixtures.Bytes("requirement-tables.bin"))`):
  - `MarineRows`: kinds `[Opcode, Param, Opcode, Opcode, End]`; the Param row's line is `(0xFF02, 0x6F)`.
  - `GhostRows`: kinds end `[MustHave, MustHave, End]` with units 0x75 and 0x70.
  - `UpgradeRows`: upgrade 0 kinds `[Opcode, Param, Opcode, Opcode, End, LevelHeader, End, LevelHeader, MustHave, End, LevelHeader, MustHave, End]`, End levels 0, 1, 2, 3.
  - `SetOpcodeKeepsOrDropsParam`: Marine line 0 → `FF03` keeps 0x6F; → `FF05` drops it (`Param` null); line 1 (`FF08`) → `FF02` gets 0.
  - `SetOpcodeTechToUnit`: units line `(0xFF25, 0x20)` → `FF02` gives `(0xFF02, 0)`.
  - `SwitchBothWays`: Marine line 0 `ToParameter` → `(0x6F, null)`; then `ToOpcode` → `(0xFF02, 0x6F)`; units `(0xFF25, 0x20)` `ToParameter` → `(0, null)`.
  - `SetValueOnBoth`: `SetValue` on Marine line 0 gives `(0xFF02, 0x70)`; on Ghost line 4 (bare) gives `(0x71, null)`.
  - `UnknownWordKept`: a units record `[FF15, FF02 006F, FFFF]` (built with the ctor) rows as `[Opcode(FF15), Opcode, Param, End]`; `SetValue` on line 1 leaves line 0 `(0xFF15, null)`.
  - `NewLines`: `NewLine(Units, 0xFF02, false)` is `(0xFF02, 0)`; `NewLine(Units, 0xFF05, false)` is `(0xFF05, null)`; `NewLine(Units, 0x70, true)` is `(0x70, null)`.
  - `TableDirtyFollowsEdits` (document as in `ReqDocumentTests.Open`): all false at open; after an edit of units 0 only Units true; after `MarkSaved` false; after an undo true again.

- [ ] **Step 2: Run them to verify they fail.** `--filter "ReqRowTests|ReqDocumentTests"`. Expected: build errors.

- [ ] **Step 3: Implement** the members under Interfaces; update `RowsAndLevelEdits` to the new kinds (its level assertions stay).

- [ ] **Step 4: Run the whole suite.** Expected: all pass.

- [ ] **Step 5: Commit.** Message "feat(editor): the requirement rows per word, their edits, and each table's unsaved mark".

### Task 3: Copy and paste of records

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/ReqPaste.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorState.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqPasteTests.cs`

**Interfaces:**
- Consumes: `ReqOpcodes.Offered`, `ReqTables.Title`, `EditorState`'s requirement members.
- Produces:
  - `static string? ReqPaste.Refusal(ReqRecord record, ReqTable source, ReqTable target)`: null when it fits; "Only upgrades have levels"; else the first word offered by `source` and not by `target`: `"{Title(target)} can't read {word:X4} ({name})"`.
  - `EditorState`: `void CopyRequirement(ReqTable table, int id)` (no record: nothing copied); `bool HasCopiedRequirement`;
    `string? PasteRequirement(ReqTable table, int id, string name)` — the refusal, "Nothing copied", or null (applied as one step "Paste onto {name}'s requirements", or nothing when equal).

- [ ] **Step 1: Write the failing tests** in `ReqPasteTests.cs` (document as in `ReqDocumentTests.Open`):
  - `PasteWithinUnits`: copy units 0, paste onto 32 (Firebat) → record 32's lines equal Marine's, Used kept, id 32; `UndoLabel` "Paste onto Firebat's requirements"; one undo restores Firebat.
  - `PasteAcrossSharedParser`: copy tech research 0, paste onto tech use 3 → applied.
  - `RefusedIntoOrders`: a units record with `FF04` copied, pasted onto orders → "Orders can't read FF04 (Has add-on)", nothing changed.
  - `LevelsOnlyIntoUpgrades`: copy upgrade 0, paste onto tech research 5 → "Only upgrades have levels".
  - `PasteSameIsNoStep`: copy units 0, paste onto units 0 → null, `CanUndo` false.
  - `NothingCopied`: paste before any copy → "Nothing copied"; copying an id with no record leaves nothing copied.
  - `PasteAddsRecord`: copy units 0, paste onto units 91 (no record) → record 91 exists.

- [ ] **Step 2: Run them to verify they fail.** `--filter ReqPasteTests`. Expected: build errors.

- [ ] **Step 3: Implement** `ReqPaste` and the three `EditorState` members (the copy lives in the state, across tabs and pages).

- [ ] **Step 4: Run the whole suite.** Expected: all pass.

- [ ] **Step 5: Commit.** Message "feat(editor): copy and paste requirement records".

### Task 4: The page rebuilt

**Files:**
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/RequirementsPage.cs` (rebuilt)
- Delete: `tools/ManifoldEditor/ManifoldEditor.App/ReqLineDialog.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/ManifoldEditor.App.csproj` (`<Content Include="..\fixtures\firegraft-lists\*.txt" Link="Data\firegraft\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Services/Workspace.cs` (the lists, onto `OpenedExe`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorSettings.cs` (`bool RequirementsTree { get; init; } = true`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/MainWindow.cs` (the keys)
- Modify: docs — the UX spec's status, `tools/ManifoldEditor/README.md`'s Requirements section

**Interfaces:**
- Consumes: Tasks 1-3; `RequirementEdits.Insert/Remove/Move/AddLevels/RemoveLevels`; `ReqOpcodes.Offered/Describe`.
- Produces: `OpenedExe` gains `IReadOnlyDictionary<ReqTable, IReadOnlyDictionary<int, IReadOnlyList<string>>> ReqLists` (a table whose file is missing or unreadable is absent, with a status line "Data\firegraft\{file}: {reason}; {Title} shows the ID view"); `RequirementsPage.CopyRecord()` and `PasteRecord()` returning bool (handled).

- [ ] **Step 1: Lists.** Workspace reads `Data\firegraft\{unitlist,upgradelist,techlist,orderlist}.txt` with `FireGraftList.Parse` into `ReqLists`; the csproj ships them.

- [ ] **Step 2: Tabs and tree** per spec §2: a `SelectorBar` of the five tables (text with " ●" from `RequirementTableDirty`, refreshed on every Refresh); a `TreeView` built from `ReqTree.Build` (leaf title "[id] name" with "(off)", "(none)", " •", unsaved colour; groups expanded when Find has text) and a `ListView` for the ID view; "Tree" and "ID" toggle buttons under them, the choice in `EditorSettings.RequirementsTree`; a table without a list shows the ID view. A right-click menu on leaves: Copy record, Paste record.

- [ ] **Step 3: Rows** per spec §3: a `ListView` of `Rows(record)`; Opcode and MustHave rows carry a two-item switch `ComboBox` ("Opcode", "Parameter"; switching calls `ToParameter` / `ToOpcode`) and a value `ComboBox` (Opcode: `Offered(table)` without "Must have", plus "Unknown 0xFFxx" when the word is not offered; MustHave and Param: "[id] name" of units, or techs for a units FF25 parameter; `IsTextSearchEnabled`); Param rows are indented with no switch; LevelHeader rows are text; End rows ("==End of List==") have an empty value box whose choice appends `NewLine(...)` at `(level, count)`. Every value change goes through `EditorState.EditRequirement` with the Global label. Insert shows a pending empty row above the selection (UI only, gone on the next refresh unless a value is chosen, then `Insert`). Delete, Move up, Move down act on the selected row's line (`Remove`, `Move`); on a LevelHeader, Delete is `RemoveLevels`. "Add levels" (upgrades without levels) is `AddLevels`. Used, Add record, Delete record, Revert to vanilla stay; Copy record and Paste record buttons too (a refusal shows with the page's `message`).

- [ ] **Step 4: Keys.** In MainWindow, Ctrl+Shift+C and Ctrl+Shift+P call `requirementsPage.CopyRecord()` / `PasteRecord()` while the Requirements page shows, the Data page's handlers otherwise.

- [ ] **Step 5: Delete `ReqLineDialog.cs`; compile check.** `bash <scratchpad>/appcheck.sh` → "App compile: 0 errors"; the Core suite all passes.

- [ ] **Step 6: Docs.** The UX spec's status: [BUILT] on the date, Core tested, window compiles, §6's Windows list not yet run. README's Requirements section: tabs, the tree and ID view, rows in place, copy and paste.

- [ ] **Step 7: Commit and push.** Message "feat(editor): the Requirements page, FireGraft's way".
