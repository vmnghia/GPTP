# Manifold Editor: the .dat files, part 3 (PyDAT's previews)

Status: Built 2026-10-26 (plan `docs/superpowers/plans/2026-10-26-dat-previews.md`): the drawing is
tested on Linux, the window compiles; all of §7 passed on Windows 2026-10-26. After that run, PyMS's 2400-frame cap was dropped so a GRP with more frames (the Viking's assault mode) draws; that is not yet checked on Windows. Design 2026-10-26. Part 3 of the PyDAT port planned in
`2026-10-24-dat-editor-design.md` (part 1, the fields, passed on Windows 2026-10-25; part 2,
the links, `2026-10-25-dat-links-design.md`, built and not yet run on Windows): the
pictures on the Units Graphics and StarEdit sub-tabs, Sprites and Images, the icons of
Weapons, Upgrades, Techdata and Orders, and the Sound Effects play button. The user's
answers: Core does the drawing and the window only paints (approach A); the icon picker is
the editor's own `IconPicker`, not PyDAT's.

The reference is PyMS at `bfc5d3a` (MIT): `PyMS/PyDAT/GraphicsUnitsTab.py`,
`StarEditUnitsTab.py`, `SpritesTab.py`, `ImagesTab.py`, `WeaponsTab.py`, `UpgradesTab.py`,
`TechnologyTab.py`, `OrdersTab.py`, `SoundsTab.py` and `PyDAT.py`'s `grp`/`get_image`
code.

## 1. Goal

What PyDAT shows of an entry, the editor shows the same way: the same picture, the same
boxes at the same pixels. Success: §7's Windows list passes, and a unit's preview here
matches PyDAT's beside it.

## 2. Drawing (`DatPreview`, Core)

**`DatPreview.Image(imageId, ...)`** returns frame 0 of an image as ARGB pixels with its
width and height, or null with a reason.

- **Path:** images.tbl's `grp_file` string with `unit\` in front. The bytes come through
  the document's resolver: the exe's MPQ first, then vanilla.
- **Decoding:** the existing `Grp` (`Manifold.Core/Data/Grp.cs`). Its header is checked
  first: a frame that reaches outside the file gives "unit\foo.grp is damaged".
