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
- The overlay's `renderFunction` points at our own blitter (`BEAM_USE_CUSTOM_RENDER`), so the
  beam drawn on screen is not the GRP's pixels and is not capped at 255px. The GRP still
  supplies the image's bounds, the frame count, and a short-but-valid fallback. See
  "Custom render function" below.

**Debug switches** live at the top of `Beam.h`:

- `BEAM_USE_CUSTOM_RENDER` — draw the beam ourselves rather than letting the engine blit the
  GRP. 0 reverts to the engine blitting the GRP, 255px ceiling and all.
- `BEAM_DEBUG_FIXED_AIM` — draw a fixed due-east beam from a literal endpoint, bypassing both
  the unit's aim and the angle table. Isolates the render path from the aiming.
- `BEAM_DEBUG_PRINT` — print weapon id and aim values via `printText`, capped per session.
- `BEAM_DEBUG_RENDERFN_PROBE` / `_MARKER` — the render function probes. Both off; their
  findings are recorded below.

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

### Rejected: drawing as shapes through the draw hook

**[BUILT]** and tried, then **rejected**. Kept behind `BEAM_USE_GRP_PATH 0` for reference.

Queueing beams as `graphics::` shapes lifts the 255px cap and removes the GRP encode, the
overlay image and its borrowed lifetime. It fails on three counts that matter more:

1. **No depth.** `DrawHook` runs *after* `oldDrawGameProc`, so the frame is already composited
   and everything drawn is on top. An air unit firing at a ground target should be occluded by
   units between them. This is not tunable from the draw hook — by the time it runs, sorting
   has happened.
2. **Shape budget.** ~17 shapes per beam, scaling with thickness, out of a 10000 pool shared
   with progress bars, rally lines and order queues. A few hundred beams exhausts it.
3. **No remap blending.** `Bitmap`'s public methods take a flat `ColorId`, so §3.4's additive
   glow would have to be reimplemented rather than inherited from the engine.

The lesson: the engine's rendering model is where sorting and palette blending are already
solved. Leaving it to escape the 255px cap trades three working things for one.

### Architecture decision: custom render function on a real image

**[PROPOSED]** — the synthesis. Keeps the engine's model, loses the byte fields.

`CImage` carries a **per-instance** render function pointer, with its signature documented at
`SCBW/structures/CImage.h:113`:

```
/*0x34*/ void* renderFunction;
// renderFunction(screenPosition.x, screenPosition.y, getCurrentFrame(), &rctDraw, (int)coloringData)
```

Pointing that at our own blitter on our own overlay gives, all at once:

- **Depth** — it is a real `CImage` on a real `CSprite`, so it sorts with everything else.
- **One image per beam** — no shape pool, no tiling.
- **No 255 ceiling** — we own the blit, so dimensions never pass through a byte field, and we
  clip to `rctDraw`, making cost proportional to the *visible* span rather than beam length.
- **Remap blending for free-ish** — the fifth argument *is* `coloringData`, which
  `CImage::setRemapping()` sets to `colorShift[remapping].data` (`CImage.cpp:52`). So the
  remap table arrives as a parameter, and §6.2's `dst = table[intensity * 256 + dst]` is
  directly implementable.

Relevant data, all already mapped in the repo:

- `SCBW_DATA(const ColorShiftData*, colorShift, 0x005128F8)` — `scbwdata.h:415`
- `struct ColorShiftData { u32 index; void* data; char name[12]; }` — `structures.h:197`
- `ColorRemapping::{None, OFire, GFire, BFire, BExpl, Trans50, ...}` — `enumerations.h:338`
- `PaletteType::RLE_FIRE = 17` — the engine's own fire blitter, `CImage.h:36`

#### Probe results

**[BUILT]** — measured in game, not inferred. The probe replaced `renderFunction` with a naked
thunk that recorded registers and stack, then chained to the engine's function.

Established:

- **The engine does call the per-instance pointer**, and replacing it is survivable. The
  approach is viable.
- **Three of the five arguments are on the stack, in the header's order.** That accounts for
  args 3-5, leaving args 1-2 to arrive in registers — the shape of `__fastcall`. Which
  registers, and who cleans the stack, round 2 settles.
- **`coloringData` - the remap table - arrives at `stack[3]`.** Confirmed by exact match
  against the value logged at spawn, constant across captures. This is the whole reason the
  path is worth taking: the bfire/ofire blending table is handed to us as an argument.
- **`&rctDraw` is `stack[2]`** - a stack address, constant across captures.
- **The frame/GRP pointer is `stack[1]`** - one capture matched the overlay's `grpOffset`
  exactly.
- Every call arrives from the same site, `0x00497D4A`. The engine's own render function for
  the overlay read `0x0040B5D6` in one run and `0x0040B596` in another - see below.
