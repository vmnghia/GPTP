# String Editor Implementation Plan (Manifold Editor, part 2)

**Goal:** edit `rez\stat_txt.tbl` in the Manifold Editor: from the button panel and from a
Strings view, written as PyMS's PyTBL writes it, saved into the exe together with the
button sets and mirrored to `to-repack\`.

**Spec:** `docs/superpowers/specs/2026-10-08-string-editor-design.md`. Reference: PyMS's
PyTBL (`PyMS/FileFormats/TBL.py`, `PyMS/PyTBL/PreviewDialog.py`, `PyMS/FileFormats/FNT.py`,
`Docs/pytbl.txt`).

**Architecture:** as in part 1. Everything but the window lives in `Manifold.Core` and is
tested on Linux with `dotnet test tools/ManifoldEditor/Manifold.Core.Tests`. The window is
code-only WinUI and compile-checked on Linux with `dotnet msbuild -restore -t:Compile
-p:WindowsAppSDKSelfContained=false` in `ManifoldEditor.App/`. `Manifold.Storm` builds
with `-p:EnableWindowsTargeting=true`.

## Tasks

| # | Task | Where | Check |
|---|---|---|---|
| 1 | `StringTable` and `TblText`, the report test | Linux | Core tests; the user runs the report |
| 2 | Strings in the document and its history; `StringUses`; `EditorState` commands | Linux | Core tests |
| 3 | Saving: both files in one swap; the to-repack copy; the check on open | Linux, plus Storm on Windows | Core tests |
| 4 | The preview: `tfontgam.pcx` colours, runs, cost line | Linux | Core tests |
| 5 | The window: Strings page, the button panel's string rows, dialogs | Windows build | compile on Linux; spec §8 on Windows |
| 6 | Docs, then the user's round (spec §8) | anywhere, then in game | |

### Task 1: the table

- `Data/StringTable.cs`: immutable, persistent.
  - `Parse(bytes)`: each id's **segment** (offset to the next distinct offset, the last
    to the end of the file), as PyTBL's `load_file`. It also keeps each id's original
    offset, so ids that shared one still share it when written.
  - `Segment(id)`, `Count`.
  - `With(id, bytes)` adds a missing final NUL. If the entry just before in offset order
    runs into this one (its segment has no NUL), that entry first gets its full text as
    its own segment.
  - `Add(bytes)` returns the new id.
  - `Write()`: unedited segments in their original offset order, then edited and new
    ones in id order. It fails with the first id whose start passes 65,535.
  - `BytesFree`.
  - `CheckWritten(bytes)` is the save's self-check: every id reads back its segment.
- `Data/TblText.cs`: PyTBL's `decompile_string` (bytes 0-31, `#`, `<`, `>` as `<N>`) and
  `compile_string` (`<N>` with N from 0 to 255 is that byte, anything else is literal; a
  char above 255 is refused). `EditText` hides the final `<0>`.
- `Data/HotkeyString.cs`: hotkey, type and text, split and joined; the six types'
  labels from PyTBL.
- `StatTxt` becomes a view over `StringTable`. Its `Get` keeps part 1's semantics (first
  part, hotkey-NUL joined), so the set names and card text don't change.
- Tests:
  - PyTBL's doc examples;
  - round trips (shared, multi-NUL, overlapping, trailing);
  - one edit changes one id;
  - the budget;
  - every byte through `<N>`.
- **Report test** (`StatTxtReport`, skipped unless `MANIFOLD_STAT_TXT` names a `.tbl`).
  It writes `tools/ManifoldEditor/reports/stat_txt-report.txt` (spec §8 step 0), for the
  user to commit.

### Task 2: the document

- `ButtonSetDocument` holds `Strings` (a `StringTable`, or null when none was found).
  A history step changes a set, the strings, or both (a copy-for-button is one step).
  `Undo` and `Redo` return where the step was made: a set, or a string id.
  - `StringsDirty` is true when the strings differ from those last saved.
  - The opened table is kept for Revert string.
- `Editor/StringUses.cs`: for an id, the buttons (set, index, enabled or disabled) and
  the unit name it is.
- `EditorState`:
  - `EditString(id, bytes)`;
  - `CopyStringForButton(index, field)`;
  - `NewStringForButton(index, field)`;
  - `AddString()`;
  - `RevertString(id)`;
  - `SharedWith(index, field)`.

  Its strings lookup reads the document, so names and hotkey checks follow edits.

### Task 3: saving

- `IWritableArchive.Write(files)`: several files in one copy-and-swap. `StormArchive`
  writes each one imploded.
- `EditorSession.Save` writes `buttonsets.bin`, plus `rez\stat_txt.tbl` when
  `StringsDirty`. The table is checked with `CheckWritten` first.
- `Model/StringMirror.cs`:
  - `DefaultPath(exe)` is `<exe folder>\to-repack\rez\stat_txt.tbl`.
  - `Write(path, bytes)` keeps a `.bak` and replaces the file atomically.
  - `Compare(exeTable, path)` says same, differs (with which is newer) or missing.
- `EditorSettings.StringMirrors`: exe path to mirror path, or "" for none; no entry means
  ask on the first save.

### Task 4: the preview

- `Data/Pcx.cs` decodes `game\tfontgam.pcx` (RLE, 8-bit, its palette).
- `Editor/TooltipPreview.cs` turns text into lines of coloured runs:
  - PyMS's `COLOR_CODES_INGAME`, with `COLOR_OVERPOWER`;
  - `<10>` and `<12>` break lines, `<18>` aligns right, `<19>` centres;
  - a hotkey string's type adds PyTBL's sample cost line.

### Task 5: the window

- A selector at the top switches between the button sets and a Strings page.
- **Strings page**:
  - the list (id, PyTBL text, uses);
  - search with Match case, `#id` to go to, and filters;
  - the edit pane with PyTBL's code list (a click inserts the code);
  - the preview;
  - the bytes left; Add and Revert.
- **Button panel**: the enabled string row (hotkey, type, text) and the disabled string
  row (text), each with:
  - Pick...;
  - the shared note with Edit for all and Make a separate copy;
  - New string when the id is 0.
- **Dialogs**:
  - on the first save that changes strings, the to-repack question;
  - on open, the two-tables choice.

### Task 6: docs

Spec status, `docs/resolution.md` §6, the README, then the user's round.
