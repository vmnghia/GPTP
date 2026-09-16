# Beam weapons

Target: **StarCraft: Brood War 1.16.1**, as a GPTP plugin. Builds on an earlier handoff
document; the engine research in §3 is largely from that work.

## How to read this

| Tag | Meaning |
|---|---|
| **[BUILT]** | Exists and works today, observed in game. Trust it. |
| **[PROPOSED]** | Suggested, never implemented or benchmarked. A hypothesis. |
| **[VERIFY]** | Claimed from recall or a third party. Confirm before relying on it. |

Nothing tagged [PROPOSED] has been agreed as a plan. Keep these honest — conflating "works"
with "should work" is what the tags exist to prevent.

---

## 1. Goal

A **general beam weapon system**, not one showcase unit:

- A weapon id is designated a beam weapon in config; any unit assigned that weapon in
  units.dat fires the beam, with no per-unit code.
- Multiple beam weapons coexist, each with its own appearance.
- Eventually several *kinds* of beam: instantaneous (current), continuous while firing
  (Void Ray), sweeping along a path (Colossus).

**Non-goal:** reproducing SC2's effects faithfully. Generating detailed beams in memory every
frame is expensive, and the cost scales with the number of simultaneous beams. Keep the beam
*shape* cheap and put the flexibility in the *system*.

---

## 2. Current state

**[BUILT]** — verified in game:

- In-memory GRP generation: rasterize into an offscreen buffer, encode to GRP RLE layout,
  point a fresh overlay's `grpOffset` at it. No draw hook involved.
- Beam spawns on the **actual shot**, from `hooks::fireWeaponHook` (the iscript
  `attackwith`/`attack` path), gated on weapon id — currently `ArcliteCannon`. Confirmed:
  the tank fires `wpn=11` from `unit=5`, the turret subunit.
- Aiming via `scbw::getPolarX/getPolarY` off the unit's `currentDirection1`. Verified
  numerically: `dir=112, len=115` produced `end=171,233`, which matches
  `127 + 115·sin(157.5°) = 171` and `127 + 115·cos → 233` exactly.
- Per-unit ring of raster/GRP buffers (`kBeamRingDepth = 6`), so simultaneous beams never
  share a buffer.
- Rasterization is a thickness-swept quad, scanline-filled, with an intensity ramp across 9
  frames. Colors index a fire remap table (`ofire` in use).

**Debug switches** live at the top of `Beam.h`:

- `BEAM_DEBUG_FIXED_AIM` — draw a fixed due-east beam from a literal endpoint, bypassing both
  the unit's aim and the angle table. Isolates the render path from the aiming.
- `BEAM_DEBUG_PRINT` — print weapon id and aim values via `printText`, capped per session.

### Bugs fixed, worth not regressing

- Polygon vertices were `Point16` (**unsigned**). Beams pointing left or up produced negative
  coordinates that wrapped to ~65535, so the scanline fill painted a full-canvas rectangle.
  Now `Point32` and signed throughout.
- The intensity ramp passed `frames - i - 1`, so the last frame got `nColors = 0`,
  `generateBeam` early-returned, and the frame was empty. Now `frames - i`.
- `generateGrp`'s bbox clamps cast to `uint16_t` before comparing, wrapping the `0x10000` and
  `-65536` sentinels both to 0, so an all-transparent frame emitted garbage byte-truncated
  bounds. Empty frames now collapse to an honest 0×0.
- Beam buffers were a single **global** pair shared across every firing unit, and
  `createBeamGrp`'s return value was discarded, leaking ~1.1MB per shot and reading freed
  memory. Now per-unit.
- `generateGrp` allocated with `new[]` and freed with `free()`.
- `injectWeaponFireHooks()` sat inside a disabled `/* */` block in `initialize.cpp`, so the
  hook never installed and the beam silently never spawned.

---

## 3. Engine constraints

### 3.1 GRP frame dimension ceiling

A GRP frame header is 8 bytes: `x offset`, `y offset`, `width`, `height` (bytes), then a u32
pointer to row data. Width and height are byte fields — **there is physically nowhere to store
a value above 255**. This is not a validation check that can be patched out. Exceeding it
does not clip, it crashes: the engine reads garbage dimensions and walks row offsets off the
end of the buffer.

Note this repo declares `GrpFrame` as `s8 x, y, width, height` while the encoder and the
original research both treat them as unsigned. Per the working agreement in CLAUDE.md, assume
upstream is right and revisit only if a concrete failure points here. It matters because it
bounds how far a frame can be offset, and therefore the maximum reach in §4.1.

### 3.2 Diagonal beams are disproportionately expensive

A GRP frame is a **rectangle** and the engine walks every row of it.

- A 255px horizontal beam → a 255×1-ish box. Cheap.
- A 255px beam at 45° → a ~255×255 box: 255 rows of offset lookup and RLE decode to light
  roughly 255 pixels.

Cost is `O(bounding box area)`, not `O(beam length)`, degrading quadratically with range.