- The surface to draw into is already mapped: `gameScreenBuffer` (`scbwdata.h:377`), a
  `graphics::Bitmap*` at `0x006CEFF0`, used exactly this way by `Shape.cpp`.

#### Probe round 1, corrected

The first run's `rfn reg` and `rfn stk` lines **did not come from the same call**. The naked
thunk writes the registers to globals on *every* call — it has nowhere else to put them before
it can call into C — while the stack snapshot was taken once and then frozen behind a
`probePending` flag. So the registers reported were whatever the last call before reporting
happened to hold, and the stack was from some earlier call. That is the whole of the
`ECX = 0x42A` anomaly: it was never paired with the stack values it was printed beside.

The stack findings stand (they were one coherent snapshot). The register findings do not.

Two other observations from the runs, for the record:

- `coloringData` differs between runs (`0x039F008C`, `0x0A0C008C`) but keeps the same low 16
  bits. That is what a heap pointer looks like across runs: Windows hands out reservations on
  64 KiB granularity, so an offset within a block is stable while the block's base moves.
  Consistent with `coloringData` being `colorShift[...].data`, as `CImage.cpp:52` says.
- The engine's own render function for the overlay differed between runs too: `0x0040B5D6`
  and `0x0040B596`, 0x40 apart — two entries in the same table, not one function. The engine
  selects it from `images_dat::RLE_Function[imageId]` (`CImage.cpp:78-82` reads the same field
  for `coloringData`), which is constant for a constant image id, so **this is not yet
  explained**. StarCraft.exe is not rebased — every hardcoded address in this project depends
  on that — so both are real, distinct code addresses. The probe now logs `pal=` alongside
  `orig=` to test the obvious hypothesis. It does not block the blitter, which replaces the
  function outright rather than chaining to it.

#### Probe round 2 — the signature, confirmed

Round 2 made the capture atomic (registers, stack and image state copied in the same call) and
added ground truth. One sample settled everything:

```
rfn reg cx=7A dx=4E img=122,78
rfn stk a=866006E b=1AFCAC c=A0C008C d=1AFDB4
rfn img frm=866006E grp=8660058 ax=0
rfn ret 497D4A: 5F 5B 80 66 0C FE
```

| argument | evidence |
| --- | --- |
| 1 — screen x, in `ECX` | `cx=7A` = 122 against the image's own `screenPosition` `122,78`, read at that instant |
| 2 — screen y, in `EDX` | `dx=4E` = 78, same |
| 3 — `GrpFrame*`, `stack[1]` | `866006E` equals `&grpOffset->frames[frameIndex]`; 0x16 past `grp=8660058` is the 6-byte header plus two 8-byte frames, i.e. frame 2 |
| 4 — `&rctDraw`, `stack[2]` | `1AFCAC`, a stack address |
| 5 — `coloringData`, `stack[3]` | `A0C008C`, the value logged at spawn |

`stack[4]` is past the fifth argument, so there are five and no more.

The return site decides who cleans the stack: `5F 5B 80 66 0C FE` is `POP EDI` / `POP EBX` /
`AND BYTE PTR [ESI+0Ch], 0FEh`. No `ADD ESP, imm`, so the **callee** cleans. Two register
arguments plus callee-cleaned stack arguments is `__fastcall`:

```cpp
void __fastcall render(int screenX, int screenY, GrpFrame *frame, void *rctDraw, void *coloringData);
```

That third instruction is a free corroboration: `CImage + 0x0C` is `flags` and bit 0 is
`CImage_Flags::Redraw`, so the caller holds the `CImage*` in `ESI` and clears its redraw flag
on return. We are in the right function.

`pal=9` is `PaletteType::RLE_EFFECT`, which is why `coloringData` is a `colorShift[...].data`
pointer at all (`CImage.cpp:81`). It stayed constant across attaches within a run, as a remap
table should.

### Custom render function

**[BUILT]** — `BEAM_USE_CUSTOM_RENDER` in `Beam.h`, on by default. First version; blending is
not in it yet.

The beam overlay's `renderFunction` now points at `beamRenderFunction` in `Beam.cpp`, which
draws the beam directly into `gameScreenBuffer` instead of letting the engine blit the
generated GRP. What that buys, and what it costs:

- **The 255px ceiling is gone.** The beam no longer travels through a GRP frame's byte-sized
  width/height on its way to the screen. `spawnBeamOverlay` now keeps two lengths: `reach`,
  the real distance to the target, which is what gets drawn; and `length`, clamped to the
  canvas, which is what still gets rasterized into the GRP.
- **The GRP is still generated**, and still owns the image's bounds, the frame count the
  iscript animates through, and a valid — if short — fallback if our function is ever not
  attached. It is no longer what appears on screen.
