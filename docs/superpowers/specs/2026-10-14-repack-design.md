# Manifold Editor part 3: Repack (the plugin and to-repack files)

Status: design 2026-10-14, from the user's request ("I want our editor to be able to pack
the qdp, with this alone I barely need to open FG anymore and I hate its exe lock"), the
scope they chose ("Plugin + to-repack files": one Repack command, no post-build step) and
their correction (the to-repack folder defaults to `<exe folder>\to-repack\` and can be
changed). Built 2026-10-15 (plan `docs/superpowers/plans/2026-10-14-repack.md`): the exe
reader and writer, the folder scan, the repack and the settings are tested on Linux with fake
exes; the window compiles but has not run, and §8's Windows steps are not done. The exe layout of §2 was read from the user's `SCManifold.exe` with
two read-only probes on 2026-10-14. Builds on parts 1 and 2,
`docs/superpowers/specs/2026-10-05-button-set-editor-design.md` and
`docs/superpowers/specs/2026-10-08-string-editor-design.md`.

## 1. Goal

Repacking takes two tools today: FireGraft, to put a new `GPTP.qdp` in `SCManifold.exe`
(and it holds the exe open while it runs), and PyMPQ, to add each file of
`SCManifold\to-repack\` by hand. Replace both with one **Repack** command in the editor.

Success: build the plugin, press Repack, and start the game: the game-start build stamp is
the new build, and every to-repack file is in the exe. The editor's own files
(`Manifold\buttonsets.bin`, its `rez\stat_txt.tbl`) survive, and FireGraft is not opened.

## 2. Facts this rests on

**FireGraft's exe is not MPQDraft's.** MPQDraft (source: `github.com/sjoblomj/MPQDraft`,
CDDL) puts plugins in an "Embedded File System" (`"EFS "` header) between its stub and the
MPQ. The user's exe has no EFS (probe 1). FireGraft's own layout (probe 2, the user's
`SCManifold.exe`, 18,033,568 bytes):

| Offset | Bytes | What |
|---|---|---|
| `0x0` | 0x25E00 | The launcher: a PE with 4 sections (`.text` 3 KB, `.rdata`, `.data`, `.rsrc` 149 KB). Its resources hold `STUB/#257` (a 143,360-byte PE, FireGraft's loader) and an icon. Its last section ends at 0x25E00. |
| `0x25E00` | 24 | The plugin header, six u32: `"PEGF"`, 1, 1, `0x1B4D69B6` (GPTP's `PLUGIN_ID`), `0x3C4400` (3,949,568: the plugin's size), 0 |
| `0x25E18` | 3,949,568 | `GPTP.qdp`, stored as is (a PE; its first 64 bytes match the user's current build) |
| `0x3EA218` | 488 | Zeros, to the next 512-byte boundary |
| `0x3EA400` | 13,928,352 | The MPQ (`MPQ\x1A`, format 0, header 32), to the end of the file |

Read as: magic, version (1), plugin count (1), then for the plugin its id, its size and a
reserved 0, then its bytes. With one plugin, "count" and "version" can't be told apart from
other readings of the two 1s, and the place of a second plugin's header is unknown; so the
editor handles exactly this layout and refuses any other (§3).

**The MPQ can move.** Storm finds an MPQ inside a file by looking for its header at each
512-byte boundary, and a format-0 MPQ's tables are offsets from its own header. The
editor's saves already rely on this file layout: StormLib writes the MPQ in place and the
game reads it (part 1, tested in game).

**Not known until a game runs:** whether FireGraft's loader trusts the header's size and
reads nothing else (no checksum, no fixed MPQ offset). The two copies of the plugin's bytes
differ (the exe holds an older build of the same size), so the probe could not compare
them. §8's first Windows test settles it on a copy of the exe.

## 3. Reading the exe

`FireGraftExe.Parse(byte[] exe)` (Manifold.Core, pure) returns the launcher, the plugin id,
the plugin's bytes and the MPQ's offset, or a reason it can't:

- The file is a PE; its last section's end (raw offset + raw size) is where the header must
  be.
- The header: `"PEGF"`, 1, 1, an id, a size, 0. Anything else (another magic, a count of
  2, a non-zero last field) is refused: "a FireGraft layout the editor doesn't know".
- The plugin's bytes start with a PE header, and fit in the file.
- Zeros from the plugin's end up to the next 512-byte boundary, and there `MPQ\x1A`. An
  MPQ elsewhere, non-zero padding, or an `MPQ\x1B` user-data header is refused.

The status line, on opening, names the plugin in the exe and compares it with the plugin
file (§5): "GPTP.qdp in the exe: 3,949,568 bytes; GPTP.qdp beside it is newer (built
21:03)". An exe without the header (a plain MPQ, or MPQDraft's) shows "no FireGraft plugin
header" and Repack then adds files only.

## 4. Writing the exe

`FireGraftExe.WithPlugin(byte[] exe, byte[] plugin)` returns the new file:

1. The launcher's bytes, unchanged.
2. The header with the new size; the id stays the exe's.
3. The new plugin's bytes.
4. Zeros to the next 512-byte boundary.
5. The MPQ, from its offset to the end of the old file, byte for byte.

Refused: a plugin that isn't a 32-bit DLL (PE machine 0x14C with the DLL flag), and a
plugin with `MPQ\x1A` at a 512-byte boundary of the new file (Storm would open that one
first; practically never, but cheap to check).

## 5. The plugin file and the to-repack folder

Both have a default and a per-exe override, kept in the editor's settings next to the
string mirror's answer:

- **Plugin:** `<exe folder>\GPTP.qdp`, where the GPTP build's post-build step copies it.
- **To-repack folder:** `<exe folder>\to-repack\`.

The Repack dialog shows both, each with **Change...** and **Default**. When the folder is
overridden, the string mirror's default path (part 2, §7) becomes
`<folder>\rez\stat_txt.tbl` for an exe not yet asked; an answer already given stays.

**Which files:** every file in a subfolder of the to-repack folder, under its path relative
to the folder (`rez\statdata.bin`, `unit\...`), added PKWARE-imploded as the editor's saves
are. Left out: files at the folder's top level (the `make_*.py` generators) and `*.bak` (the
string mirror's backups).

## 6. The Repack command

A toolbar button **Repack...** opens a dialog:

- The plugin: its path and build time, its size, and "replaces the exe's" or "same as the
  exe's" (then it isn't rewritten). Missing: "no plugin: files only".
- The folder, then one row per file: its path in the MPQ, its size, and **new**,
  **changed** or **same** against the exe. New and changed rows are ticked; same rows are
  not.
- Two rows are unticked, with a warning, when they differ from the exe's:
  `rez\stat_txt.tbl` ("would replace the strings saved by the editor") and
  `Manifold\buttonsets.bin` ("would replace the editor's button sets").
- **Repack** and **Cancel**. Repack is disabled when nothing is ticked and the plugin is
  the same.

With unsaved edits, Repack first asks **Save and repack** or **Cancel**.

Repack then:

1. Copies the exe to `<exe>.repacking`.
2. Adds the ticked files to the copy with StormLib (one flush).
3. Reads the copy, and writes `WithPlugin(copy, plugin)` over it when the plugin changed.
4. Checks the result: it parses (§3) with the new plugin's bytes; its MPQ opens; every
   added file reads back the same; `Manifold\buttonsets.bin` (and `rez\stat_txt.tbl` when
   it wasn't ticked) read back the same as before.
5. Swaps it in with `File.Replace`, keeping the previous exe as `<exe>.bak`, as saves do.

Any failure leaves the exe as it was and says which step failed. A busy exe (the game
running, FireGraft open) gives the editor's busy message. Afterwards the editor reopens the
exe, so the window shows what is now packed, and the status line names the new plugin.

The editor holds no handle on the exe between commands (StormLib opens it per read or
write), so it never locks the exe the way FireGraft does.

## 7. Code

- `Manifold.Core/Data/FireGraftExe.cs`: `Parse`, `WithPlugin`, the header record. Pure.
- `Manifold.Core/Model/Repack.cs`: the file list from a folder (`Scan`: path in the MPQ,
  bytes, state against the exe), the protected paths, and the check of step 4 against an
  `IArchive`.
- `Manifold.Core/Editor/EditorSettings.cs`: per exe, the plugin path and the folder
  (null: the default).
- `Manifold.Storm/StormArchive.cs`: a write into a given file (`WriteInto` made public as
  `AddFiles(string file, ...)`), so Repack can add to its copy and then rebuild it.
- `ManifoldEditor.App`: the toolbar button and the dialog, and reopening the exe.

## 8. Testing

C# tests, on Linux (fake exes built in the test, as the probes were tried: a small PE as the
launcher, a small DLL as the plugin, a hand-made MPQ header):

- `Parse` reads the user's layout: the header fields, the plugin's bytes, the MPQ offset.
- Each refusal: wrong magic, count 2, non-zero reserved field, non-zero padding, MPQ not at
  the boundary, `MPQ\x1B`, a plugin that isn't a DLL.
- `WithPlugin` with a larger and a smaller plugin: the result parses, holds the new
  plugin's bytes, has the MPQ at a 512-byte boundary with the same bytes as before, and
  the launcher's bytes unchanged. With the same plugin, the result is the input.
- `MPQ\x1A` at a boundary inside the new plugin is refused.
- `Scan`: subfolders only, no top-level files, no `*.bak`, paths with `\`; states new,
  changed, same; the two protected paths unticked when they differ.
- The settings: defaults, an override, back to the default; the mirror's default follows
  the folder.

On Windows, in order:

1. **The loader (on a copy).** Copy `SCManifold.exe` to `SCManifold-test.exe`. Touch
   `game_hooks.cpp`, build, open the copy in the editor, Repack with only the plugin, and
   start a game from the copy: the build stamp is the new one. Then Repack again with an
   older or newer `GPTP.qdp` of a different size, and start again. If either fails, the
   loader reads more than the header; stop and report what the game shows.
2. **The files.** Change one to-repack file (or add one), Repack, and check it in game
   (or with `mpqfind.py`). The button sets and strings are unchanged.
3. **The warnings.** Edit `to-repack\rez\stat_txt.tbl` in PyTBL: the dialog shows it
   unticked with its warning.
4. **Busy.** Start the game, then Repack: the busy message, and the exe unchanged.
5. **The real exe.** Repack `SCManifold.exe` itself, and play.

## 9. Out of scope

- FireGraft's EXE edits: they live in its `.fgp` in the MPQ and are left as they are.
- More than one plugin, other plugin ids, and MPQDraft's EFS layout.
- Building an exe without FireGraft's launcher.
- Repacking automatically after each build (a console tool for the post-build step was
  offered and not chosen).
