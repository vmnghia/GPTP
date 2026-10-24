# Manifold Editor QoL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The eleven quality-of-life changes of the spec: centred icons, Add new button,
filtering Condition/Action boxes, icon search, Repack plugin / Repack files with a filter and
Select all, toolbar markers, Launch, Save as, a remembered window and a status popup.

**Architecture:** Every rule goes into `Manifold.Core` as a pure function or small class with
xUnit tests; `ManifoldEditor.App` (WinUI 3 in C#, no `.xaml`) only wires controls to them.
New window pieces get their own files instead of growing `MainWindow.cs` (1,300 lines).

**Tech Stack:** C# 12 / .NET 8, WinUI 3 (Windows App SDK), xUnit, StormLib through
`Manifold.Storm`.

**Spec:** `docs/superpowers/specs/2026-10-24-editor-qol-design.md`

## Global Constraints

- All paths below are under `tools/ManifoldEditor/`.
- Core tests: `dotnet test Manifold.Core.Tests` (155 pass, 2 skipped before this plan; the
  count only grows). Install with `apt-get install -y dotnet-sdk-8.0` if `dotnet` is missing.
- App compile check, from `ManifoldEditor.App/`:
  `dotnet msbuild -restore -t:Compile -p:WindowsAppSDKSelfContained=false 2>&1 | grep -c "error CS"`
  must print `0`. No `.xaml` files.
- Storm compile check: `dotnet build Manifold.Storm -p:EnableWindowsTargeting=true`.
- Blank button: icon 0, condition Always `0x4282D0`, action Move `0x424440`, vars 0, strings 0.
- Commits end with the two attribution lines from the session's system reminder; no model ids.
- Batches (spec §12): Tasks 1-4, 5-7, 8-10, 11-12. After each batch, run every check above,
  push to `claude/ecstatic-darwin-ztohwv` (fetch first; never force-push), and give the user
  that batch's Windows steps from spec §11.

## Review Focus

- A Save as target the save picker already created as a 0-byte file: treated as a new file
  (no `.bak` of an empty file; deleted again on failure). Test in Task 11.
- A Save as onto an existing, non-empty exe whose save fails: the target is byte-for-byte
  unchanged and no `.saving` is left. Test in Task 11.
- A filter in Repack files that hides a ticked warned row: the count still counts it and
  Select all doesn't touch it. Test in Task 8.
- The markers' recheck while the game holds the exe (right after Launch): `Pending` throws,
  and the window keeps the old markers. `Pending` itself must not throw for a missing plugin
  or folder. Test in Task 8.
- A remembered window on a display that is gone, or a query of only spaces in a search box:
  the default placement / the full list. Tests in Tasks 3, 5 and 6.

---

## Batch 1

### Task 1: Centring maths

**Files:**
- Modify: `Manifold.Core/Editor/CardLayout.cs`
- Test: `Manifold.Core.Tests/CardIconTests.cs` (new)

**Interfaces:**
- Produces:
  - `public readonly record struct IconBox(int X, int Y, int Width, int Height);`
  - `public static IconBox? CardLayout.IconBounds(uint[] argb, int width, int height)`:
    the box of pixels whose alpha byte is non-zero; null when none.
  - `public static (double Left, double Top, double Scale) CardLayout.IconOffset(IconBox? box, int width, int height, double square)`:
    `Scale = square / Math.Max(width, height)`; with a box, `Left = square / 2 - (box.X + box.Width / 2.0) * Scale`
    (Top likewise with Y and Height); without, the frame is centred
    (`Left = (square - width * Scale) / 2`, Top likewise).

- [ ] **Step 1: Write the failing tests**

```csharp
public class CardIconTests
{
    static uint[] Frame(int w, int h, IconBox drawn)
    {
        var px = new uint[w * h];
        for (int y = drawn.Y; y < drawn.Y + drawn.Height; y++)
            for (int x = drawn.X; x < drawn.X + drawn.Width; x++) px[y * w + x] = 0xFF00FF00;
        return px;
    }

    [Fact]
    public void Bounds_of_the_drawn_pixels()
    {
        Assert.Equal(new IconBox(3, 2, 20, 18), CardLayout.IconBounds(Frame(36, 34, new(3, 2, 20, 18)), 36, 34));
        Assert.Null(CardLayout.IconBounds(new uint[36 * 34], 36, 34));
    }

    [Fact]
    public void A_full_frame_sits_where_uniform_scaling_put_it()
    {
        var (left, top, scale) = CardLayout.IconOffset(new IconBox(0, 0, 36, 34), 36, 34, 52);
        Assert.Equal(52.0 / 36, scale, 6);
        Assert.Equal(0, left, 6);
        Assert.Equal((52 - 34 * scale) / 2, top, 6);
    }

    [Fact]
    public void An_off_centre_icon_moves_to_the_middle()
    {
        var (left, top, scale) = CardLayout.IconOffset(new IconBox(0, 0, 20, 20), 36, 34, 52);
        Assert.Equal(26 - 10 * scale, left, 6);
        Assert.Equal(26 - 10 * scale, top, 6);
    }

    [Fact]
    public void An_empty_frame_is_centred_as_a_whole()
    {
        var (left, top, scale) = CardLayout.IconOffset(null, 36, 34, 52);
        Assert.Equal(0, left, 6);
        Assert.Equal((52 - 34 * scale) / 2, top, 6);
    }
}
```

- [ ] **Step 2: Run `dotnet test Manifold.Core.Tests --filter CardIconTests`** — FAIL (no `IconBox`).
- [ ] **Step 3: Implement `IconBox`, `IconBounds`, `IconOffset` in `CardLayout.cs`** as the Interfaces block says.
- [ ] **Step 4: Run the filter again** — 4 PASS; then the whole suite passes.
- [ ] **Step 5: Commit** `Manifold Editor: centre an icon on its drawn pixels (maths)`.

### Task 2: Centred icons in the window

**Files:**
- Modify: `ManifoldEditor.App/Services/IconCache.cs`, `ManifoldEditor.App/MainWindow.cs`
  (`CellView`, `FillPanel`, `FillIconGrid`)

**Interfaces:**
- Consumes: Task 1's `IconBounds`, `IconOffset`.
- Produces: `public FrameworkElement? IconCache.View(int frame, double square)`: a
  `Canvas` of `square` x `square` holding the frame's `Image` (Stretch `Fill`, size
  `width * Scale` x `height * Scale`) at (`Left`, `Top`); null when `Get(frame)` is null.
  Bounds are computed once per frame, from `resources.IconPixels(frame)`, and cached with
  the bitmap.

