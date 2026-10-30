# Manifold Editor .dat part 4 (entry counts, and the unsaved rows) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PyDAT's Add Entry and Set Entry Count on the Data page, and entries changed since the last save drawn in the accent colour.

**Architecture:** Core's `DatFile` gains PyMS's growth rules (`ExpandedCount`, `CanGrow`, `WithCount`); `EditorState` records a growth as one whole-file undo step through `ButtonSetDocument.ApplyDatFile`; the document answers `EntryChangedSinceSave` from the saved files it already keeps. The window adds the two commands, PyDAT's question and count dialog, the two keys, and colours a row as the list realises it.

**Tech Stack:** C# .NET 8, xUnit (+ Xunit.SkippableFact), WinUI 3 built in code (no .xaml).

**Spec:** `docs/superpowers/specs/2026-10-26-dat-counts-design.md` (part 1's formats: `2026-10-24-dat-editor-design.md` §3). PyMS reference at `/tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/pyms/src` (`<pyms>`): `PyMS/FileFormats/DAT/AbstractDAT.py` (`expanded_count`, `can_expand`, `expand_entries`), `PyMS/PyDAT/DATTab.py`, `EntryCountDialog.py`.

## Global Constraints

- Core checks: `MANIFOLD_ARR_DIR=<pyms>/PyMS/MPQ/arr dotnet test tools/ManifoldEditor/Manifold.Core.Tests` (469 pass, 2 skip today).
- App check: `bash /tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/appcheck.sh` (0 errors). No `.xaml`; one ContentDialog at a time (`ContentDialogs.Open`), at most three buttons.
- PyMS's growth: a file not yet expanded goes up to `ExpandedMin` (units 250); the count rounds up to `ExpandedMultiple` (units 12); above `ExpandedMax` is refused; a format with no `ExpandedMax` (mapdata) can't grow. Counts never shrink except by undo.
- First growth rewrites the file in the expanded layout: every property covers every entry, an uncovered value becomes 0, a widened type keeps its value; new entries are all 0.
- Undo labels: "Add entry to images.dat", "Set images.dat to 1200 entries" (the count after rounding).
- Menu items: "Add Entry (DatExtend)" Ctrl+Shift+A, "Set Entry Count (DatExtend)..." Ctrl+Shift+S, Data page only.
- First-growth questions (exact): plugin files (`DatFormats.PluginExtends`) "Expanded .dat files need the plugin's DatExtend. Expand images.dat?"; others "The plugin can't load an expanded units.dat: the game will refuse it. Expand it anyway?"
- Count dialog texts: "How many entries for images.dat?", "Resulting entry count: N", "Some entries are reserved", and the refusal "The entry count can't be set to less than the current entries (999)."
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01771DQsv5DDEKm1LjYbN2zB`. No model ids.

## Review Focus

- A count typed far past any maximum (`99999999999`, or text): refused with the maximum, no overflow (Task 1 test `ExpandedCountRefusesHugeAndNegative`).
- After a growth, the new entries take edits, a paste and a TXT import onto their ids, and a preview of a new image says "has no GRP file", not "past the end" (Task 2 test `NewEntriesAreUsableEverywhere`).
- Save, then undo a growth: the file is dirty (fewer entries than saved) though no row is coloured; redo makes it clean again (Task 2 test `UndoAGrowthPastTheSaveIsDirtyWithNoRowMarked`).
- Expanding a vanilla file colours nothing by itself: values that only became covered (sprites' health bar 0-129) are no difference from the saved file (Task 2 test `TheLayoutChangeAloneMarksNoOldEntry`).
- Set Entry Count with the current count, or with a count that rounds to the current one: no step recorded (Task 2 test `ASameCountRecordsNothing`).

---

### Task 1: The growth rules (`DatFile`)

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Data/Dat/DatFile.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatCountTests.cs`, and one test in `DatVanillaTests.cs`

**Interfaces:**
- Produces, on `DatFile`:
  - `int? ExpandedCount(long wanted)`: PyMS's `expanded_count` from this file (minimum only when not yet expanded, then the step); null when the result is above `Format.ExpandedMax`, when the format has no expanded layout, or when `wanted < 1`.
  - `bool CanGrow` = `ExpandedCount(Count + 1) is not null`.
  - `DatFile WithCount(long count)`: `count == Count` returns `this`; `count < Count` or `ExpandedCount(count)` null throws `ArgumentOutOfRangeException` whose message names the limit ("images.dat has at most 65536 entries", "images.dat has 999 entries: it can't have fewer"); otherwise the file at `ExpandedCount(count)` entries, expanded.

- [ ] **Step 1: Write the failing tests** in `DatCountTests.cs` (files built as `DatFileTests.Zeros`/`Expanded`):
  - `ExpandedCountAsPyMs`: vanilla units `ExpandedCount(229)` = 250, `(251)` = 252, `(912)` = 912, `(913)` null; an expanded units file of 252 `ExpandedCount(253)` = 264; weapons `(131)` = 131, `(256)` null; images `(1000)` = 1000, `(65536)` = 65536, `(65537)` null; mapdata `(2)` null.
  - `ExpandedCountRefusesHugeAndNegative`: `ExpandedCount(long.MaxValue)`, `(0)` and `(-5)` are null.
  - `CanGrowAsTheLimits`: vanilla images, units, weapons, sfxdata can; mapdata can't; an expanded weapons file of 255 can't.
  - `FirstGrowthRewritesTheLayout`: vanilla sprites with `health_bar` 7 on 130 and `image` 3 on 5, `WithCount(518)`: `IsExpanded`, `Count` 518, `Get(130, "health_bar")` 7, `Get(5, "image")` 3, `Get(5, "health_bar")` 0 (now covered), every property of entry 517 is 0.
  - `UnitsGraphicsKeepsItsValueAsAShort`: vanilla units with `graphics` 200 on 0, `WithCount(229)`: `Count` 250, `Get(0, "graphics")` 200, `With(0, "graphics", 300)` accepted.
  - `AGrownFileSavesAndParsesBack`: `DatFile.Parse(format, grown.Save())` is `IsExpanded`, has the new count and `SameAs(grown)`.
  - `WithCountRefuses`: `WithCount(Count)` is the same file (`ReferenceEquals`); `WithCount(Count - 1)` throws with "it can't have fewer"; `WithCount(70000)` on images throws with "at most 65536".

  In `DatVanillaTests.cs`, `EveryVanillaFileThatCanGrowRoundTrips` (skipped without `MANIFOLD_ARR_DIR`): for each format with `ExpandedMax`, `WithCount(Count + 1)` keeps every value of every entry the vanilla file covers, and parses back `SameAs`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter "DatCountTests|DatVanillaTests"`
Expected: FAIL (build error: `ExpandedCount` not defined).

- [ ] **Step 3: Implement** `ExpandedCount`, `CanGrow` and `WithCount` in `DatFile.cs`: the expanded arrays are each property's values placed at entry index (`offsets` 0), zero-filled to the new count; values widen without change (`ulong` already holds them).

- [ ] **Step 4: Run the Core checks**

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifoldEditor/Manifold.Core/Data/Dat/DatFile.cs tools/ManifoldEditor/Manifold.Core.Tests/DatCountTests.cs tools/ManifoldEditor/Manifold.Core.Tests/DatVanillaTests.cs
git commit -m "Manifold Editor: PyMS's rules for growing a .dat file"
```

### Task 2: Growth as an undo step, and entries changed since save

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorState.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core/Model/ButtonSetDocument.cs`
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatCountTests.cs`

**Interfaces:**
- Consumes: Task 1's `CanGrow`, `ExpandedCount`, `WithCount`.
- Produces:
  - `EditorState.AddEntry(DatId file, int shown)` → `bool`: `WithCount(Count + 1)` through `Document.ApplyDatFile(file, after, shown, "Add entry to images.dat")`; false when the file is missing or can't grow.
  - `EditorState.SetEntryCount(DatId file, long count, int shown)` → `string?`: null when applied or nothing changes; else the refusal message from `WithCount`. Label `$"Set {name} to {after.Count} entries"`.
  - `ButtonSetDocument.EntryChangedSinceSave(DatId file, int entry)` → `bool`: true when the saved file is missing, the entry is past its count, or `!current.EntrySameAs(entry, saved)`; false past the current count.
  - The undo step's place is `shown` (the entry selected before): undo shows it. The window selects the new last entry itself after Add or Set (Task 3).

- [ ] **Step 1: Write the failing tests** in `DatCountTests.cs` (documents as `DatDocumentTests.Document`/`State` build them):
  - `AddEntryIsOneStep`: images 999 → `AddEntry(Images, 4)` true; `Count` 1000; `UndoLabel` "Add entry to images.dat"; `DatDirty`; `Undo()` gives back a file `SameAs` the vanilla one (its `Save()` bytes equal); `Redo()` gives 1000 again.
  - `SetEntryCountRoundsAndLabels`: units `SetEntryCount(Units, 251, 0)` null; `Count` 252; `UndoLabel` "Set units.dat to 252 entries"; `SetEntryCount(Units, 100, 0)` returns the "can't have fewer" message and records nothing; `SetEntryCount(Weapons, 300, 0)` returns "weapons.dat has at most 255 entries".
  - `ASameCountRecordsNothing`: `SetEntryCount(Images, 999, 0)` null and `CanUndo` false; on a 252-entry units file, `SetEntryCount(Units, 252, 0)` records nothing.
  - `AddEntryRefusesWhatCantGrow`: mapdata false; nothing recorded.
  - `EntryChangedSinceSave`: after `EditDat(Units, 3, "armor", 2)`, entry 3 true, entry 4 false; after `AddEntry(Images, 0)`, entry 999 true, entry 998 false; after `MarkSaved()`, none; edit entry 3 then undo it: entry 3 false.
  - `TheLayoutChangeAloneMarksNoOldEntry`: `AddEntry(Sprites, 0)` on vanilla sprites: entries 0-516 false, 517 true.
  - `UndoAGrowthPastTheSaveIsDirtyWithNoRowMarked`: `AddEntry(Images, 0)`, `MarkSaved()`, `Undo()`: `DatDirty(Images)` true, no entry 0-998 marked; `Redo()`: `DatDirty` false.
  - `NewEntriesAreUsableEverywhere`: after `SetEntryCount(Images, 1001, 0)`: `EditDat(Images, 1000, "iscript_id", 5)` true; `ImportText` of PyDAT text for `Image(1000):` applies; `PasteEntry(Images, 1000, <copied text of entry 0>)` null; `new DatPreview(_ => null, palettes).Image(images, tbl, 1000).Reason` is "image 1000 has no GRP file".
  - `GrownNames`: units grown to 252 named (Basic) 228-249 "Reserved Unit #228".."Reserved Unit #249", 250 "Expanded Unit #250"; images grown to 1000, entry 999 "Expanded Image #999" (`DatNames.Build` as `DatNamesTests` calls it).

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter DatCountTests`
Expected: FAIL (build error: `AddEntry` not defined).

- [ ] **Step 3: Implement** `EditorState.AddEntry`, `EditorState.SetEntryCount` and `ButtonSetDocument.EntryChangedSinceSave`.

- [ ] **Step 4: Run the Core checks**

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifoldEditor/Manifold.Core tools/ManifoldEditor/Manifold.Core.Tests/DatCountTests.cs
git commit -m "Manifold Editor: entry counts as undo steps; entries changed since save"
```

### Task 3: The commands in the window

**Files:**
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/EntryCountDialog.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DataPage.cs` (menu items, `UpdateMenu`, `AddEntry`, `SetEntryCount`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/MainWindow.cs` (the two `DataAccelerator`s)

**Interfaces:**
- Consumes: Task 2's `EditorState.AddEntry`, `SetEntryCount`; Task 1's `CanGrow`, `ExpandedCount`; `DatFormats.PluginExtends(DatId)`.
- Produces: `public Task DataPage.AddEntry()`, `public Task DataPage.SetEntryCount()` (each through `Guard`, as `PasteEntry`); `sealed class EntryCountDialog` with `Task<long?> ShowAsync()` (null on Cancel).

- [ ] **Step 1: The menu.** After the overrides item: a separator, `addEntryItem` "Add Entry (DatExtend)" and `setCountItem` "Set Entry Count (DatExtend)...". `UpdateMenu`: Add enabled when `doc.Dat(file)?.CanGrow == true`; Set enabled when the file is there and `Format.ExpandedMax is not null`.

- [ ] **Step 2: The first-growth question** (`Task<bool> ConfirmGrowth(DatFile dat)`): true at once when `dat.IsExpanded`; else a ContentDialog (title "Expand <name>?", the Global Constraints text for `PluginExtends` or not, buttons "Expand" / "Cancel", default Cancel for a file the plugin refuses, Expand otherwise).

- [ ] **Step 3: `AddEntry()` and `SetEntryCount()`**: the pattern of `EditOverrides` (`busy`, `DialogOpen()`, `CommitPending()`, the same-state check after awaiting). Add: nothing when the file is missing or `!CanGrow` (the key on a file at its maximum), else question, then `state.AddEntry(file, Entry)`; Set: question, then `EntryCountDialog`, then `state.SetEntryCount(file, count, Entry)`. After a change: `entries[file] = <new Count - 1>`, `Remember()`, `changed()`. A refusal from `SetEntryCount` (not expected after the dialog's own checks) shows in `Tell("Count not changed", ...)`.

- [ ] **Step 4: `EntryCountDialog`** (PyDAT's `EntryCountDialog`): a ContentDialog titled "How many entries?", text "How many entries for images.dat?", a number box holding the current count, a constraints line from the format (`Minimum expanded entries: 250`, `Maximum expanded entries: 912`, `Expanded entries multiple: 12`, `Some entries are reserved` for units, each shown only when the format has it), and "Resulting entry count: N" updated on each change (`dat.ExpandedCount(n)`, or "past the maximum (N)" when null). OK (`PrimaryButtonClick`): parse with `DatValues.ParseNumber(text, DatType.Long)`; below the current count: `args.Cancel = true`, the refusal text shown in red in the dialog, the box set back to the current count; null `ExpandedCount`: `args.Cancel = true`, "<name> has at most <max> entries" in red.

- [ ] **Step 5: The keys.** In `MainWindow`, beside the Data page's other keys: `DataAccelerator(VirtualKey.A, dataPage.AddEntry, Control | Shift)` and `DataAccelerator(VirtualKey.S, dataPage.SetEntryCount, Control | Shift)`.

- [ ] **Step 6: Run the App check**

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add tools/ManifoldEditor/ManifoldEditor.App
git commit -m "Manifold Editor: Add Entry and Set Entry Count on the Data page"
```

### Task 4: The coloured rows, and the docs

**Files:**
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DataPage.cs` (`FillList`, the list's `ContainerContentChanging`)
- Modify: `tools/ManifoldEditor/README.md` (the Data page section: "Entry counts" and the colour), `CLAUDE.md` (the editor bullet: part 4 built, not yet run on Windows), `docs/resolution.md` §6 (part 4 `[BUILT 2026-10-26; not yet run on Windows]`; CRLF: edit with Python `io.open(..., newline='')`), the spec's Status line.

**Interfaces:**
- Consumes: Task 2's `ButtonSetDocument.EntryChangedSinceSave`.

- [ ] **Step 1: Mark the rows.** `FillList` computes `bool[] changed` beside `dots` (`doc.EntryChangedSinceSave(file, i)`), kept as `rowChanged`. The list's `ContainerContentChanging` sets the item container's `Foreground` to the accent brush when `rowChanged[args.ItemIndex]`, else clears it (`ClearValue(Control.ForegroundProperty)`). When only a row's colour changes (its text is the same), `FillList` sets the realised container's foreground directly (`list.ContainerFromIndex(i) as ListViewItem`, skipped when null). The accent brush: `Application.Current.Resources["AccentTextFillColorPrimaryBrush"]` when present, else a `SolidColorBrush` of `Colors.DodgerBlue`. The • text is unchanged.

- [ ] **Step 2: Run the App check**, then the Core checks.

Expected: 0 errors; all pass.

- [ ] **Step 3: The docs.** README: an **Entry counts** paragraph (the two commands and keys, the first-growth questions, the dialog, counts only grow, files the plugin refuses still flagged and confirmed on Save) and one sentence on the coloured rows beside the • paragraph; drop entry counts from **Not yet**. CLAUDE.md: "parts 4-5, entry counts and improvements, are not built" becomes "part 4, entry counts (Add Entry, Set Entry Count) and rows coloured when unsaved, is built and not yet run on Windows; part 5, improvements, is not built". resolution.md §6: part 4 moves from the remaining parts to its own `[BUILT 2026-10-26; not yet run on Windows]` sentence naming the spec and its §6 test round. The spec's Status: "Built 2026-10-26 (plan `docs/superpowers/plans/2026-10-26-dat-counts.md`): the rules are tested on Linux, the window compiles; not yet run on Windows."

- [ ] **Step 4: Commit**

```bash
git add tools/ManifoldEditor/ManifoldEditor.App tools/ManifoldEditor/README.md CLAUDE.md docs/resolution.md docs/superpowers/specs/2026-10-26-dat-counts-design.md
git commit -m "Manifold Editor: rows changed since save in the accent colour; docs for entry counts"
```