### 3.3 Per-frame cost, per beam

Roughly in order of expense:

1. **Clearing the offscreen buffer** — currently a full `9 × 255 × 255 × 2` fill (~1.1MB)
   every regeneration, even though only a few thousand pixels get lit.
2. **RLE encoding** — scans every row of the frame box regardless of what was drawn.
3. **Engine-side RLE decode** on draw.
4. **iscript execution** for every spawned image, every tick.
5. **Line rasterization** — a width-16 beam over 255px is a few thousand pixel writes. This is
   noise on any modern CPU. **Do not optimize this first**; the encode/decode round trip
   around it is the real cost.

### 3.4 8-bit indexed palette

No true alpha. `ofire`/`bfire` and siblings are **remap tables**: index by
`[intensity_row][destination_pixel]`. Indexing by destination is what makes the beam brighten
what is behind it rather than painting over it.

**[VERIFY]** Confirm the remap table's row count and stride before hardcoding a `* 256`
multiply — BW's remap tables are not all the same height.

Because the falloff is additive, the beam's appearance changes with the background.
**[PROPOSED]** Make the innermost row or two near-opaque so the core reads consistently, and
leave only the halo additive.

### 3.5 Sprite sort order

BW sorts sprites by y coordinate. A long diagonal beam spans a large y range from a single
anchor, so expect wrong sorting against units near either end. Little control under the GRP
path; a draw hook (§4.6) owns the draw call and can force ordering.

### 3.6 Image pool pressure

Every spawned overlay consumes an image entry. `createTopOverlay` returns null when the pool
is exhausted — already guarded. Any tiled-segment approach would consume one entry per
segment plus per-tick iscript for each, and could exhaust the pool outright.

### 3.7 Overlay lifetime is not ours

`CSprite::createTopOverlay` always allocates a **new** `CImage`; it never reuses one. The
overlay currently borrows `ImageId::Explosion2_Small`, whose lifetime is driven by its own
iscript — the engine decides when it dies, not us.

For instantaneous beams this is survivable: the ring in §2 delays freeing a buffer for
`kBeamRingDepth` shots, which is a **safety margin, not a guarantee**. For anything
persistent it is disqualifying, which makes §4.3 a prerequisite rather than a nicety.

---

## 4. Roadmap

### Architecture decision: draw as shapes, not as GRPs

**[BUILT]** infrastructure, **[PROPOSED]** for beams.

Range must be unbounded — siege mode alone is 12 tiles (~384px), and mod-defined weapons can
go further, up to map length. A GRP frame's width and height are byte fields, so **a single
frame physically cannot exceed 255px**. That is a wall, not a budget: no amount of
bounding-box work gets past it. Tiling into 255px segments would, at one image entry and one
iscript per segment (§3.6), and it would generate every segment whether on screen or not.

The repo already carries the alternative. `hooks::injectDrawHook()` patches `0x004BD68D` with
a BWAPI-derived hook that receives the screen `Bitmap*` each frame and calls
`graphics::drawAllShapes()` after the game has drawn. Rally point lines already use it across
arbitrary map distances. Queueing the beam as shapes gives:

- **No length ceiling** — `Shape` holds `Point32` endpoints; nothing passes through a byte.
- **No GRP, no overlay image, no images.dat entry, no iscript** — §3.7's borrowed lifetime
  problem, the ring buffer, and the whole encode/decode round trip (§3.3 items 1-3) all
  disappear rather than shrink.
- **Per-frame redraw is free.** Shapes reset every frame and are re-queued, so continuous and
  sweeping beams are a matter of varying the endpoints — no persistence to manage.
- **Capacity is not a concern.** `MAX_SHAPES` is 10000 per frame.

Costs and unknowns: shapes draw *after* the game, so beams render over everything and are
never occluded (this sidesteps §3.5 rather than solving it, and may or may not be the look
wanted). `Bitmap`'s public drawing methods take a flat `ColorId`, so the destination-indexed
remap glow of §3.4 is not available without extending `Bitmap` — the first version
approximates it with an intensity ramp across the beam's thickness using the same palette
entries.

This supersedes the ordering below. §4.1's bounding-box work targeted the GRP encoder, which
the shape path removes entirely; §4.3's dedicated `images.dat` entry is no longer a
prerequisite for anything. Both are kept for reference in case the GRP path is ever revived
(it remains available behind `BEAM_USE_GRP_PATH`).

### Superseded ordering

Ordered. 4.1 and 4.2 are code-only and independent; 4.3 is where data editing enters.

### 4.1 Bounding-box-relative rendering — **next**

**[PROPOSED]** Fixes reach and the dominant CPU costs in one change.

The beam is currently rasterized outward from the **center** of a fixed 255×255 canvas, so it
can only reach ~127px in any direction — but a siege tank outranges that at ~224px, so long
shots render short.

A GRP frame carries its own x/y offset, so the frame need not be centered. Rasterize the
beam's tight bounding box and use the frame offset to position it:

