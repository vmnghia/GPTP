# Manifold Editor .dat part 3 (PyDAT's previews) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PyDAT's pictures (Units Graphics and StarEdit, Sprites, Images), the icons of Weapons, Upgrades, Techdata and Orders with the editor's icon picker, and the Sound Effects play button, on the editor's Data page.

**Architecture:** Core's `DatPreview` decodes frame 0 of an image's GRP with PyMS's palette rules, and builds a *scene* per tab: pictures and boxes in PyDAT's order at PyDAT's pixels. `PreviewScene.Render()` makes the 257×257 ARGB square, so the window only copies pixels into a bitmap. Icons reuse the button page's `IconCache`; sounds play the MPQ's WAV bytes through Windows' `MediaPlayer`.

**Tech Stack:** C# .NET 8, xUnit (+ Xunit.SkippableFact), WinUI 3 built in code (no .xaml).

**Spec:** `docs/superpowers/specs/2026-10-26-dat-previews-design.md` (parts 1-2: `2026-10-24-dat-editor-design.md`, `2026-10-25-dat-links-design.md`). PyMS reference at `/tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/pyms/src` (`<pyms>` below): `PyMS/PyDAT/GraphicsUnitsTab.py` (`drawboxes`, `draw_addon_preview`, `drawpreview`), `StarEditUnitsTab.py`, `SpritesTab.py`, `ImagesTab.py`, `SoundsTab.py`, `DataContext.py` (`get_image_frame`, `get_grp_frame`), `FileFormats/GRP.py` (`rle_*`, `frame_to_photo`).

## Global Constraints

