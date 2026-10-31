# Dat Requirements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The five requirement tables are edited on a Requirements page of the Manifold
Editor, saved as `Manifold\requirements.bin`, and applied by the plugin, which reads that
file before FireGraft's project.

**Architecture:** Core holds the model (records of lines, upgrade levels), the FireGraft
section codec, the file, the import and StarCraft.exe's vanilla tables, and the document's
undo steps; the window adds one page and one line dialog. The plugin's existing loader
gains one read in front of its .fgp path; the parser is shared because the file has the
.fgp layout.

**Tech Stack:** C# .NET 8 (Manifold.Core, xUnit; WinUI 3 built in code, no .xaml), C++17
MSVC Win32 (the plugin).

**Spec:** `docs/superpowers/specs/2026-10-30-dat-requirements-design.md`

## Global Constraints

- File path in the MPQ: `Manifold\requirements.bin`; layout "FgPa", u32 version, NUL-ended
  game version, sections `char[4] tag, u32 length, data`; exactly the sections UntR, UpgR,
  TecR, TecU, OrdR; version 7 and game version "1.16.1" (the project's).
- FireGraft record format: u16 count; per record u8 id, u8 word count, the words (the last
  0xFFFF), u8 used (1/0). Ids ≤ 255, ≤ 255 words per record. The editor never writes 0xFF00.
- Table addresses and id counts: units 0x514178 / 228, upgrades 0x5145C0 / 61, tech
  research 0x514908 / 44, tech use 0x514A48 / 44, orders 0x514CF8 / 189. The five tables
  occupy 0x514178-0x51521C (4260 bytes), gaps zero.
- Opcode names, parameters and which table offers which: spec §3's table, verbatim.
- Labels: "Edit {name}'s requirements", "Turn on {name}'s requirements", "Turn off
  {name}'s requirements", "Add {name}'s requirements record", "Delete {name}'s requirements
  record", "Revert {name}'s requirements to vanilla".
- Plugin success line for the file: "Requirements: N tables from Manifold\requirements.bin";
  the .fgp path keeps its "FireGraft requirements: ..." lines.
- Checks: `MANIFOLD_ARR_DIR=<scratchpad>/pyms/src/PyMS/MPQ/arr dotnet test tools/ManifoldEditor/Manifold.Core.Tests`
  (488 pass, 2 skip before this plan) and `bash <scratchpad>/appcheck.sh` (0 errors).
  The plugin can't be built here: re-read C++ edits for includes and MSVC syntax.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_01771DQsv5DDEKm1LjYbN2zB`.

## Review Focus

- An upgrade level with no lines (vanilla upgrade 0's Level 1 is `FF1F FFFF`): it keeps its
  header and round-trips. Test in Task 1.
- A record with no lines (`FFFF` only), as Add record makes it: written, read back, shown.
  Tests in Tasks 1 and 2.
- An id or a record past the format's limits (id 256, 256 words): Write refuses with a
  reason, and Save writes nothing. Tests in Tasks 2 and 3.
- Undo across a Save: the unsaved mark goes after Save and comes back on undo past it.
  Test in Task 3.
- A bad `requirements.bin`: edits refused until Replace it; then Save writes a good file.
  Test in Task 3.

---

### Task 1: The model, the opcodes and words ⇄ lines

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/ReqTables.cs` (ReqTable, ReqTables)
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/ReqRecord.cs` (ReqLine, ReqLevel, ReqRecord)
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/Requirements.cs` (RequirementTable, Requirements)
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/ReqOpcodes.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqModelTests.cs`

**Interfaces:**
- Produces (namespace `Manifold.Core.Data`):
  - `enum ReqTable { Units, Upgrades, TechResearch, TechUse, Orders }`
  - `static class ReqTables`: `IReadOnlyList<ReqTable> All`; `string SectionTag(ReqTable)`
    ("UntR", "UpgR", "TecR", "TecU", "OrdR"); `int IdCount(ReqTable)`; `DatId NamesFrom(ReqTable)`
    (Units, Upgrades, Techdata, Techdata, Orders); `string Title(ReqTable)` ("Units",
    "Upgrades", "Tech research", "Tech use", "Orders"); `uint Address(ReqTable)`.
  - `enum ReqParam { None, Unit, Tech }`; `static ReqParam ReqTables.ParamOf(ReqTable, ushort word)`:
    units FF02, FF03, FF04 → Unit, FF25 → Tech; other tables FF02, FF03 → Unit; else None.
  - `readonly record struct ReqLine(ushort Word, ushort? Param)`: Word < 0xFF00 is "Must have"
    that unit (Param null).
  - `sealed record ReqLevel(int Level, IReadOnlyList<ReqLine> Lines)` (Level 1-3).
  - `sealed class ReqRecord`: `int Id`, `bool Used`, `IReadOnlyList<ReqLine> Lines`,
    `IReadOnlyList<ReqLevel> Levels`; ctor `(int id, bool used, IReadOnlyList<ReqLine> lines, IReadOnlyList<ReqLevel> levels)`;
    `static ReqRecord FromWords(ReqTable table, int id, bool used, IReadOnlyList<ushort> words)`
    (throws `InvalidDataException` with a reason); `IReadOnlyList<ushort> ToWords()`;
    `bool SameAs(ReqRecord other)` (id, used, words); `ReqRecord WithUsed(bool)`.
  - `sealed class RequirementTable`: `ReqTable Table`, `IReadOnlyList<ReqRecord> Records`;
    ctor `(ReqTable, IReadOnlyList<ReqRecord>)`; `ReqRecord? Find(int id)`;
    `RequirementTable With(ReqRecord)` (replaces the record with that id in place, else appends);
    `RequirementTable Without(int id)`; `bool SameAs(RequirementTable)` (same records, same order).
  - `sealed class Requirements`: ctor `(IReadOnlyList<RequirementTable> tables)` (five, in
    ReqTables.All order); indexer `RequirementTable this[ReqTable]`; `Requirements With(RequirementTable)`;
    `bool SameAs(Requirements)`.
- Produces (namespace `Manifold.Core.Editor`):
  - `sealed record ReqOpcode(ushort Word, string Name, ReqParam Param)`; `const ushort ReqOpcodes.MustHave = 0`
    (the dialog's "Must have" choice; the line it makes is `new ReqLine(unitId, null)`).
  - `static IReadOnlyList<ReqOpcode> ReqOpcodes.Offered(ReqTable)`: "Must have" first, then
    spec §3's opcodes for that column in word order, without FF1F-FF21.
  - `static string ReqOpcodes.Describe(ReqTable, ReqLine, Func<int, string> unitName, Func<int, string> techName)`.

- [ ] **Step 1: Write the failing tests** in `ReqModelTests.cs`:
  - `MarineRoundTrips`: words `FF02 006F FF08 FF05 FFFF` (units, id 0) → 3 lines
    `(0xFF02, 0x6F)`, `(0xFF08, null)`, `(0xFF05, null)`, no levels; `ToWords()` gives the input.
  - `GhostBareIdsAreMustHave`: `FF02 006F FF08 FF05 0075 0070 FFFF` → lines 4 and 5 are
    `(0x75, null)`, `(0x70, null)`.
  - `UpgradeLevelsRoundTrip`: upgrades id 0, `FF02 007A FF05 FF07 FF1F FFFF FF20 0074 FFFF FF21 0074 FFFF`
    → 3 lines; levels 1 (0 lines), 2 (`(0x74, null)`), 3 (same); round-trips.
  - `EmptyRecordIsOneTerminator`: `new ReqRecord(5, true, [], []).ToWords()` is `[0xFFFF]`;
    `FromWords(Units, 5, true, [0xFFFF])` has no lines and no levels.
  - `TechParameterOnUnitsOnly`: units `FF24 FF02 0026 FF25 0020 FFFF` → `(0xFF25, 0x20)`;
    orders `FF25 FFFF` → `(0xFF25, null)` (FF25 takes no parameter there).
  - `WordsAfterTheEndRefused`: orders `FF1E FFFF 0001` throws `InvalidDataException`.
  - `MissingTerminatorRefused`: units `FF02 006F` throws `InvalidDataException`.
  - `OfferedLists`: `Offered(Units)` holds FF04 and FF25 and not FF12; `Offered(Orders)` holds
    FF1E and FF12 and not FF04; neither holds FF1F; both start with `MustHave`.
  - `DescribeLines`: `Describe(Units, (0xFF02, 0x6F), u => u == 0x6F ? "Terran Barracks" : "?", t => "?")`
    is "Current unit is Terran Barracks"; a bare 0x70 is "Must have <name>"; `(0xFF01, null)` is
    "or"; units `(0xFF12, null)` is "Unknown 0xFF12"; units `(0xFF25, 0x20)` with tech 32
    "Lurker Aspect" is "Has researched Lurker Aspect".
  - `TableWithReplacesInPlace`: a table of records 0, 1, 2; `With(record 1 changed)` keeps the
    order; `With(record 7)` appends; `Without(1)` leaves 0, 2.

- [ ] **Step 2: Run them to verify they fail.** Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter ReqModelTests`. Expected: build errors (types missing).

- [ ] **Step 3: Implement the types listed under Interfaces.** `FromWords` reads lines up to
  0xFFFF (end, no levels) or, for upgrades only, up to a level marker FF1F-FF21; each level
  reads lines up to its 0xFFFF; a parameter word follows any word whose `ParamOf` isn't None.
  Anything left after that, or a missing 0xFFFF, is refused. `ToWords`: lines; then either
  0xFFFF (no levels) or, per level, marker, lines, 0xFFFF. Opcode names from spec §3 verbatim.

- [ ] **Step 4: Run them to verify they pass.** Same command. Expected: all pass.

- [ ] **Step 5: Commit.** `git add` the five files; message "feat(editor): the requirements model and opcodes".

### Task 2: The sections, the file, the import and vanilla

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/ReqSections.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/RequirementsFile.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Data/Req/VanillaRequirements.cs`
- Create: `tools/ManifoldEditor/fixtures/requirement-tables.bin` (and a line in `fixtures/README.md`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqFileTests.cs`

**Interfaces:**
- Consumes: Task 1's types; `FgpProject.Parse(byte[])` (its `Version`, `GameVersion`, `Sections`); `PeImage.Read(uint va, int length)`.
- Produces:
  - `static class ReqSections`: `RequirementTable Parse(ReqTable, byte[] section, out int blanks)`
    (0xFF00 words dropped and counted; throws `InvalidDataException` with the plugin's
    reasons: "cut short", "a record does not end with 0xFFFF", "data after the last record");
    `byte[] Write(RequirementTable)` (throws `InvalidOperationException` naming the id for
    an id > 255 or more than 255 words).
  - `static class RequirementsFile`: `const string ArchivePath = "Manifold\\requirements.bin"`;
    `byte[] Write(Requirements)`; `Requirements Read(byte[] bytes, Requirements vanilla, List<string> notes)`
    (throws `InvalidDataException` with a reason; a missing section is vanilla's table, with a note).
  - `static Requirements RequirementsImport.FromProject(FgpProject project, Requirements vanilla, List<string> status)`
    (in `RequirementsFile.cs`): sections parsed, missing ones vanilla; per table with blanks a
    line "{tag}: {n} blank item(s) left out".
  - `static class VanillaRequirements`: `const uint Start = 0x514178, End = 0x51521C`;
    `Requirements FromBlock(ReadOnlySpan<byte> block)`; `Requirements Read(PeImage exe)`.

- [ ] **Step 1: Make the fixture.** `requirement-tables.bin`: the 4260 bytes of StarCraft.exe
  1.16.1 at 0x514178-0x51521C, rebuilt from the probe's table dump
  (`/root/.claude/uploads/766c9e66-d560-5026-8461-003701a27af9/bda1bd79-req_probe.txt`,
  "Table candidates": each table's word 0, its records, 0xFFFF; zeros between tables), by a
  script in the scratchpad. Check: 4260 bytes; the bytes after units' end are zeros up to
  0x5145C0.

- [ ] **Step 2: Write the failing tests** in `ReqFileTests.cs`:
  - `ProjectSectionsRoundTrip`: for each table, `Write(Parse(section))` equals the fixture
    project's section bytes (`SCManifold.fgp`), and blanks is 0.
  - `BlanksDropped`: a section with one record `FF02 FF00 006F FFFF` parses to `(0xFF02, 0x6F)`, blanks 1.
  - `SectionErrors`: a cut-short record, a record ending `0x0001`, and a trailing byte each
    throw with the reason named above.
  - `WriteRefusesPastLimits`: a record with id 256, and one of 256 words, throw `InvalidOperationException`.
  - `FileRoundTrips`: `Read(Write(r))` is `SameAs(r)` for the project's requirements; the
    bytes start "FgPa", version 7, "1.16.1\0", and hold the five tags in order.
  - `EmptyRecordInFile`: a units record with no lines survives `Write`/`Read`.
  - `FileMissingSectionIsVanilla`: a file without OrdR reads OrdR as vanilla and adds a note.
  - `VanillaMatchesProjectExceptHealing`: `FromBlock(fixture)` equals the project's tables
    record for record except tech use 24 and 34; record counts 91, 51, 26, 33, 112.
  - `VanillaRecordsRoundTrip`: every vanilla record's `ToWords()` equals the words read from
    the block.
  - `RealExeMatchesFixture` (skipped without `MANIFOLD_SC_DIR`, as the existing real-exe
    test): `Read(new PeImage(StarCraft.exe))` is `SameAs(FromBlock(fixture))`.

- [ ] **Step 3: Run them to verify they fail.** `--filter ReqFileTests`. Expected: build errors.

- [ ] **Step 4: Implement the four classes.** The vanilla walk: records until an id of 0xFFFF;
  a record's words run to 0xFFFF and go on while the next word is FF1F-FF21.

- [ ] **Step 5: Run them to verify they pass.** Expected: all pass (the real-exe test skipped).

- [ ] **Step 6: Commit.** Message "feat(editor): requirements.bin, FireGraft's sections and the vanilla tables".

### Task 3: The document, opening and saving

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/ButtonSetDocument.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/EditorSession.cs`
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/RequirementEdits.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorState.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/ReqDocumentTests.cs`

**Interfaces:**
- Consumes: Tasks 1-2.
- Produces:
  - `readonly record struct ReqPlace(ReqTable Table, int Id)`; `Place` gains `ReqPlace? Req = null`.
  - `sealed record ReqEdit(Requirements Before, Requirements After, ReqTable Table, int Id)`;
    `Change` gains `ReqEdit? Req = null`, and its `Place` carries it.
  - `sealed record RequirementSet(Requirements Opened, Requirements? Saved, Requirements Vanilla, string? Problem)`
    (Saved null: the exe has no good file, so the requirements count as unsaved).
  - `ButtonSetDocument` ctor gains `RequirementSet? requirements = null`; members
    `Requirements? Requirements`, `Requirements? VanillaRequirements`, `string? RequirementsProblem`,
    `bool RequirementsDirty`, `bool ApplyRequirements(ReqTable, int id, Func<RequirementTable, RequirementTable>, string label)`
    (false without requirements, while a problem stands, or when nothing changes),
    `bool RecordDiffersFromVanilla(ReqTable, int id)`, `bool RecordChangedSinceSave(ReqTable, int id)`,
    `void ReplaceBadRequirements()` (clears the problem; not an undo step; leaves them unsaved);
    `IsDirty` and `MarkSaved` include them.
  - `static RequirementSet EditorSession.OpenRequirements(IArchive modExe, string fgpPath, Requirements vanilla, List<string> status)`;
    `EditorSession.Open` gains `RequirementSet? requirements = null`; `FilesToSave` adds the
    file when `RequirementsDirty` (a Write refusal returns "Manifold\requirements.bin: {reason}. Nothing was saved.").
  - `RequirementEdits` (pure): `readonly record struct ReqLineAt(int Level, int Index)` (Level 0:
    the record's own lines); `ReqRecord Insert(ReqRecord, ReqLineAt, ReqLine)`, `Replace`,
    `Remove`, `Move(ReqRecord, ReqLineAt, int delta)` (within its level; unchanged at an end),
    `AddLevel(ReqRecord)` (the next level, empty; refused past 3), `RemoveLastLevel(ReqRecord)`;
    `sealed record ReqRow(ReqLineAt? At, int? LevelHeader, ReqLine? Line)` and
    `IReadOnlyList<ReqRow> Rows(ReqRecord)` (lines, then per level its header row and lines).
  - `EditorState`: `ReqPlace? LastReqPlace` (set on undo and redo); `bool EditRequirement(ReqTable, int id, Func<ReqRecord, ReqRecord>, string name)`,
    `SetRequirementUsed(ReqTable, int id, bool used, string name)`, `AddRequirementRecord(ReqTable, int id, string name)`
    (Used, no lines), `DeleteRequirementRecord(ReqTable, int id, string name)`,
    `RevertRequirementToVanilla(ReqTable, int id, string name)` (no vanilla record: deleted) — each
    one step with the Global Constraints' labels.

- [ ] **Step 1: Write the failing tests** in `ReqDocumentTests.cs` (a document made with the
  project's requirements as Opened and Saved, vanilla from the fixture):
  - `EditIsOneLabelledStep`: `EditRequirement(Units, 0, r => Insert(r, (0, 3), (0x70, null)), "Marine")`
    → `UndoLabel` "Edit Marine's requirements"; dirty; undo gives back `SameAs` the opened; `LastReqPlace` is (Units, 0).
  - `UsedAndRecordSteps`: turning off, adding record 91, deleting, reverting each record one
    step with their labels; reverting tech use 24 makes it vanilla's (no SCV).
  - `VanillaAndSavedMarks`: tech use 24 differs from vanilla, units 0 doesn't; after an edit of
    units 0 `RecordChangedSinceSave(Units, 0)` is true, after `MarkSaved` false, after an undo true.
  - `ProblemBlocksEdits`: with `Problem` set, `ApplyRequirements` returns false and
    `RequirementsDirty` is false; after `ReplaceBadRequirements` it's dirty and edits apply.
  - `OpenPrefersTheFile`, `OpenImportsTheProject`, `OpenWithNothingIsVanilla`,
    `OpenBadFileSetsProblem`, `OpenBadProjectIsVanilla`: with fake archives (as SessionTests): the file wins with
    Saved = Opened; without it the project is imported with Saved null and a status line
    "Imported FireGraft's requirements from Firegraft\SCManifold.fgp"; with neither, vanilla
    with Saved = Opened; a damaged file gives Problem and Saved null. `OpenBadProjectIsVanilla`:
    a damaged .fgp gives a status line with its reason and vanilla tables, Saved = Opened.
  - `SaveWritesTheFileWhenDirty`: Save adds `Manifold\requirements.bin` only when dirty; a
    record of 256 words makes Save return the refusal and write nothing.
  - `RowsAndLevelEdits`: upgrades 0's `Rows` are 3 lines then headers 1, 2, 3 with 0, 1, 1
    lines; `AddLevel` on a record with 3 levels is refused; `Move` can't cross a level.

- [ ] **Step 2: Run them to verify they fail.** `--filter ReqDocumentTests`. Expected: build errors.

- [ ] **Step 3: Implement** the members under Interfaces, following how the document holds
  .dat files (a current, a saved and a vanilla value; `Record`/`Put` swap the whole
  `Requirements`).

- [ ] **Step 4: Run the whole suite.** Full command from Global Constraints. Expected: all
  pass, earlier tests unchanged.

- [ ] **Step 5: Commit.** Message "feat(editor): requirement edits, undo steps, open and save".

### Task 4: The plugin reads requirements.bin first

**Files:**
- Modify: `GPTP/hooks/interface/requirements_loader.cpp` (`applyOnce`, `report`)
- Modify: `tests/fg_requirements_test.cpp`
- Modify: `CLAUDE.md` (the FireGraft requirements module's line)
- Modify: `docs/superpowers/specs/2026-10-29-fg-requirements-design.md` ("What the plugin does": step 0)

**Interfaces:**
- Consumes: `mpqread::read(const char*, std::vector<u8>&)` and its `OK`/`MISSING`/`FAILED` results; `fgreq::findSection`, `fgreq::buildTable`.

- [ ] **Step 1: Add the host test** `requirementsFileLayout` to `fg_requirements_test.cpp`:
  a buffer "FgPa", u32 7, "1.16.1\0", then the five sections each with one record; every
  `findSection(tag)` returns its section and `buildTable` gives word 0, the record, 0xFFFF;
  a sixth tag ("Buts") is absent (NULL, no error). Call it from `main` with the others.

- [ ] **Step 2: Change `applyOnce`.** Read `"Manifold\\requirements.bin"` first. `OK`: use it
  as the project and its name in every line, with the prefix "Requirements: " instead of
  "FireGraft requirements: " (a file-scope `const char* prefix` that `report` uses).
  `MISSING`: today's datalist path, unchanged. Anything else: report and return (no fallback).

- [ ] **Step 3: Re-read the diff** for includes, `sprintf_s` buffer sizes, and that the .fgp
  path's lines are unchanged. (No build here; the user runs `tests/verify.ps1`.)

- [ ] **Step 4: Docs.** CLAUDE.md's FireGraft requirements line: the plugin reads
  `Manifold\requirements.bin` (written by the Manifold Editor) first, then the .fgp. The
  2026-10-29 spec: the same, marked [BUILT], unverified in game.

- [ ] **Step 5: Commit.** Message "feat(requirements): the loader reads Manifold\requirements.bin before FireGraft's project".

### Task 5: The Requirements page

**Files:**
- Create: `tools/ManifoldEditor/ManifoldEditor.App/RequirementsPage.cs`
- Create: `tools/ManifoldEditor/ManifoldEditor.App/ReqLineDialog.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/MainWindow.cs` (page item, `ShowPage`, `Refresh`, `KeepSelection`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DataPage.cs` (`public IReadOnlyList<string> Names(DatId id) => context.Names(id);`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Services/Workspace.cs` (`Open`)
- Modify: `docs/superpowers/specs/2026-10-30-dat-requirements-design.md` (status), `CLAUDE.md` (the Manifold Editor's parts), `tools/ManifoldEditor/README.md` (the page)

**Interfaces:**
- Consumes: Task 3's `EditorState` and document members, `RequirementEdits.Rows`,
  `ReqOpcodes.Offered/Describe`, `ReqTables`.
- Produces: `RequirementsPage(Func<OpenedExe?> opened, Action changed, Func<DatId, IReadOnlyList<string>> names, Func<string, string, Task> message)`
  with `Grid Root`, `void Refresh()`, `void Show(ReqPlace)`; `ReqLineDialog(XamlRoot, ReqTable, ReqLine? current, bool offerLevel, names)`
  with `Task<ReqLineChoice?> ShowAsync()` where `sealed record ReqLineChoice(ReqLine? Line, bool AddLevel)`.

- [ ] **Step 1: Workspace.** Read StarCraft.exe's bytes once; `VanillaRequirements.Read` from
  the same `PeImage` as the button sets (a Step "Reading the vanilla requirements"); then
  `EditorSession.OpenRequirements(exe, fgp, vanilla, reqStatus)`, passed to `Open`; its
  status lines join the others.

- [ ] **Step 2: The page** per spec §6: table selector, find box, entry list (names from
  `names(ReqTables.NamesFrom(t))`, "(off)", greyed "(none)", the vanilla • and the Data page's
  unsaved colour via `RecordChangedSinceSave`), the Used checkbox with its tooltip, the rows
  (`Rows` + `Describe`; headers "Level 1"-"Level 3"; level lines indented), the six line
  buttons, Add record / Delete record / Revert to vanilla, and, while `RequirementsProblem`
  stands, a bar with the problem and "Replace it" (calls `ReplaceBadRequirements`). Every
  edit goes through `EditorState` and then `changed()`.

- [ ] **Step 3: The dialog**: opcode `ComboBox` from `Offered(table)` ("Must have" first);
  for a Unit or Tech parameter a find box over a list of `names(Units)` or `names(Techdata)`;
  for upgrades, when `offerLevel`, a last choice "Level N" (N = levels + 1) that returns
  `AddLevel`. OK is disabled until a parameter is picked when one is needed.

- [ ] **Step 4: MainWindow.** A "Requirements" `SelectorBarItem` after Data, its text
  "Requirements ●" while `RequirementsDirty`; `ShowPage` and `Refresh` handle the fourth
  page; `KeepSelection` shows the page and `Show(place)` when `LastReqPlace` is set; the
  last page is remembered as "requirements".

- [ ] **Step 5: Compile check.** Run `bash <scratchpad>/appcheck.sh`. Expected: "App compile: 0 errors". Then the full Core suite: all pass.

- [ ] **Step 6: Docs.** The spec's status: [BUILT] on the date, Core tested on Linux, the
  window compiles, §8's Windows list not yet run. CLAUDE.md's Manifold Editor paragraph and
  the README gain the Requirements page and `Manifold\requirements.bin`.

- [ ] **Step 7: Commit and push** the work branch. Message "feat(editor): the Requirements page".