- [ ] **Step 1: Add `View` to `IconCache`.**
- [ ] **Step 2: Use it** in `CellView` (each stack square: `View(frame, slot.Size)` as the
  `Border`'s child instead of the `Image`), in `FillPanel` (`View(b.Icon, 36)`) and in
  `FillIconGrid` (`View(frame, 36)`). The `#frame` text fallback stays for a null view.
- [ ] **Step 3: Compile check** (Global Constraints) prints `0`.
- [ ] **Step 4: Commit** `Manifold Editor: icons centred in the card, the panel and the picker`.

### Task 3: Window settings

**Files:**
- Modify: `Manifold.Core/Editor/EditorSettings.cs`
- Create: `Manifold.Core/Editor/WindowPlacement.cs`
- Test: `Manifold.Core.Tests/WindowSettingsTests.cs` (new)

**Interfaces:**
- Produces:
  - `public readonly record struct WindowRect(int X, int Y, int Width, int Height);`
  - On `EditorSettings`: `public WindowRect? Window { get; init; }`,
    `public bool WindowMaximized { get; init; }`, `public string? LastPage { get; init; }`
    (`"strings"` or null for the button sets), `public int? LastSetId { get; init; }`.
  - `public static WindowRect? WindowPlacement.Restore(WindowRect? saved, IReadOnlyList<WindowRect> workAreas)`:
    `saved` when its overlap with some work area is at least 100 wide and 50 high, else null.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Window_page_and_set_round_trip()   // Save to a temp file, Load back
{
    var s = new EditorSettings { Window = new WindowRect(10, 20, 1200, 800), WindowMaximized = true, LastPage = "strings", LastSetId = 131 };
    // save, load: all four equal; a settings file without them loads with Window null, LastSetId null
}

[Fact]
public void A_window_on_a_display_restores()
    => Assert.Equal(new WindowRect(100, 100, 800, 600),
        WindowPlacement.Restore(new(100, 100, 800, 600), [new(0, 0, 1920, 1040)]));

[Fact]
public void A_window_mostly_off_but_grabbable_restores()
    => Assert.NotNull(WindowPlacement.Restore(new(1800, 900, 800, 600), [new(0, 0, 1920, 1040)])); // 120 x 140 on

[Fact]
public void A_window_on_a_display_that_is_gone_does_not()
{
    Assert.Null(WindowPlacement.Restore(new(2000, 100, 800, 600), [new(0, 0, 1920, 1040)]));
    Assert.Null(WindowPlacement.Restore(new(1850, 100, 800, 600), [new(0, 0, 1920, 1040)])); // 70 wide on
    Assert.Null(WindowPlacement.Restore(null, [new(0, 0, 1920, 1040)]));
}
```

- [ ] **Step 2: Run `--filter WindowSettingsTests`** — FAIL.
- [ ] **Step 3: Implement** the properties and `Restore`.
- [ ] **Step 4: Run** — PASS; the whole suite passes.
- [ ] **Step 5: Commit** `Manifold Editor: window placement, page and set in the settings`.

### Task 4: Remembered window, status popup, toolbar with More

**Files:**
- Modify: `ManifoldEditor.App/MainWindow.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces (used by Tasks 10-11): fields `Button saveButton`, `Button saveAsButton` (added in
  Task 11), `Button repackPluginButton`, `Button repackFilesButton`, `Button launchButton`
  (added in Task 10), and the toolbar order of spec §7. In this task the two Repack entries
  are still the one `Repack...` button; Task 9 splits it.

- [ ] **Step 1: Remembered window.**
  - In the constructor, replace the fixed `Resize(1400, 860)`:
    `WindowPlacement.Restore(settings.Window, DisplayArea.FindAll().Select(d => new WindowRect(d.WorkArea.X, d.WorkArea.Y, d.WorkArea.Width, d.WorkArea.Height)).ToList())`
    gives `MoveAndResize`, else `Resize(1400, 860)`; then, when `settings.WindowMaximized`,
    `((OverlappedPresenter)AppWindow.Presenter).Maximize()`.
  - Track the restored bounds in `AppWindow.Changed` (when the presenter state is
    `Restored` and the position or size changed), so a maximized window remembers the
    bounds it had before.
  - At the start of `OnClosing`, save `Window`, `WindowMaximized`, `LastPage`
    (`"strings"` when the Strings page shows) and `LastSetId` (when an exe is open) into
    `settings` and write them, whether or not the close goes on.
  - After the last exe reopens in the `Loaded` handler: select `LastSetId` (when in
    0-249) and the page `LastPage` names.
- [ ] **Step 2: Status popup.** `status.Tapped` shows a `Flyout` at `status` holding a
  read-only, wrapping, selectable `TextBox` (Consolas 12, `MaxHeight` 360) with every line
  of `opened.Status` (or the no-exe text), and a **Copy** button that puts that text on the
  clipboard (`DataPackage.SetText`, `Clipboard.SetContent`). The tooltip becomes
  "Click for details".
- [ ] **Step 3: Toolbar.** Order: Open exe..., Save (Ctrl+S), Undo (Ctrl+Z), Redo (Ctrl+Y),
  a gap (`Margin` left 12), Repack..., a gap, `DropDownButton` **More** whose `MenuFlyout`
  holds StarCraft folder... and Re-import FireGraft's sets.... Keep `saveButton` and the
  repack button in fields. The repack-time disabling (`toolbar.Children.OfType<Control>()`)
  must still reach every button, More included.
- [ ] **Step 4: Compile check** prints `0`.
- [ ] **Step 5: Commit** `Manifold Editor: remembered window, status details, More menu`.
- [ ] **Step 6: Batch 1 end:** whole suite, compile checks, fetch, push; hand the user spec
  §11 steps 1-3.

## Batch 2

### Task 5: Condition and Action filter as you type

**Files:**
- Create: `Manifold.Core/Editor/FunctionSearch.cs`, `ManifoldEditor.App/FunctionBox.cs`
- Modify: `ManifoldEditor.App/MainWindow.cs` (the two `ComboBox`es, `FillFunctions`, `Pick`,
  `CardKeysApply` already counts `AutoSuggestBox`)
- Test: `Manifold.Core.Tests/FunctionSearchTests.cs` (new)

**Interfaces:**
- Produces:
  - `public static IReadOnlyList<GameFunction> FunctionSearch.Filter(FunctionTable table, string query)`:
    trimmed query; empty gives every function; else functions whose name contains it
    (ordinal, ignore case) or whose address in hex (`Address.ToString("X")`) contains it
    after a leading `0x`/`0X` is removed. Table order kept.
  - `sealed class FunctionBox` with `public AutoSuggestBox Root`,
    `public event Action<uint>? Picked`,
    `public void Show(FunctionTable table, uint address, Func<uint, string> label)`,
    `public void Clear()`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Empty_or_blank_lists_all()
{
    var t = Fixtures.Actions();
    Assert.Equal(t.Functions.Count, FunctionSearch.Filter(t, "").Count);
    Assert.Equal(t.Functions.Count, FunctionSearch.Filter(t, "   ").Count);
}
[Fact] public void By_name_any_case()
    => Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), "RALLY").Select(f => f.Name));