- **Depth, culling and the image budget are unchanged**, because it is still one real `CImage`
  on a real `CSprite`. That was the whole point of preferring this over shapes.

Three details worth knowing before editing it:

- **Finding the beam from a `GrpFrame*`.** The render function is handed no image pointer, so
  the frame's *address* is the key: it points inside the GRP allocation that beam owns, and
  `findBeamForFrame` scans the ring slots for the one whose allocation contains it. The
  alternative — reading `ESI`, which the return-site disassembly shows holds the `CImage` — is
  an artifact of the caller's register allocation, not a contract, so it is not relied on.
  A frame that matches nothing simply is not drawn.
- **Map coordinates, not the arguments.** `screenX`/`screenY` arrive as arguments and are
  deliberately ignored. The endpoints are stored in map space and converted with
  `*screenX` / `*screenY` (`scbwdata.h:79-80`), the same globals `Shape.cpp:167` uses, so
  nothing depends on what a 255px canvas anchors its `screenPosition` to.
- **The fade still comes from the iscript.** The frame index is read off the frame pointer the
  engine passed, so the animation is still the engine's; only the pixels are ours.

#### What the first run of the blitter established

**[BUILT]** — measured, from the `rct` and `shift` dumps.

**`rctDraw` is not a screen clip rectangle.** For a shot whose frame bounding box works out to
125×59, it read `{0, 0, 125, 59}` as four `s32` — the frame's own extent, in frame-local
coordinates, which is what an unclipped source rect for the blit looks like. Using it to clip
*screen* coordinates confined the beam to a 125×59 box in the screen's top-left corner, which
is exactly what the first build did.

So the blitter clips to the surface instead. That is safe across the whole 640×480: the render
function runs in the sprite pass and the console is composited over the game afterwards, so
pixels underneath it are covered rather than left showing.

**`ColorShiftData::index` is just the `ColorRemapping` enum value** (`ofire` → 1, `bfire` → 3),
not anything about the table's shape. It says nothing about the stride.

**`coloringData` is `colorShift[OFire].data`, exactly.** The dump read
`color=394008C` and `ofire d=394008C` in the same frame — which is what
`images_dat::Remapping[Explosion2_Small]` should produce, and closes out any doubt about which
argument is the remap table.

#### The colour problem

`generateBeam()` fills the beam with `{47, 45, 27, 27, 17, 11, 10, 10, 5, 5}`, and those are
**not palette entries**. Under `PaletteType::RLE_EFFECT` the engine reads a GRP pixel as a
*shift level* and looks the real colour up in `coloringData`. The fire look the GRP path had
was the engine's blending, not the numbers.

Writing those numbers straight to the surface writes whatever the palette holds at index 47,
which is why the first blitter build drew a green beam. It now writes a flat fire-ish ramp as
a placeholder. Restoring the real look means `dst = remap[...]`, and that needs the table's
shape.

Still open:

- **[VERIFY]** The remap table's orientation and stride. Two forms are plausible and they are
  distinguishable, which is what `reportBeamRenderDebug` now tests in game:
  - `table[shift * stride + dst]`, one row per shift level. Row 0 is "no shift", so the first
    256 bytes read as the identity.
  - `table[dst * stride + shift]`, one row per destination colour. Column 0 is "no shift", so
    `table[d * stride] == d` for every `d` — an invariant that only holds for the right
    stride, so it yields the stride as well as the orientation.

  Candidates start at 48 because `generateBeam` emits shift levels up to 47 and the engine
  blitted those through this table for weeks without reading off the end of it.

Incidental finding: on the current GRP path, `overlay->setRemapping(ColorRemapping::BFire)`
after `createTopOverlay` switches the glow table. Without it the beam inherits whatever
`images_dat::Remapping[Explosion2_Small]` specifies.

### Superseded framing: shapes

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

Ordered when the GRP was still what got drawn. 4.1 is superseded; 4.2 is the live next step,
and 4.3 is where data editing enters.

### 4.1 Bounding-box-relative rendering — **superseded**

**[PROPOSED]**, and no longer the way out of §3.1. It aimed at reach by rasterizing the beam's
tight bounding box and positioning it with the frame's own x/y offset, buying ~360px on a
diagonal. The custom render function takes the beam out of the GRP entirely, so reach is no
longer bounded at all and this buys nothing toward it.

One idea in it is still live and applies to the GRP that remains (bounds and fallback only):
clear and encode only the bbox rather than the whole 255×255 canvas, and skip regeneration
when the endpoints have not moved. That is §3.3's cost, not §3.1's ceiling — worth doing when
the per-shot allocation shows up in practice, not before.

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