- Axis-aligned beam → a ~255×16 box, so a full 255px beam fits.
- 45° beam of length L → an L/√2 × L/√2 box, so L can reach ~360 before a dimension hits 255.

That covers every BW weapon range. It also removes the §3.3 costs: clear and encode only the
bbox instead of the whole canvas.

Also here: **[PROPOSED]** track the previous frame's bbox and clear only that region, and
skip regeneration entirely when the endpoints have not moved (a dirty flag). A stationary
beam should cost nothing.

### 4.2 Weapon-id → beam config table

**[PROPOSED]** Replace the hardcoded `weaponId == ArcliteCannon` gate with a lookup keyed by
weapon id, holding thickness, remap table, frame count, color ramp and beam type. Units get
beams automatically via units.dat — no per-unit work.

Start as a C++ table. Moving it to a text config later fits the asset-versioning process
(binaries as build outputs, text as source of truth).

### 4.3 Dedicated `images.dat` entry

**[PROPOSED]** A minimal entry with a single-frame, non-expiring iscript, replacing the
borrowed `Explosion2_Small`. Removes the §3.7 lifetime guesswork and lets the ring in §2 be
replaced by real ownership.

This is the first step needing PyMS/data editing rather than C++, and therefore the natural
forcing function for the asset-versioning workflow.

### 4.4 Persistent beam instances — continuous beams

**[PROPOSED]** Requires 4.3. A per-unit beam instance with a real lifetime (start on fire,
update per frame while firing, end on stop), driven from `nextFrame()`, regenerating only
when the endpoint moves. This is what a Void Ray-style beam needs: it tracks a moving target
rather than being fired and forgotten.

### 4.5 Sweeping beams

**[PROPOSED]** Requires 4.4. A parametric path over time, independent of target position, so
the beam sweeps rather than tracks.

### 4.6 Draw-hook path

**[PROPOSED]** The structural answer if CPU actually bites with many simultaneous beams.
`images.dat` has a drawing-function field; hook or add one and blit the beam directly from an
owned buffer using 16-bit dimensions.

Wins: `O(beam length)` instead of `O(bbox area)`, same cost at any angle; no RLE encode and no
engine decode — the whole round trip disappears; **no 255 ceiling**; one image entry per beam;
explicit draw ordering, fixing §3.5.

Costs: **clipping becomes ours** (BW's built-in draw functions handle partially off-screen
images; a custom blitter that doesn't will write past the end of a row), and sort order becomes
an explicit decision.

Do not attempt this before 4.1 is benchmarked — 4.1 may well be enough.

---

## 5. Prior art: SC: SUM

**[VERIFY]** — from the mod author's moddb comments, paraphrased.

The first and only known Brood War mod to ship a beam weapon (on its Mothership):

- One in-memory GRP per Mothership instance; the GRP pointer of an underlay image is
  redirected to it. **The per-unit copy is mandatory** — without it every unit sharing the
  underlay shows the same graphic.
- Rendered to an offscreen buffer, converted to GRP RLE layout, then a rendering update is
  triggered on the underlay.
- The iscript does nothing meaningful: one image, one frame.
- Memory cost negligible; **CPU time is the constraint** — software rendering plus an RLE
  encode against a frame budget in milliseconds.
- The author's conclusion: a fully laser-based mod is possible, but only with "advanced
  performance optimization techniques."

The Mothership beam is short-ranged and the author warned to keep the number of beam units
low. **[VERIFY]** Whether that range was the 255 box ceiling or a balance decision is unknown.

---

## 6. Open questions

1. Remap table row count and stride (§3.4).
2. Whether `GrpFrame`'s byte fields are signed or unsigned in practice, which bounds the
   maximum reach achievable in §4.1 (§3.1).
3. **[VERIFY]** The Burning Ground mod reportedly broke many engine limits via plugin; whether
   GRP frame dimensions were among them is unconfirmed.
4. Dead duplicates: the same GRP-encoding routine exists four times over (`createGRP` and
   `generateGrp` in `Beam.cpp`, `BeamManager::GenerateGrp`, `Grp::Generate`), of which only
   `generateGrp` is live. `GRPPalette.cpp/h` and `Canvas.cpp/h` are likewise unreferenced by
   the beam path. Worth deleting once the system settles.

---

## 7. Rules for anyone picking this up

- **Do not trust recalled 1.16.1 addresses or struct layouts** — not from documentation, not
  from an LLM. Source them from the GPTP headers and verify against the binary.
- **Do not let the beam touch game state.** Cosmetic-only keeps lockstep and replays correct
  for free. The moment a visual can influence damage or timing, every client must execute it
  identically — a much harder bar.
- **Benchmark before optimizing.** The intuitive bottleneck (line drawing) is not the real one
  (§3.3).
- **Non-goals:** Remastered support; SC2 effect fidelity; any limit-breaking work not demanded
  by a concrete blocker.