[Fact] public void By_address_with_or_without_0x()
{
    Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), "4244a0").Select(f => f.Name));
    Assert.Equal(["Rally Point"], FunctionSearch.Filter(Fixtures.Actions(), "0x4244A0").Select(f => f.Name));
}
[Fact] public void No_match_is_empty() => Assert.Empty(FunctionSearch.Filter(Fixtures.Actions(), "zzz"));
```

- [ ] **Step 2: Run `--filter FunctionSearchTests`** — FAIL.
- [ ] **Step 3: Implement `FunctionSearch.Filter`.** — PASS, suite passes.
- [ ] **Step 4: Write `FunctionBox`.** Behaviour (spec §4): `Show` sets the label as `Text`
  and remembers it; `GotFocus` selects all text and lists every function
  (`ItemsSource` = labels, `IsSuggestionListOpen` true); `TextChanged` with reason
  `UserInput` filters through `FunctionSearch`; `QuerySubmitted` with a chosen suggestion,
  or else the only match, raises `Picked(address)`; Esc (`KeyDown`, handled) and `LostFocus`
  without a pick restore the remembered label and close the list. No pick changes nothing.
  `Header` "Condition" / "Action", stretched.
- [ ] **Step 5: Wire it** in place of `conditionBox` / `actionBox`: `Picked` calls
  `EditSelected(b => b with { Condition = address })` (Action likewise); `FillPanel` calls
  `Show(...)` with `state.ConditionLabel` / `state.ActionLabel`, and `Clear()` with no
  button. Remove `FillFunctions` and `Pick`.
- [ ] **Step 6: Compile check** prints `0`.
- [ ] **Step 7: Commit** `Manifold Editor: Condition and Action filter as you type`.

### Task 6: Icon picker with search and Unused

**Files:**
- Create: `Manifold.Core/Editor/IconSearch.cs`, `ManifoldEditor.App/IconPicker.cs`
- Modify: `ManifoldEditor.App/MainWindow.cs` (remove `iconGrid`, `FillIconGrid`, `OnIconPicked`)
- Test: `Manifold.Core.Tests/IconSearchTests.cs` (new)

**Interfaces:**
- Consumes: Task 2's `IconCache.View`.
- Produces:
  - `public static IReadOnlySet<int> IconSearch.Used(IEnumerable<ButtonSet> sets)`.
  - `public static IReadOnlyList<int> IconSearch.Filter(int count, IReadOnlyList<string> names, string query, bool unusedOnly, IReadOnlySet<int> used)`:
    frames 0..count-1 in order; a trimmed query of digits only matches its frame exactly;
    any other non-empty query matches names containing it (ignore case); frames past
    `names` have no name. `unusedOnly` drops frames in `used`.
  - `sealed class IconPicker(IconCache icons, IReadOnlyList<string> names)` with
    `public Flyout Flyout`, `public event Action<int>? Picked`,
    `public Func<IReadOnlySet<int>> Used { get; init; }` (asked on each opening).

- [ ] **Step 1: Write the failing tests**

```csharp
static readonly string[] Names = ["Marine", "Ghost", "Vulture", "Goliath"];
[Fact] public void By_number_exactly()
    => Assert.Equal([2], IconSearch.Filter(6, Names, " 2 ", false, new HashSet<int>()));