- Core checks: `MANIFOLD_ARR_DIR=<pyms>/PyMS/MPQ/arr dotnet test tools/ManifoldEditor/Manifold.Core.Tests` (all pass; 435 pass, 2 skip today).
- App check: `bash /tmp/claude-0/-home-user-GPTP/766c9e66-d560-5026-8461-003701a27af9/scratchpad/appcheck.sh` (0 errors). No `.xaml`. WinUI built in code: one parent per element; fresh list items; one ContentDialog at a time (`ContentDialogs.Open`).
- Core has no WinUI types. Pixels are ARGB `uint`, `0xAARRGGBB`; index 0 of a GRP is always transparent.
- PyDAT's maths is Python 2's: `/` between integers rounds down. The square is 257×257, centred at (130,130), black.
- A box `(X1,Y1,X2,Y2)` is a Tk rectangle: its outline covers the pixels on the four edges, X1..X2 and Y1..Y2 **inclusive**; a fill covers every pixel in that range. A picture placed at a point (Tk's centre anchor) has its top-left at `(x - Width/2, y - Height/2)`, Width and Height being the GRP's full frame size. Both clip to the square.
- Colours: placement `0xFFFF0000`, dimensions `0xFF00FF00`, add-on `0xFFFFFF00`, health bar `0xFF000000` with boxes `0xFF008000`, selection circle indexes 1-9 `0xFF249824` (36,152,36), shadow `0xFF323232` (50,50,50).
- Draw functions: 9 use remapping, 10 shadow, 13 selection circle (PyMS `ImagesDAT.DrawFunction`); remapping 1 ofire, 2 gfire, 3 bfire.
- Palettes ship as `Data\Units.pal`, `Data\Terrain.pal`, `Data\ofire.pal`, `Data\gfire.pal`, `Data\bfire.pal` (PyMS `Palettes/`, MIT, (c) Zach Zahos), read with `RawPalette.Read`.
- Every Show box is off by default and is remembered in `EditorSettings`; the add-on parent defaults to 106.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01771DQsv5DDEKm1LjYbN2zB`. No model ids.

## Review Focus

- A GRP that is truncated or random bytes: "unit\x.grp is damaged", never an exception out of `DatPreview` (Task 1 test `DamagedGrpsGiveAReasonAtEveryLength`).
- A reference chain that leaves its file (a unit's graphics 300 in a 209-entry flingy.dat, a sprite's image past images.dat's end): the reason names the id and the file; what can still be drawn is (Task 2 test `AChainPastAFilesEndNamesIt`).
- Sprites under 130 in a vanilla sprites.dat have no circle or health bar: the sprite is drawn alone, with no reason; an expanded file draws them on every sprite (Task 2 test `CircleAndBarOnlyWhereTheFileCoversThem`).
- An add-on parent id past units.dat's end, or a unit with add-on position 0,0: the add-on part is left out (with a reason for the first only), the unit still drawn (Task 2 test `AddonLeftOutForABadParentOrZeroPosition`).
- A picture bigger than the square, or pushed off it by an offset or a big extents box: clipped, no index out of range (Task 2 test `RenderClipsToTheSquare`).

---

### Task 1: Pictures: `DatPreview.Image`

**Files:**
- Create: `tools/ManifoldEditor/Manifold.Core/Editor/DatPreview.cs` (`PreviewPalettes`, `PreviewPicture`, `DatPreview` with `Image`)
- Create: `tools/ManifoldEditor/fixtures/palettes/{Units,Terrain,ofire,gfire,bfire}.pal` (copied from `<pyms>/Palettes/`)
- Modify: `tools/ManifoldEditor/fixtures/README.md` (a row for `palettes/`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/ManifoldEditor.App.csproj` (Content: `..\fixtures\palettes\*.pal` linked as `Data\%(Filename)%(Extension)`)
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorResources.cs` (`Resolver`, `Palettes`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Services/Workspace.cs` (loads the palettes)
- Modify: `tools/ManifoldEditor/Manifold.Core.Tests/TestSupport.cs` (`Make.Grp`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatPreviewTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PreviewPalettes(uint[]? Units, uint[]? Terrain, uint[]? Ofire, uint[]? Gfire, uint[]? Bfire)` with `public static PreviewPalettes Load(string folder)` (a missing or unreadable file is null) and `public IEnumerable<string> Missing` (the missing files' names, e.g. `"Units.pal"`).
  - `public sealed record PreviewPicture(uint[] Pixels, int Width, int Height, int Top, int Bottom)`: Width×Height ARGB, 0 transparent; Top and Bottom are PyMS's `image[3]`, `image[4]`: the first drawn row and the last drawn row + 1 (both -1 when nothing is drawn).
  - `public sealed class DatPreview(Func<string, byte[]?> read, PreviewPalettes palettes)` with `public (PreviewPicture? Picture, string? Reason) Image(DatFile images, StringTable? imagesTbl, int image)`.
  - `EditorResources.Resolver` (`ResourceResolver?`, set by `Load`) and `EditorResources.Palettes` (`PreviewPalettes`); `EditorResources.Load(ResourceResolver resolver, uint[]? iconPalette, PreviewPalettes? palettes = null)`.
  - Test helper `Make.Grp(int width, int height, params byte[][] frames)`: each frame Width×Height indexes, framed at (0,0,width,height), rows coded as copies of at most 63 bytes.

- [ ] **Step 1: Write the failing tests** in `DatPreviewTests.cs`. Build images.dat with `DatFile.Parse(DatFormats.Of(DatId.Images), new byte[DatFormats.Of(DatId.Images).VanillaSize])` and `With(...)` (as `DatFileTests.Zeros`; an expanded file as `DatFileTests.Expanded`, `entries * ExpandedEntrySize`), images.tbl with `StringTable` from bytes (as `StringTableTests` does), palettes with `palette[i] = 0xFF000000 | (uint)i` (and a different marker per palette, e.g. `| 0x010000` for Terrain), `read` from a dictionary.
  - `DrawsFrameZeroWithTheUnitsPalette`: grp_file 1 → tbl string 1 `"zerg\\drone.grp"`, read at `"unit\\zerg\\drone.grp"`; a 3×2 frame `{0,5,6, 7,0,8}` gives pixels `{0, Units[5], Units[6], Units[7], 0, Units[8]}`, Width 3, Height 2, Top 0, Bottom 2.
  - `ATrailingNulInTheTblStringIsDropped`: the string `"zerg\\drone.grp\0"` reads the same path (via `DatNames.TblString`).
  - `ThingyTilesetUsesTerrain`: path `"thingy\\tileset\\x.grp"` draws with `Terrain`.
  - `FireRemappingUsesTheFirePalettes`: draw function 9 with remapping 1, 2, 3 → `Ofire`, `Gfire`, `Bfire`; draw function 9 with remapping 0 and 4 → `Units`; remapping 1 with draw function 0 → `Units`.
  - `SelectionCircleDrawsOneToNineGreen`: draw function 13; indexes 1 and 9 → `0xFF249824`, index 10 → `Units[10]`, 0 transparent.
  - `ShadowDrawsEveryPixelGrey`: draw function 10; every non-zero index → `0xFF323232`.
  - `ReasonsForWhatIsMissing` (exact texts): image past the end → `"image 1200 is past images.dat's end"`; grp_file 0 → `"image 3 has no GRP file"`; tbl null or id past it → `"images.tbl has no string 7"`; `read` null → `"unit\\zerg\\drone.grp not found"`; palette null → `"Units.pal not found"`; a GRP with 0 frames → `"unit\\zerg\\drone.grp has no frames"`; width or height 0 → `"unit\\zerg\\drone.grp has an empty frame"`.
  - `DamagedGrpsGiveAReasonAtEveryLength`: a built 4×4 GRP cut at every length from 0 to its full length − 1 gives either a picture or `"unit\\zerg\\drone.grp is damaged"` (or the no-frames/empty-frame reason), never throws; a header offset pointing past the end gives "damaged".
  - `FramesAreCachedPerPathPaletteAndDrawFunction`: two `Image` calls for two images with the same path and draw function read the bytes once (count reads); a third with draw function 13 reads again; draw functions other than 10 and 13 share the normal entry.
  - `PalettesLoadFromAFolder`: a temp folder with `Units.pal` only → `Units` set, the others null, `Missing` = `Terrain.pal, ofire.pal, gfire.pal, bfire.pal`; the shipped `fixtures/palettes/*.pal` all read as 256 opaque colours.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter DatPreviewTests`
Expected: FAIL (build error: `DatPreview` not defined).

- [ ] **Step 3: Implement** `PreviewPalettes`, `PreviewPicture` and `DatPreview.Image` in `Manifold.Core/Editor/DatPreview.cs`, following `DataContext.get_image_frame`/`get_grp_frame`: the path is `"unit\\" + DatNames.TblString(imagesTbl, grp_file)`; the palette by the spec's rule (Terrain test is `path.StartsWith("thingy\\tileset\\")` on the tbl string, before `unit\` is added); the draw function kept only when 10 or 13. Damage: check `bytes.Length >= 6 + 8 * FrameCount` before decoding, and turn `IndexOutOfRangeException`/`ArgumentException`/`ArgumentOutOfRangeException` from `Grp` into the damaged reason. Cache successes and failures in a `Dictionary<(string Path, string Palette, int Draw), (PreviewPicture?, string?)>`. Top/Bottom from the decoded indexes (rows with any non-zero index).

- [ ] **Step 4: Ship the palettes and hand them in.** Copy the five `.pal` files into `fixtures/palettes/`; add the README row ("PyMS `Palettes/*.pal` at bfc5d3a, 768 bytes raw RGB: the palettes PyDAT draws images with. MIT, (c) Zach Zahos (poiuyqwert)."); add the csproj Content line. `EditorResources.Load` keeps `resolver` in `Resolver` and `palettes ?? new(null, null, null, null, null)` in `Palettes`, and adds one note per missing palette: `"<name>: not found; previews that need it show why"`. `Workspace.Open` passes `PreviewPalettes.Load(Path.Combine(AppContext.BaseDirectory, "Data"))`.

- [ ] **Step 5: Run the Core checks and the App check**

Run: the two commands in Global Constraints.
Expected: all Core tests pass (DatPreviewTests included); App compile 0 errors.

- [ ] **Step 6: Commit**

```bash
git add tools/ManifoldEditor/Manifold.Core/Editor/DatPreview.cs tools/ManifoldEditor/Manifold.Core/Editor/EditorResources.cs tools/ManifoldEditor/Manifold.Core.Tests/DatPreviewTests.cs tools/ManifoldEditor/Manifold.Core.Tests/TestSupport.cs tools/ManifoldEditor/fixtures/palettes tools/ManifoldEditor/fixtures/README.md tools/ManifoldEditor/ManifoldEditor.App/ManifoldEditor.App.csproj tools/ManifoldEditor/ManifoldEditor.App/Services/Workspace.cs
git commit -m "Manifold Editor: draw a .dat image's frame 0 with PyDAT's palettes"
```

### Task 2: Scenes: PyDAT's layouts, rendered to the square

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/DatPreview.cs` (layers, scenes, geometry, `SoundPath`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/DatPreviewSceneTests.cs`
- Modify: `tools/ManifoldEditor/Manifold.Core.Tests/DatVanillaTests.cs` (one test)

**Interfaces:**
- Consumes: Task 1's `DatPreview.Image`, `PreviewPicture`.
- Produces:
  - `public abstract record PreviewLayer;` `public sealed record PictureLayer(PreviewPicture Picture, int X, int Y) : PreviewLayer` (X, Y the centre point); `public sealed record BoxLayer(int X1, int Y1, int X2, int Y2, uint Outline, uint? Fill = null) : PreviewLayer`.
  - `public sealed record PreviewScene(IReadOnlyList<PreviewLayer> Layers, IReadOnlyList<string> Reasons)` with `public static readonly PreviewScene Empty` and `public uint[] Render()` (257×257, row-major, background `0xFF000000`, layers in order, transparent picture pixels skipped).
  - `public readonly record struct UnitPreviewOptions(bool Placement, bool Dimensions, bool Addon, int AddonParent)`.
  - On `DatPreview`: `const int Size = 257`, `const int Centre = 130`; `PreviewScene ImageScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int image)`; `PreviewScene SpriteScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int sprite)`; `PreviewScene UnitGraphicsScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int unit, UnitPreviewOptions options)`; `PreviewScene UnitStarEditScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int unit)`.
  - Static geometry: `BoxLayer PlacementBox(int width, int height)`, `BoxLayer StarEditPlacementBox(int width, int height)`, `BoxLayer DimensionsBox(int left, int up, int right, int down)`, `(BoxLayer Box, int X, int Y) AddonBox(int width, int height, int horizontal, int vertical, int parentWidth, int parentHeight)`, `IReadOnlyList<BoxLayer> HealthBar(int circleY, PreviewPicture circle, int health)`.
  - `public static string? SoundPath(DatFile sfxdata, StringTable? sfxdataTbl, int sound)`: `"sound\\" + DatNames.TblString(sfxdataTbl, sound_file)`; null for an entry past the end, `sound_file` 0, or no string.

- [ ] **Step 1: Write the failing tests** in `DatPreviewSceneTests.cs` (build the files as in Task 1; a units/flingy/sprites/images chain of empty vanilla-size files with `With`):
  - `PlacementBoxRoundsAsPyDat`: `PlacementBox(32, 32)` = `(114,114,145,145)`; `PlacementBox(33, 21)` = `(114,120,146,140)` (130−floor(16.5), 130−floor(10.5), 129+ceil(16.5), 129+ceil(10.5)); colour red, no fill.
  - `StarEditPlacementBoxUsesIntegerHalves`: `StarEditPlacementBox(33, 21)` = `(114,120,145,139)`; `(32,32)` = `(114,114,145,145)`.
  - `DimensionsBox`: `DimensionsBox(8, 9, 10, 11)` = `(122,121,140,141)`, green.
  - `AddonBoxAsPyDat`: `AddonBox(64, 64, 96, 0, 128, 96)` = box `(1,97,129,193)` with the picture point `(65,145)` (x = 129 − 32 − 96 = 1, y = 129 − 32 − 0 = 97; point x + 64, y + 48), yellow; an odd width 65 gives x = 129 − 32 − 96.
  - `HealthBarAsPyDat`: a circle picture with Top 10, Bottom 20 at `circleY` 140, health 13 (boxes = max(1, (13−1)/3) = 4, w = 12): black box `(124,151,136,155)` filled black; green boxes `(125,152,126,154)`, `(128,152,129,154)`, `(131,152,132,154)`, `(134,152,135,154)` filled `0xFF008000`; health 0..3 gives 1 box.
  - `RenderPlacesAPictureByItsCentre`: a 3×3 picture at (130,130) puts its pixel (0,0) at square (129,129); a 4×4 at (130,130) at (128,128); a box's corners and edges are drawn inclusive, its inside untouched without a fill.
  - `RenderClipsToTheSquare`: a 300×300 picture at (130,130), one at (−50, 400), and a box `(−10,−10,300,300)` render without throwing; the pixels inside the square are set as clipped.
  - `UnitGraphicsSceneOrder`: with all options on, a unit with add-on position (96, 0) and parent 106: layers are the parent's picture, the unit's picture, yellow box, red box, green box, in that order; with Placement off, no red box; with options all off, only the picture.
  - `AddonLeftOutForABadParentOrZeroPosition`: add-on position (0,0) → no parent picture or yellow box, no reason; parent 300 → no add-on part, reason `"unit 300 is past units.dat's end"`, unit still drawn; a unit under 106 (no add-on position in a vanilla file) → no add-on part, no reason.
  - `AChainPastAFilesEndNamesIt`: graphics 250 with a 209-entry flingy.dat → no picture, reason `"flingy 250 is past flingy.dat's end"`, boxes still drawn when on; the same for a sprite past sprites.dat (`"sprite 600 is past sprites.dat's end"`) and an image past images.dat; a missing file (`dat` returns null) → `"flingy.dat not found"`.
  - `UnitStarEditScene`: the picture then the StarEdit red box.
  - `SpriteSceneOrder`: sprite 130 with circle value 2 and offset 10, health 13: layers are image 563's picture at (130,140), the health bar's five boxes, then the sprite's picture at (130,130).
  - `CircleAndBarOnlyWhereTheFileCoversThem`: sprite 5 of a vanilla-size sprites.dat → only the sprite's picture, no reason; sprite 5 of an expanded sprites.dat (`DatFileTests.Expanded(DatId.Sprites, 600)`'s way) → circle and bar too; a missing circle GRP → no circle or bar, its reason, the sprite drawn.
  - `ImageSceneIsThePictureAtTheCentre`.
  - `SoundPathAsPyDat`: sound_file 1 with tbl string `"Terran\\Marine\\TMaPss00.wav\0"` → `"sound\\Terran\\Marine\\TMaPss00.wav"`; 0 → null; past the end → null.
  - `ReasonsAreDistinct`: a scene where the same GRP is missing twice (the unit and its add-on parent) lists the reason once.

  In `DatVanillaTests.cs`, `EveryVanillaPreviewDrawsOrGivesAReason` (skipped without `MANIFOLD_ARR_DIR`): `read` maps a path to `Path.Combine(ArrDir, "..", path with '\\' → '/')` when the file exists; for every unit (with all options on, both scenes), every sprite and every image, the scene builds and renders without throwing, and every scene with no picture has a reason. Images 561-570 (the selection circles in PyMS's `MPQ/unit/thingy/`) give pictures.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter "DatPreviewSceneTests|DatVanillaTests"`
Expected: FAIL (build error: `PreviewScene` not defined).

- [ ] **Step 3: Implement** the layers, `Render`, the geometry helpers and the four scenes, following PyDAT's tabs (Global Constraints give the box and placement rules):
  - Unit picture: units `graphics` → flingy `sprite` → sprites `image` → `Image`. Units Graphics adds, when on: the add-on part only if the unit `Covers` `addon_position` and its x or y is non-zero (parent graphics chain, parent `staredit_placement_size`); placement from the unit's `staredit_placement_size` (`DatValues.Part` 0 width, 1 height); dimensions from `unit_extents` (parts 0 left, 1 up, 2 right, 3 down).
  - Sprite: circle and bar only when sprites `Covers` `selection_circle_image`; the circle is image `561 + selection_circle_image` at `(130, 130 + selection_circle_offset)`; `HealthBar` from that picture and `health_bar`.
  - `StarEditPlacementBox`: PyDAT's StarEdit tab divides the integers (`self.width.get() / 2`), so both halves round down: `(130 − w/2, 130 − h/2, 129 + w/2, 129 + h/2)`.
  - Reasons are kept in order, without repeats.

- [ ] **Step 4: Run the Core checks**

Run: the Core command in Global Constraints.
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifoldEditor/Manifold.Core/Editor/DatPreview.cs tools/ManifoldEditor/Manifold.Core.Tests/DatPreviewSceneTests.cs tools/ManifoldEditor/Manifold.Core.Tests/DatVanillaTests.cs
git commit -m "Manifold Editor: PyDAT's preview layouts for units, sprites and images"
```

### Task 3: The picture previews in the window

**Files:**
- Modify: `tools/ManifoldEditor/Manifold.Core/Editor/EditorSettings.cs` (`DatPreviewSettings`, `Previews`)
- Test: `tools/ManifoldEditor/Manifold.Core.Tests/WindowSettingsTests.cs` (or the file that tests `EditorSettings` today)
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Dat/PreviewBox.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/IDatTab.cs` (`DatContext`: settings, `Preview`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DataPage.cs` (passes the settings to `DatContext`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/Tabs/{UnitsGraphicsTab,UnitsStarEditTab,SpritesTab,ImagesTab}.cs`

**Interfaces:**
- Consumes: Task 2's scenes, `UnitPreviewOptions`, `PreviewScene.Render`; Task 1's `EditorResources.Resolver`, `Palettes`.
- Produces:
  - `public sealed record DatPreviewSettings` with init properties `bool Unit, UnitPlacement, UnitDimensions, UnitAddon, StarEdit, Sprite, Image` (all false) and `int AddonParent = 106`; `EditorSettings.Previews` (`DatPreviewSettings`, `= new()`).
  - `DatContext(Func<OpenedExe?> opened, Func<DatId, DatNameSetting> nameSetting, Action changed, Action<DatId, int> jump, Action<IReadOnlyList<DatRefMatch>> showUsedBy, Func<EditorSettings> settings, Action<EditorSettings> setSettings)`; `DatContext.Settings` (get), `DatContext.SetSettings(EditorSettings)`; `DatContext.Preview` (`DatPreview?`): one per `OpenedExe` (rebuilt when `Opened()` returns another instance), reading through `Resources.Resolver`.
  - `sealed class PreviewBox` (a plain class, not a control) with `FrameworkElement Root` and `void Show(PreviewScene? scene)` (null: the black square, no text).

- [ ] **Step 1: Write the failing test** `PreviewSettingsRoundTripAndDefault`: a new `EditorSettings` has every `Previews` bool false and `AddonParent` 106; settings with `Previews = new() { Unit = true, AddonParent = 107 }` saved and loaded keep both; a settings file written before this part (no `Previews` key) loads the defaults.

- [ ] **Step 2: Run it to see it fail**, then **implement** `DatPreviewSettings` and `Previews`; run it to see it pass.

Run: `dotnet test tools/ManifoldEditor/Manifold.Core.Tests --filter PreviewSettingsRoundTripAndDefault`
Expected: FAIL, then PASS.

- [ ] **Step 3: Implement `PreviewBox`**: a `StackPanel` holding a 257×257 `Image` over a black `Border` (`Stretch.None`, one `WriteableBitmap(257, 257)` kept and rewritten) and a `TextBlock` (font size 11, wrapping, width 257) for the reasons joined by newlines, collapsed when there are none. `Show` copies `scene.Render()` as `IconCache.Get` does (ARGB words are BGRA bytes).

- [ ] **Step 4: Hand the settings to `DatContext`** and build `Preview` from `Opened()?.Resources` (`new DatPreview(path => r.Resolver?.Find(path)?.Data, r.Palettes)`), cached by the `OpenedExe` reference. `DataPage` passes its `settings` and `setSettings`.

- [ ] **Step 5: Add the previews to the four tabs**, each a `Group("Preview", ...)` with its checkboxes under the square, PyDAT's labels:
  - Units Graphics, beside the Dimensions and Addon groups: "Show Preview"; "Show StarEdit Placement Box (Red)", "Show Dimensions Box (Green)"; "Show Addon Placement (Yellow) with parent building:" and a number box (0-65535, Enter or leaving it commits; a refused value turns red as the other boxes do). The add-on checkbox and box are enabled only when the shown unit `Covers` `addon_position`.
  - Units StarEdit: "Show Preview".
  - Sprites: "Show Preview". Images: "Show Preview".
  - A checkbox click stores its setting (`Context.SetSettings(Context.Settings with { Previews = ... })`) and redraws. Each tab registers a loader (`AddLoader`) that redraws: `box.Show(on ? Context.Preview?.XScene(Context.File, Context.Opened()?.Resources.ImagesTbl, entry, ...) : null)`. `Show` runs after every edit, undo and redo, so the picture follows its fields with nothing more.
  - Update each tab's summary comment ("The preview is left out for now" goes).

- [ ] **Step 6: Run the Core checks and the App check**

Expected: all pass; 0 errors.

- [ ] **Step 7: Commit**

```bash
git add tools/ManifoldEditor/Manifold.Core/Editor/EditorSettings.cs tools/ManifoldEditor/Manifold.Core.Tests tools/ManifoldEditor/ManifoldEditor.App/Dat
git commit -m "Manifold Editor: PyDAT's previews on the Units, Sprites and Images tabs"
```

### Task 4: Icons and the icon picker on the Data tabs

**Files:**
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/IDatTab.cs` (`DatContext.Icons`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/DatFields.cs` (`IconField`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/Tabs/{WeaponsTab,UpgradesTab,TechdataTab,OrdersTab}.cs`

**Interfaces:**
- Consumes: `IconCache(EditorResources)` and `IconCache.View(int frame, double square)` (`Services/IconCache.cs`); `IconPicker(IconCache icons, IReadOnlyList<string> names)` with `Picked` and `Used` (`IconPicker.cs`); `IconSearch.Used(sets)`; `DatContext.Edit`.
- Produces: `DatContext.Icons` (`IconCache?`, one per `OpenedExe`); `DatFields.IconField(DatTabBase tab, DatId file, string property, string label, string? hintKey, ulong? none)` returning the reference field, a 38×38 black square with the icon, and a "Select..." button.

- [ ] **Step 1: Implement `DatContext.Icons`**, cached by the `OpenedExe` reference as `Preview` is.

- [ ] **Step 2: Implement `DatFields.IconField`**: the existing `ReferenceField(tab, file, property, label, hintKey, () => tab.Context.Lists["Icons"], none is null ? null : "None", 300)`, then a `Border` 38×38 black holding `Icons.View(frame, 38)` (empty when the value is the none value, past `Icons.Count`, or `View` returns null), redrawn by a loader; "Select..." opens a fresh `IconPicker(Icons, Lists["Icons"])` as its flyout, `Used = () => IconSearch.Used(opened.State.Document.Sets)` (the icons the button sets use, as on the button page), and `Picked` edits the property to the frame through `Context.Edit` (one undo step). The button is disabled when `Icons` is null.

- [ ] **Step 3: Use it** for weapons `icon` ("WeapIcon"), upgrades `icon`, techdata `icon` and orders `highlight_icon` (none 65535, "OrdHighlight"), keeping each tab's current label and hint. Update the tabs' summary comments ("The icon preview and the icon picker are left out for now" goes).

- [ ] **Step 4: Run the App check**

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifoldEditor/ManifoldEditor.App/Dat
git commit -m "Manifold Editor: icon previews and the icon picker on the Data tabs"
```

### Task 5: The play button, and the docs

**Files:**
- Create: `tools/ManifoldEditor/ManifoldEditor.App/Services/SoundPlayer.cs`
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/IDatTab.cs` (`DatContext.Sounds`)
- Modify: `tools/ManifoldEditor/ManifoldEditor.App/Dat/Tabs/SoundsTab.cs`
- Modify: `tools/ManifoldEditor/README.md` (a "Previews" section), `CLAUDE.md` (the editor bullet: part 3 built, not yet run on Windows), `docs/resolution.md` §6 (part 3 `[BUILT 2026-10-26; not yet run on Windows]`; CRLF: edit with Python `io.open(..., newline='')`), the spec's Status line.

**Interfaces:**
- Consumes: Task 2's `DatPreview.SoundPath`; `EditorResources.Resolver`.
- Produces: `sealed class SoundPlayer : IDisposable` with `void Play(byte[] wav)` (stops a sound still playing, then plays these bytes) and `event Action<string>? Failed` (the reason Windows gives); `DatContext.Sounds` (one `SoundPlayer` for the page, made on first use).

- [ ] **Step 1: Implement `SoundPlayer`** on `Windows.Media.Playback.MediaPlayer`: the bytes into an `InMemoryRandomAccessStream`, `MediaSource.CreateFromStream(stream, "audio/wav")`, `Source` replaced on each play; `MediaFailed` raises `Failed` with `args.ErrorMessage` (marshalled back to the UI thread through the page's `DispatcherQueue`).

- [ ] **Step 2: Add the ▶ Play button** beside the sound file in `SoundsTab`: a loader works out `SoundPath(Context.File(DatId.Sfxdata), Resources.SfxdataTbl, entry)` and reads it once through `Resources.Resolver` (kept for the click); the button is enabled only when there are bytes. Its tooltip is the path, or "No sound" / "<path> not found". A click plays the kept bytes. A `TextBlock` beside the button shows a failure, `"Can't play <path>: <reason>"`, and is cleared on the next play or entry. Update the tab's summary comment.

- [ ] **Step 3: The docs.** README "Previews": what each tab shows, the Show boxes remembered, the icon Select..., the play button, and that pictures are frame 0 only. The CLAUDE.md editor bullet's part list ("its parts 3-5 ... are not built") becomes "part 3, previews, is built and not yet run on Windows; parts 4-5, entry counts and improvements, are not built". The spec's Status: "Built 2026-10-26 (plan `docs/superpowers/plans/2026-10-26-dat-previews.md`): the drawing is tested on Linux, the window compiles; not yet run on Windows."

- [ ] **Step 4: Run the Core checks and the App check**

Expected: all pass; 0 errors.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifoldEditor/ManifoldEditor.App tools/ManifoldEditor/README.md CLAUDE.md docs/resolution.md docs/superpowers/specs/2026-10-26-dat-previews-design.md
git commit -m "Manifold Editor: the Sound Effects play button; docs for the Data page's previews"
```
