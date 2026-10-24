# Manifold Editor: quality-of-life changes

Status: design 2026-10-24, from the user's list ("let's work on some QoL changes for the
editor first") plus two of the editor's own suggestions they picked (remember the window;
show the save and repack state). Their answers: Launch checks before it starts; Add new
button starts from a template picked in a list; Save as switches to the new exe; Condition
and Action filter as you type; the icon picker's one filter is Unused; the plugin and the
files are repacked by two commands. Not built yet. Builds on the editor's parts 1-3
(`2026-10-05-button-set-editor-design.md`, `2026-10-08-string-editor-design.md`,
`2026-10-14-repack-design.md`).

## 1. Goal

Fewer clicks and fewer surprises in the loop the user runs every day: edit buttons, save,
build the plugin, repack, start the game. Success: each change below works on Windows as
described in §11, and nothing the editor did before changes, apart from the toolbar's
layout and the Repack command being split in two.

Out of scope, kept in mind: **Repack without the dialog** (one press repacks the newer
plugin and every new or changed file). The user judged it too risky for now; it is noted
in `docs/resolution.md` §6. Also out: a preset for Set Worker Rally Point (not needed yet),
and names for custom icons (they are found by number and by the Unused filter).

## 2. The card: centred icons

Today an icon's GRP frame (36x34 for `cmdicons.grp`) is scaled to fit its square, and the
drawn pixels sit wherever the frame puts them: most icons look off-centre, high and to the
left (the user's screenshot of slots 1-5).

Instead, each icon keeps today's scale (its square divided by the frame's larger side) and
moves so the middle of its drawn pixels (the bounding box of the non-transparent pixels)
is the middle of its square. A frame with no drawn pixels doesn't move. Since the drawn
box fits the square at that scale, nothing drawn leaves the square. This applies to a
single icon, to each icon of a stack (in its own square), to the panel's icon preview and
to the picker's tiles.

`CardLayout` (Core) gets the pure part: `IconBounds(pixels, width, height)` (the box, or
null) and `IconOffset(bounds, width, height, square)` (left, top and scale).

## 3. Add new button

Right-click a slot: **Add new button...**, above Copy, Paste and Delete. It opens a picker
(a flyout like the string Pick...): a search box and a list. The first row is **Blank**:
icon 0, condition Always (0x4282D0), action Move (0x424440), both vars 0, no strings.
After it, every distinct button in the exe's sets and in vanilla's: two buttons are the
same when every field but the slot matches. Each row shows the icon, the tooltip text
(its first line, as the button list names buttons), the action's name, and how many sets
use it; rows are sorted by tooltip text. The search matches the tooltip text, the action's
and the condition's names, and the icon's name or number.

Picking a row adds that button at the slot, as Paste does: on a filled slot it shares the
slot, after the buttons there. One undo step ("Add button"); the new button is selected,
so the panel shows it.

Core: `ButtonTemplates.Build(sets, vanilla)` (Blank first, then the distinct buttons with
their use counts) and `ButtonTemplates.Filter(templates, query, names)`;
`EditorState.AddButton(position, button)`, which is `CardEdits.Paste` with its own label.

## 4. Condition and Action: filter as you type

Each becomes a box (an `AutoSuggestBox`) that shows the current function's label, as the
combo box does now ("Rally Point (4244A0)"). Clicking it selects its text and lists every
function. Typing narrows the list to the functions whose name or address contains the
text, case-insensitive ("rally", "4244"). The arrow keys move through the list; Enter or a
click picks; Esc, or leaving the box without picking, puts the current label back and
changes nothing. An address the table doesn't know stays as it is and is shown as now,
until a pick replaces it.

Core: `FunctionSearch.Filter(table, query, label)`. App: `FunctionBox.cs`, one class for
both boxes, raising `Picked(uint address)`.

## 5. The icon picker

The flyout gets a search box and an **Unused** check box over the grid. The search matches
the icon's number (as typed: "286"; a number matches its frame exactly) or its name in
`Icons.txt` (case-insensitive, contains). Unused keeps only icons no button in the exe's
sets uses (as they stand, unsaved edits included). Frames past `Icons.txt`'s 389 names, a
mod's added icons, show their number only, as now. Custom icons already show: the editor
reads `cmdicons.grp` from the mod exe before the vanilla MPQs.