- **Palette:**
  - Terrain for a path under `thingy\tileset\`;
  - ofire, gfire or bfire when the draw function is 9 and the remapping is 1, 2 or 3;
  - Units otherwise.
- **Draw functions:**
  - 13 (selection circle): indexes 1-9 drawn as (36,152,36);
  - 10 (shadow): every drawn pixel as (50,50,50);
  - anything else normal.
  - Index 0 is transparent in every case.
- **Cache:** frames are cached per (path, palette, draw function). The cache is dropped
  when the document's sources change (another exe opened).

**Overlays.** All are in a 257×257 square centred on (130,130). They use PyDAT's maths,
including Python 2's integer division (rounding down).

- **Placement box** (red, unit placement size w×h): from
  `130-floor(w/2), 130-floor(h/2)` to `129+ceil(w/2), 129+ceil(h/2)`.
- **StarEdit's placement box** is the same red box, but PyDAT's StarEdit tab divides the
  integers, so both halves round down: `130-w/2, 130-h/2` to `129+w/2, 129+h/2`.
- **Dimensions** (green, the unit's extents): from `130-left, 130-up` to
  `130+right, 130+down`.
- **Add-on placement** (yellow):
  - the parent's box at `x = 129 - w/2 - addon.x`, `y = 129 - h/2 - addon.y`, sized by
    the parent's placement size;
  - the parent's picture centred in it;
  - the parent unit id defaults to 106.
- **Sprites:**
  - The selection circle is image `561 + value`, drawn at y = 130 + the vertical offset.
  - The health bar is black, `3*boxes` wide and 4 high, at
    `y + 6 + (frame bottom − top)/2`. Its green boxes are 1×2, one every 3 pixels.
- Pictures are centred on their point.

**Icons** need nothing new in Core: the editor already draws cmdicons.grp frames
(`EditorResources.IconPixels`, `CardLayout.IconBounds`), and the window's `IconCache.View`
centres one in a square. The Data tabs reuse it at 38×38, PyDAT's preview size. The None
value (65535) and a frame past the GRP's end show an empty square. Every tab draws the icon
plain: PyDAT recolours an icon through `unit\cmdbtns\ticon.pcx` only for the selected
tile of its own picker, which the editor doesn't use.

**Sound path.** `DatPreview.SoundPath(sound)` is `sound\` plus sfxdata.tbl's string. 0
("No sound") gives none.

**Palettes** ship from PyMS in the app's `Data\`: Units, Terrain, ofire, gfire, bfire
(`.pal`, beside the existing Icons palette). They are read with `RawPalette.Read`.

## 3. The window

Each picture preview is a 257×257 black square in its own group, with PyDAT's checkboxes.
Every checkbox is off by default, as in PyDAT, and is remembered in `EditorSettings`.

- **Units Graphics:**
  - Show Preview: the unit's picture, via graphics → flingy → sprite → image.
  - Show Placement (red) and Show Dimensions (green).
  - Show Addon Placement (yellow) with a parent building id box, 106 by default. It's
    enabled only for units 106-201, the ones that have an add-on position.
- **Units StarEdit:** Show Preview, with the picture and the red placement box.
- **Sprites:** Show Preview, with the sprite's image, plus the selection circle and health
  bar on sprites 130 and up.
- **Images:** Show Preview of the image itself, with its draw function and remapping.
- **Icons:**
  - Weapons, Upgrades and Techdata draw the icon beside the icon field. A **Select...**
    button opens the editor's own `IconPicker`, with its search and Unused filter.
  - Orders draws its highlight icon the same way, with the same picker.
  - The None value (65535) shows an empty icon.
- **Sound Effects:** a ▶ Play button beside the sound file.
  - It plays the WAV bytes from the exe's MPQ, then vanilla, through Windows'
    `MediaPlayer`.
  - A new play stops a sound still playing.
  - It's disabled for "No sound" (0) or when the file isn't found.

**Redrawing.** A preview redraws when its entry changes, or when a field it depends on is
edited or undone. The unit's picture follows its graphics field; the dimensions follow the
extents. Nothing is drawn while its Show box is off.

## 4. Errors

- A preview never blocks an edit. When one fails, the square stays black with the reason in
  small text under it, e.g. "unit\zerg\foo.grp not found". The tab's fields work as before.
  The causes are a missing GRP, palette or TBL string, a GRP that doesn't decode, or a
  frame of size 0.
- An id past the end of its target file is drawn as missing:
  "image 1200 is past images.dat's end".
- A sound that fails to play (missing file, a format Windows can't play): a line beside the
  Play button says why (the Data page has no status line). The editor goes on.
- A missing palette in `Data\`: every preview that needs it says so; the rest work.

## 5. Units

- **Core:** `Manifold.Core/Editor/DatPreview.cs` (pictures, overlays, sound path)
  and its result types: pixels with a size, or a reason, and a list of overlay
  rectangles with colours. No WinUI types.
- **App:** a preview control in `ManifoldEditor.App/Dat/` that paints `DatPreview`'s
  pixels and rectangles into a `WriteableBitmap`, and the checkboxes. A sound player
  service. The tabs gain the groups in §3.
- `EditorSettings` gains the Show boxes and the add-on parent id.

## 6. Out of this part

Animation (PyDAT shows frame 0 only, and so does this); PyDAT's own icon picker; the
iscript preview; adding entries (part 4).

## 7. Testing

**Core (Linux):**
- `DatPreview.Image` with small GRPs built by the tests:
  - normal drawing: index 0 transparent, the Units palette;
  - the Terrain palette for `thingy\tileset\`;
  - the fire palettes for draw function 9 with remapping 1-3;
  - the selection circle colour, and the shadow colour.
- Missing and damaged GRPs give their messages.
- Overlay geometry against PyDAT's numbers, worked out by hand from its code: the
  placement box at odd and even sizes, the dimensions, the add-on box with parent 106 and
  another parent, the selection circle's offset, and the health bar's width and boxes.
- The sound path: `sound\` plus the sfxdata.tbl string; 0 gives no sound.
- With `MANIFOLD_ARR_DIR`, every vanilla unit, sprite and image either draws or gives a
  reason, and none throws. (Linux has no unit GRPs, so most give "not found"; the test is
  that the whole chain runs.)

**Windows (the user):**
1. Marine, Command Center (placement and dimensions), Comsat Station (add-on on parent
   106), and a Zerg egg, on Graphics and StarEdit.
2. Sprites: a unit sprite with its circle and health bar. Images: a shadow, a selection
   circle, and a fire image in its colour.
3. The weapon, upgrade, tech and order icons, and Select... to pick one.
4. Play a sound, then play another while it's playing. "No sound": the button is disabled.
5. Edit the graphics field and see the preview follow; undo and see it follow back. Turn
   the Show boxes off, reopen the editor: they stay off.