[Fact] public void By_name_any_case()
    => Assert.Equal([0], IconSearch.Filter(6, Names, "mar", false, new HashSet<int>()));
[Fact] public void Frames_past_the_names_only_by_number()
{
    Assert.Equal([5], IconSearch.Filter(6, Names, "5", false, new HashSet<int>()));
    Assert.Equal([0, 1, 2, 3, 4, 5], IconSearch.Filter(6, Names, "", false, new HashSet<int>()));
}
[Fact] public void Unused_counts_unsaved_edits()   // Used() over the document's current sets
{
    var sets = Make.EmptySets();
    sets[0] = Make.Set(Make.Button(1, icon: 1), Make.Button(2, icon: 3));
    Assert.Equal([0, 2, 4, 5], IconSearch.Filter(6, Names, "", true, IconSearch.Used(sets)));
}
```

- [ ] **Step 2: Run `--filter IconSearchTests`** — FAIL.
- [ ] **Step 3: Implement `IconSearch`.** — PASS, suite passes.
- [ ] **Step 4: Write `IconPicker`:** a `Flyout` with a `TextBox` ("Search icons: name or
  number"), a `CheckBox` "Unused", and the existing `GridView` (560 x 420, tiles as
  `FillIconGrid` made them, built once and kept); filtering shows or collapses tiles
  (not rebuilding them). An item click raises `Picked(frame)` and hides the flyout.
- [ ] **Step 5: Wire it:** `iconButton.Flyout = picker.Flyout`; `Used = () => IconSearch.Used(opened!.State.Document.Sets)`;
  `Picked` edits the icon. A new picker is made when an exe opens (its icons differ).
- [ ] **Step 6: Compile check** prints `0`.
- [ ] **Step 7: Commit** `Manifold Editor: icon search and the Unused filter`.

### Task 7: Add new button

**Files:**
- Create: `Manifold.Core/Editor/ButtonTemplates.cs`, `ManifoldEditor.App/ButtonPicker.cs`
- Modify: `Manifold.Core/Editor/EditorState.cs`, `ManifoldEditor.App/MainWindow.cs` (`CellView`'s menu)
- Test: `Manifold.Core.Tests/ButtonTemplateTests.cs` (new)

**Interfaces:**
- Produces:
  - `public sealed record ButtonTemplate(Button Button, string Name, int Uses);` (`Button.Position` 0)
  - `public static class ButtonTemplates` with
    `public static readonly Button Blank = new(0, 0, 0x4282D0, 0x424440, 0, 0, 0, 0);`,
    `public static IReadOnlyList<ButtonTemplate> Build(IReadOnlyList<ButtonSet> sets, IReadOnlyList<ButtonSet> vanilla, Func<ushort, string?> text)`
    and `public static IReadOnlyList<ButtonTemplate> Filter(IReadOnlyList<ButtonTemplate> all, string query, FunctionTable conditions, FunctionTable actions, IReadOnlyList<string> iconNames)`.
  - Build: first `new ButtonTemplate(Blank, "Blank", 0)`; then every button of `sets` and
    `vanilla` with `Position = 0`, distinct, Blank's equal left out; `Name` is
    `ButtonText.Display(text(EnabledString))`, or `$"(no tooltip) icon {Icon}"` when that is
    empty; `Uses` counts the sets in `sets` (not vanilla) holding it at least once; sorted by
    `Name` (ordinal, ignore case), then `Action`, then `Icon`.
  - Filter: Blank always first; an empty trimmed query keeps all; else a row matches when
    its name, its action's or condition's name, or its icon's name (when `Icon < iconNames.Count`)
    contains the query (ignore case), or the query is digits and equals its icon number.
  - `public bool EditorState.AddButton(ushort position, Button button)`: `IsPosition` check,
    then `Apply(s => CardEdits.Paste(s, button with { Position = position }, position), "Add button")`.
  - `sealed class ButtonPicker` with `public Flyout Flyout`,
    `public event Action<Button>? Picked`,
    `public void Fill(IReadOnlyList<ButtonTemplate> templates, Func<string, IReadOnlyList<ButtonTemplate>> filter, IconCache icons)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Blank_comes_first_with_its_fields()
{
    var t = ButtonTemplates.Build(Make.EmptySets(), Make.EmptySets(), _ => null);
    var blank = Assert.Single(t);
    Assert.Equal(("Blank", 0, (ushort)0, 0x4282D0u, 0x424440u), (blank.Name, blank.Uses, blank.Button.Icon, blank.Button.Condition, blank.Button.Action));
}
[Fact] public void Same_button_in_two_slots_is_one_row_counting_sets()
{
    var sets = Make.EmptySets();
    sets[0] = Make.Set(Make.Button(1, icon: 7, enabledString: 5), Make.Button(4, icon: 7, enabledString: 5));
    sets[1] = Make.Set(Make.Button(9, icon: 7, enabledString: 5));
    var t = ButtonTemplates.Build(sets, Make.EmptySets(), id => id == 5 ? "SStim Packs" : null);
    var row = t[1];
    Assert.Equal(2, t.Count);
    Assert.Equal(2, row.Uses);
    Assert.Equal(0, row.Button.Position);
}
[Fact] public void Vanilla_only_buttons_are_listed_with_no_uses()
{
    var vanilla = Make.EmptySets();
    vanilla[7] = Make.Set(Make.Button(1, icon: 11));
    var t = ButtonTemplates.Build(Make.EmptySets(), vanilla, _ => null);
    Assert.Equal(0, t[1].Uses);
    Assert.Equal("(no tooltip) icon 11", t[1].Name);
}
[Fact] public void Filter_by_name_action_and_icon_keeps_blank_first()
{
    // build rows for "SStim Packs" (icon 2, action Move) and "RRally" (icon 3, action Rally Point 0x4244A0)
    // "stim" -> [Blank, Stim Packs]; "rally point" -> [Blank, Rally]; iconNames[3] = "Wraith": "wraith" -> [Blank, Rally]; "3" -> [Blank, Rally]
}
[Fact] public void AddButton_on_empty_and_filled_slots_is_one_undo_step_each()
{
    // State with set 0 = [Button at 1]; AddButton(2, Blank) -> button at 2, last index; AddButton(1, Blank) -> two at 1, the new one last;
    // Undo once -> only the first AddButton remains
}
```

- [ ] **Step 2: Run `--filter ButtonTemplateTests`** — FAIL.
- [ ] **Step 3: Implement `ButtonTemplates` and `AddButton`.** — PASS, suite passes.
- [ ] **Step 4: Write `ButtonPicker`:** a `Flyout` with a search `TextBox` and a `ListView`
  (Height 400, Width 520), rows: the icon (`icons.View(icon, 28)`), the name, then smaller
  "action name · in N sets" ("vanilla only" when `Uses` is 0). A click raises `Picked` and
  hides.
- [ ] **Step 5: Wire it:** `CellView`'s menu gets **Add new button...** first, then a
  separator. It shows the picker at the cell (`Flyout.ShowAt(border)`), filled with
  `ButtonTemplates.Build(state.Document.Sets, state.Document.Vanilla, state.Text)` and a
  filter over `state.Conditions`, `state.Actions` and `iconNames`. `Picked` runs
  `CommitFields()`, `state.AddButton(cell.Position, b)`, then selects the new button
  (`selectedPosition = cell.Position`, `selectedButton = Buttons.Count - 1`) and
  `Refresh()`.
- [ ] **Step 6: Compile check** prints `0`.
- [ ] **Step 7: Commit** `Manifold Editor: Add new button from a template`.
- [ ] **Step 8: Batch 2 end:** checks, push, spec §11 steps 4-6.

## Batch 3

### Task 8: Repack selection, pending check, launch questions

**Files:**
- Create: `Manifold.Core/Model/RepackSelection.cs`, `Manifold.Core/Model/LaunchCheck.cs`
- Modify: `Manifold.Core/Model/Repack.cs`
- Test: `Manifold.Core.Tests/RepackSelectionTests.cs`, `Manifold.Core.Tests/RepackPendingTests.cs`,
  `Manifold.Core.Tests/LaunchCheckTests.cs` (new)

**Interfaces:**
- Produces:
  - `public sealed class RepackSelection(IReadOnlyList<RepackFile> files)`:
    `public IReadOnlyList<RepackFile> Files`; `public string Filter { get; set; }` ("");
    `public bool IsTicked(int index)`; `public void SetTicked(int index, bool ticked)`;
    `public IReadOnlyList<int> Visible` (indexes whose `MpqPath` contains the trimmed
    filter, ignore case); `public bool CanSelectAll` (some visible row without a warning);
    `public bool? SelectAllState` (over visible rows without a warning: true all ticked,
    false none, null mixed; false when there are none); `public void ClickSelectAll()`
    (state true: untick them, else tick them); `public string CountText`
    (`$"{ticked} of {Files.Count} ticked"`); `public IReadOnlyList<RepackFile> Ticked`.
    Starts with each row's `TickedByDefault`.
  - `public sealed record RepackPending(bool PluginDiffers, bool PluginNewer, int Files, string PluginLine);`
  - `public static RepackPending Repack.Pending(byte[] exe, string exePath, string pluginPath, string folder, IArchive archive)`:
    `PluginLine` is `PluginStatus(exe, exePath, pluginPath)`; `PluginDiffers` when the exe
    parses with a layout and the plugin file reads and differs from the exe's;
    `PluginNewer` when it differs and its write time is later than the exe's; `Files` is
    `Scan(folder, archive).Count(f => f.TickedByDefault)`. A missing plugin or folder is
    not an error; a busy exe or folder throws (the caller keeps its markers).
  - `public sealed record LaunchQuestions(bool AskSave, string? RepackText);`
  - `public static LaunchQuestions LaunchCheck.Questions(bool dirty, RepackPending? pending, string pluginName)`:
    `RepackText` null unless `pending` has `PluginNewer` or `Files > 0`; the texts are
    `"{pluginName} is newer than the exe's plugin."`, `"{n} to-repack file is new or changed."`
    / `"{n} to-repack files are new or changed."`, and both together
    `"{pluginName} is newer than the exe's plugin, and {n} to-repack file(s) {is/are} new or changed."`.
  - Make `Repack.IsBusy(Exception)` `internal` (Task 11 uses it).

- [ ] **Step 1: Write the failing tests**

```csharp
// RepackSelectionTests: files a (new), b (changed), c (same), rez\stat_txt.tbl (changed, warned)
[Fact] public void Starts_with_the_default_ticks()          // a, b ticked; CountText "2 of 4 ticked"
[Fact] public void Filter_hides_rows_and_keeps_their_ticks() // Filter "REZ" -> Visible [3]; a still ticked
[Fact] public void Select_all_skips_warned_and_hidden_rows()
{
    // Filter "" ; ClickSelectAll (state null: a,b ticked, c not) -> a,b,c ticked, warned row unticked
    // SetTicked(warned, true); Filter "stat"; CanSelectAll false; CountText "4 of 4 ticked" (hidden ones count)
}
[Fact] public void Select_all_state_has_three_values()      // none ticked -> false; some -> null; all -> true; ClickSelectAll at true unticks