Core: `IconSearch.Filter(count, names, query, unusedOnly, usedIcons)`. App: the grid moves
out of `MainWindow` into `IconPicker.cs`.

## 6. Repack: two commands

**Repack plugin...** opens a short dialog: the plugin line as today (the path, its size
and build time, "replaces the exe's" or "the same as the exe's"), Change... and Default,
and Repack, enabled when the plugin differs. It runs `Repack.Run` with the plugin and no
files.

**Repack files...** is today's dialog without the plugin: the folder with Change... and
Default, then, new:

- A **filter** box: rows whose path in the MPQ contains the text (case-insensitive) show;
  the others are hidden and keep their ticks.
- A **Select all** box over the rows: it ticks or unticks every row shown, except the two
  with a warning (`rez\stat_txt.tbl`, `Manifold\buttonsets.bin` when they differ from the
  exe's), which are ticked one at a time. It shows ticked, unticked or mixed for the rows
  it acts on.
- A line "N of M ticked", M counting every row, hidden or not.

Repack is enabled when a row is ticked; it runs `Repack.Run` with the ticked files and no
plugin. Both commands keep today's behaviour around the run: unsaved edits are saved first
(asked), the toolbar is off while it runs, failures name the step, the exe is reopened.

Core: `RepackSelection` (rows, filter, ticks: what shows, the Select all state, what a
click on it does, the count).

## 7. The toolbar and its markers

```
Open exe...  Save (Ctrl+S)  Save as...  Undo  Redo | Repack plugin...  Repack files...  Launch (F5) | More ▾
```

**More** holds StarCraft folder... and Re-import FireGraft's sets..., the two commands
used rarely, so the row fits a smaller window.

Markers:
- **Save** shows ● after its text while there are unsaved edits (the title bar's *).
- **Repack plugin** shows ● when the plugin file differs from the exe's plugin; its
  tooltip is the status line's plugin text ("…; GPTP.qdp differs (3,951,104 bytes, newer
  than the exe)").
- **Repack files** shows "(N)", N the files the dialog would tick on opening (new or
  changed, without a warning); none when 0.

The markers are rechecked after opening an exe, after a save or a repack, and when the
window becomes active again (back from building the plugin). The check reads the exe, the
plugin and the folder, so it runs off the UI thread; one at a time, none while a repack
runs, and a check that fails (the exe busy) leaves the markers as they were.

Core: `Repack.Pending(exeBytes, exePath, pluginPath, folder, archive)` returning whether
the plugin differs, whether it is newer than the exe, and the count of files.

## 8. Launch

**Launch (F5)** starts the exe that is open, through the shell (as a double-click does),
from the exe's folder; Windows asks for admin rights if the exe needs them. A refused
prompt is not an error; any other failure shows a message.

Before starting it checks, in order:

1. **Unsaved edits:** "Save the changes to SCManifold.exe before launching?" Save,
   Launch anyway, Cancel. A failed save stops here.
2. **Newer than the exe:** the plugin differs and is newer than the exe, or files are
   pending (§7): "GPTP.qdp is newer than the exe's plugin, and 2 to-repack files are new or
   changed." **Repack first**, Launch anyway, Cancel. Repack first opens Repack plugin...
   and then Repack files... (only those with something pending), as the toolbar would;
   each may be cancelled, and the game starts after them unless a repack failed.

A plugin that differs but is older than the exe asks nothing: packing an older build is a
choice made in the Repack dialog.

Core: `LaunchCheck.Questions(dirty, pending)`: which of the two questions to ask and the
second one's text.

## 9. Save as

**Save as...** picks a new exe (a save picker, `.exe`, named after the open one) and saves
the edits into a copy of the open exe there:

1. The open exe is copied to `<target>.saving`; the button sets (and the strings, when
   changed) are added to the copy, as Save adds them; then the copy becomes the target
   (`File.Replace` keeping `<target>.bak` when the target existed, else a move).
2. Any failure deletes the copy and leaves the target as it was; the edits stay unsaved.
   A busy target gives the busy message.
3. On success the editor works on the new exe: the open document carries on (undo history
   and the saved mark move to it), the title and `LastExe` name it, and the status line is
   recomputed. The original exe is untouched.

The target is refused when it is the open exe (that is Save) or the `.bak` of it. The new
exe inherits none of the original's per-exe settings: its to-repack folder and plugin are
its own folder's defaults, and the first save with changed strings asks again where to
write them. Copying the original's answer would let a test copy's strings overwrite the
real mod's `to-repack\rez\stat_txt.tbl`.

The strings' to-repack copy is not written by Save as itself; it follows the next save's
rules for the new exe.

Core: `EditorSession.SaveAs(source, target, document, open)` around `Save`, with the file
steps behind an interface so the failure paths are tested with a fake; the refusals of the
target. Storm: a writable archive that writes a copy to a new path (`StormArchive.AddFiles`
on the copy).

## 10. The window

- **Remembered:** its size, position and maximized state, the page shown (button sets or
  strings) and the set selected, kept in the settings on closing. Reopened as they were;
  a saved position that no longer lies on any display opens at the default instead. The
  set is selected after the last exe reopens (if it has that set; all exes have 250).
- **The status bar** opens a flyout on a click: every status line in full, one per line,
  as selectable text, with Copy. Its tooltip says "Click for details".

Core: `EditorSettings.Window` (x, y, width, height, maximized), `LastPage`, `LastSetId`;
`WindowPlacement.Restore(saved, workAreas)` (the saved rectangle if it overlaps a display
by at least 100x50 pixels, else null).

## 11. Testing

C# tests on Linux, written before the code (xUnit, `Manifold.Core.Tests`):

- `IconBounds` and `IconOffset`: a centred frame doesn't move; an off-centre one moves by
  half its offset, scaled; an empty frame; a frame filling its canvas.
- `ButtonTemplates`: Blank first with its fields; two buttons that differ only in slot are
  one row, with the use count of both; vanilla buttons the exe no longer has are listed;
  the filter by tooltip, action, condition and icon. `EditorState.AddButton` on an empty
  and a filled slot, one undo step.
- `FunctionSearch`: by name, by address, case, no match, an empty query lists all.
- `IconSearch`: by number (exact), by name, Unused with unsaved edits counted, frames past
  the names.
- `RepackSelection`: the filter; Select all skips warned rows and hidden rows; its three
  states; the count; hidden rows keep their ticks.
- `Repack.Pending`: plugin same, differs newer, differs older, missing; the file count
  leaves out warned and same rows.
- `LaunchCheck`: each combination of dirty and pending; the text.
- `EditorSession.SaveAs`: success moves the saved mark; a failed add deletes the copy and
  keeps the target; the target is the open exe or its `.bak`.
- `EditorSettings`: the window, page and set round-trip; `WindowPlacement.Restore` on,
  partly off and fully off the displays.

The window compiles (`dotnet msbuild -restore -t:Compile -p:WindowsAppSDKSelfContained=false`
in `ManifoldEditor.App`), and the whole test suite passes.

On Windows, in batches as built (§12):

1. **Icons:** slots 1-5 of the user's screenshot look centred; a stack of three too; the
   panel preview and the picker.
2. **Window:** resize and move it, switch to Strings, pick a set, close, reopen: as left.
   Maximized too. The status bar's flyout shows every line; Copy works.
3. **Toolbar:** More holds the two commands; both still work.
4. **Function boxes:** type "rally" in Action: only the rally functions; Enter picks; Esc
   and clicking away keep the old one; Undo takes a pick back.
5. **Icon picker:** "286", "marine", Unused (then use one and reopen: it is gone).
6. **Add new button:** Blank on an empty slot; a template on a filled slot (stacks);
   search "stim"; Undo.
7. **Repack plugin...:** build, then the marker shows; repack; the marker goes; the game
   shows the new build stamp.
8. **Repack files...:** change a to-repack file: the count shows; filter, Select all (the
   warned rows stay as they were), repack.
9. **Launch:** with unsaved edits; with a newer plugin (Repack first, then the game
   starts); with nothing pending.
10. **Save as:** to `SCManifold-test.exe`: the title names it, the original is unchanged
    (its modified time), the test exe runs; Save as onto the open exe is refused.

## 12. Order

1. Centred icons, the status flyout, the remembered window, the toolbar with More.
2. The function boxes, the icon picker, Add new button.
3. The two Repack commands with the filter and Select all, the markers, Launch.
4. Save as.

Each batch is pushed and tested on Windows before the next, so a mistake in one shows
before the others build on it.