// RepackPendingTests (temp dir, Make.FireGraftExe(Make.Dll()), FakeArchive)
[Fact] public void Same_plugin_and_no_folder_is_nothing_pending()   // (false, false, 0)
[Fact] public void A_newer_different_plugin()   // plugin file = Make.Dll(extra: 512), write time exe + 1 min -> (true, true, _)
[Fact] public void An_older_different_plugin()  // write time exe - 1 min -> (true, false, _)
[Fact] public void A_missing_plugin_is_not_pending_and_does_not_throw()
[Fact] public void Files_count_leaves_out_same_and_warned()   // folder: rez\a.bin new, rez\stat_txt.tbl differing -> Files == 1

// LaunchCheckTests
[Fact] public void Nothing_to_ask()               // Questions(false, (false,false,0,""), "GPTP.qdp") == (false, null)
[Fact] public void Dirty_asks_save()              // AskSave true
[Fact] public void Plugin_and_files_texts()       // exact strings above for: newer only; 1 file; 2 files; newer + 2 files
[Fact] public void An_older_plugin_asks_nothing() // (true, false, 0, "") -> RepackText null
[Fact] public void No_pending_check_asks_only_save() // pending null
```

- [ ] **Step 2: Run the three filters** — FAIL.
- [ ] **Step 3: Implement** the three pieces.
- [ ] **Step 4: Run** — PASS; whole suite passes.
- [ ] **Step 5: Commit** `Manifold Editor: Repack's selection, what is pending, Launch's questions`.

### Task 9: Repack plugin... and Repack files...

**Files:**
- Modify: `ManifoldEditor.App/RepackDialog.cs`, `ManifoldEditor.App/MainWindow.cs` (`RepackExe`, toolbar)

**Interfaces:**
- Consumes: Task 8's `RepackSelection`.
- Produces:
  - `enum RepackKind { Plugin, Files }`; `RepackDialog` takes a `RepackKind kind` after
    `exe`; `ShowAsync` returns `RepackChoice` with no files for `Plugin` and a null plugin
    for `Files`.
  - `async Task<bool?> MainWindow.RepackExe(RepackKind kind)`: true when it repacked,
    false when it failed (after showing the message), null when cancelled (the unsaved-edits
    question or the dialog).

- [ ] **Step 1: Split the dialog.** `Plugin`: title "Repack the plugin into {name}", the
  plugin section only; Repack enabled when `plugin` is set. `Files`: title "Repack files
  into {name}", the folder section, then a filter `TextBox` ("Filter by path"), a
  `CheckBox` "Select all" (`IsThreeState`, bound to `SelectAllState`, disabled when
  `!CanSelectAll`; a click calls `ClickSelectAll()`), the `CountText` line, then one
  `CheckBox` per `Visible` row (hidden rows are collapsed, not removed); a row's click calls
  `SetTicked`. Repack enabled when `Ticked.Count > 0`. Changing the folder rebuilds the
  `RepackSelection`.
- [ ] **Step 2: Toolbar:** replace Repack... with **Repack plugin...** and **Repack files...**
  (`repackPluginButton`, `repackFilesButton`), each calling `RepackExe(kind)`. The "Repacked"
  message names only what that kind packed.
- [ ] **Step 3: Compile check** prints `0`.
- [ ] **Step 4: Commit** `Manifold Editor: Repack plugin and Repack files, with a filter and Select all`.

### Task 10: Markers and Launch

**Files:**
- Modify: `ManifoldEditor.App/MainWindow.cs`, `ManifoldEditor.App/Services/Workspace.cs`

**Interfaces:**
- Consumes: Task 8 (`Repack.Pending`, `LaunchCheck.Questions`), Task 9 (`RepackExe(kind)`).
- Produces: `OpenedExe` gains `string PluginLine` (the plugin status line moves out of
  `Status`); the status bar shows `[PluginLine, ..Status]`. `async Task CheckPending()`;
  `RepackPending? pending` field.

- [ ] **Step 1: Plugin line out of `Status`.** `Workspace.Open` puts
  `Repack.PluginStatus(...)` in `PluginLine` instead of the status list.
- [ ] **Step 2: `CheckPending()`:** unless `opened` is null, a repack runs, or a check is
  already running: `Task.Run(() => Repack.Pending(File.ReadAllBytes(exe), exe, settings.PluginPathFor(exe), settings.RepackFolderFor(exe), new StormArchive(exe)))`;
  any exception keeps the old `pending`; on success set `pending`, replace
  `opened.PluginLine` with `pending.PluginLine`, then `UpdateMarkers()`. Called at the end
  of `OpenExe`, after a successful `Save`, and from `Activated` when the state is not
  `Deactivated`.
- [ ] **Step 3: `UpdateMarkers()`** (also called from `Refresh`): `saveButton.Content` is
  "Save (Ctrl+S) ●" when `Document.IsDirty`, else "Save (Ctrl+S)"; `repackPluginButton`
  "Repack plugin... ●" when `pending?.PluginDiffers`, tooltip `PluginLine`;
  `repackFilesButton` "Repack files... (N)" when `pending?.Files > 0`.
- [ ] **Step 4: Launch.** Button **Launch (F5)** after Repack files..., and an F5
  accelerator with no modifiers (`VirtualKeyModifiers.None`). `async Task Launch()`:
  `CommitFields()`; `await CheckPending()`; `var q = LaunchCheck.Questions(Document.IsDirty, pending, Path.GetFileName(settings.PluginPathFor(exe)))`.
  - `q.AskSave`: dialog "Unsaved changes" — "Save the changes to {name} before launching?";
    Save (stop if `Save()` is false) / Launch anyway / Cancel (stop).
  - `q.RepackText`: dialog "Newer than the exe" — the text; Repack first / Launch anyway /
    Cancel (stop). Repack first runs `RepackExe(Plugin)` when `pending.PluginNewer`, then
    `RepackExe(Files)` when `pending.Files > 0`; a `false` (a failure) stops; a `null`
    (cancelled) goes on to the next and then launches.
  - Start: `Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })`;
    `Win32Exception` with `NativeErrorCode == 1223` (the admin prompt refused) is silent,
    any other exception shows "Can't launch" with its message.
- [ ] **Step 5: Compile check** prints `0`.
- [ ] **Step 6: Commit** `Manifold Editor: toolbar markers and Launch (F5)`.
- [ ] **Step 7: Batch 3 end:** checks, push, spec §11 steps 7-9.

## Batch 4

### Task 11: Save as

**Files:**
- Modify: `Manifold.Core/Model/EditorSession.cs`, `Manifold.Core/Editor/EditorState.cs`
  (`ExeName` gets a setter), `ManifoldEditor.App/MainWindow.cs`
- Test: `Manifold.Core.Tests/SaveAsTests.cs` (new)

**Interfaces:**
- Consumes: `IRepackHost.AddFiles`, `Repack.IsBusy` (Task 8).
- Produces:
  - `public static string? EditorSession.SaveAsProblem(string source, string target)`:
    "That is the open exe: use Save." when the full paths match (ignore case);
    "That is the open exe's backup." for `source + ".bak"`; else null.
  - `public static string? EditorSession.SaveAs(string source, string target, ButtonSetDocument document, IRepackHost host, out byte[]? stringsWritten)`:
    the refusal, else: the files Save would write (extract the list-building and the
    strings' check from `Save` into one private helper both use); `existed` = the target
    exists with length > 0; copy `source` to `target + ".saving"`;
    `host.AddFiles(work, files)`; then `File.Replace(work, target, target + ".bak")` when
    `existed`, else `File.Move(work, target, overwrite: true)`; `document.MarkSaved()`.
    Failures return "{target file} is in use by another program. Nothing was saved." when
    `Repack.IsBusy`, else "Saving {target file}: {message}"; `stringsWritten` null. The
    `.saving` copy is always deleted (best effort); on failure a target that didn't exist
    (or was empty) is deleted too.

- [ ] **Step 1: Write the failing tests** (temp dir; `source` holds `[1, 2, 3]`;
  `FakeRepackHost` from `RepackRunTests.cs`; `doc = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets())`
  with `doc.Apply(5, _ => Make.Set(Make.Button(1)), "Edit")` so it is dirty)

```csharp
[Fact] public void Saves_into_a_new_exe_and_moves_the_saved_mark()
{
    Assert.Null(EditorSession.SaveAs(source, target, doc, host, out _));
    Assert.Equal(target + ".saving", Assert.Single(host.Adds).File);
    Assert.Contains(host.Adds[0].Files, f => f.Path == ButtonSetFile.ArchivePath);
    Assert.Equal([1, 2, 3], File.ReadAllBytes(target));   // the fake adds nothing to the bytes
    Assert.False(doc.IsDirty);
    Assert.False(File.Exists(target + ".bak"));
    Assert.False(File.Exists(target + ".saving"));
    Assert.Equal([1, 2, 3], File.ReadAllBytes(source));
}
[Fact] public void An_existing_target_is_kept_as_bak()            // target held [9]; after: .bak == [9]
[Fact] public void An_empty_target_from_the_picker_counts_as_new() // target 0 bytes; after: no .bak
[Fact] public void A_failed_add_leaves_an_existing_target_as_it_was()
{
    // target [9], host.FailAdd = true: result starts with "Saving ", target == [9], no .saving, doc still dirty
}
[Fact] public void A_failed_add_removes_a_new_or_empty_target()   // 0-byte target, FailAdd: target gone
[Fact] public void The_open_exe_and_its_bak_are_refused()
{
    Assert.Equal("That is the open exe: use Save.", EditorSession.SaveAsProblem(source, source.ToUpperInvariant()));
    Assert.Equal("That is the open exe's backup.", EditorSession.SaveAsProblem(source, source + ".bak"));
}
```

- [ ] **Step 2: Run `--filter SaveAsTests`** — FAIL.
- [ ] **Step 3: Implement** `SaveAsProblem`, the shared helper, `SaveAs`. Existing
  `SessionTests` / `StringSaveTests` must still pass unchanged.
- [ ] **Step 4: Run** — PASS; whole suite passes.
- [ ] **Step 5: Window.** Toolbar **Save as...** after Save (`saveAsButton`). `async Task SaveAs()`:
  refuse while repacking (as Save); `CommitFields()` must pass (as Save); a
  `FileSavePicker` (`FileTypeChoices` "Executable" `.exe`, suggested name
  `<exe name without extension>-copy`); `SaveAsProblem` shown as "Not saved"; then
  `await Task.Run(() => EditorSession.SaveAs(..., new StormRepackHost(), out strings))`.
  On success: `opened = opened with { ExePath = target, Archive = new StormArchive(target) }`,
  `state.ExeName = Path.GetFileName(target)`, `settings = settings with { LastExe = target }`
  saved, `unlockedStrings` kept, then `WriteRepackStrings(strings)` when strings were
  written (it asks, as for any exe without an answer), `Refresh()`, `CheckPending()`.
- [ ] **Step 6: Compile checks** (App and Storm) print `0` errors.
- [ ] **Step 7: Commit** `Manifold Editor: Save as`.

### Task 12: Docs

**Files:**
- Modify: `tools/ManifoldEditor/README.md` ("Using it": the toolbar, More, Add new button,
  the boxes, the icon picker, Repack plugin / Repack files, the markers, Launch, Save as, the
  remembered window, the status popup), the spec's status line ("Built 2026-10-..; not yet
  tested on Windows"), `docs/resolution.md` §6's entry (`[PROPOSED 2026-10-24]` →
  `[BUILT …; Windows tests: spec §11]`; CRLF, edit with newline-preserving tools).

- [ ] **Step 1: Edit the three docs.**
- [ ] **Step 2: Commit** `Docs: the editor's QoL changes, as built`.
- [ ] **Step 3: Batch 4 end:** checks, push, spec §11 step 10; then a fresh whole-branch
  review (finishing-a-development-branch).
