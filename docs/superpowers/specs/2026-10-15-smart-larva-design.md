# Smart larva morphing (SC2's one-per-press morph, one morph table)

Status: `[BUILT 2026-10-15]`, not yet run in game. Plan (with the exe facts that settled
§3.1's `[VERIFY]` items): `docs/superpowers/plans/2026-10-15-smart-larva.md`. Planning
found one more site: cancel's guard for unfinished units (0x4682E5), which must keep a
Zergling back from its egg, or a Baneling hatching, from being cancelled and refunded a
second time.
Asked for by the user on 2026-10-15. SC2 is the reference; the user checks SC2's behaviour.
There is no KYSXD tutorial for this (his wiki has none).

## 1. Goal

- A plain press of a morph button (click or hotkey) morphs **one** of the selected units, as
  in SC2, instead of every one that can (vanilla BW). **Shift+press** morphs every one that
  can, as vanilla does.
- The press picks the unit so repeated presses spread over the Hatcheries.
- Which unit may morph, and through which egg, comes from **one table** that the user edits
  to add morphs. The first addition is Zergling → Baneling (the user's data, §6).

Synced game state: who morphs is decided on every client from the command (§4).

## 2. What the player sees

### 2.1 One per press

Morph buttons are those whose action is Unit_Morph (0x423790): a larva's units, Lurker,
Guardian, Devourer, and any later morph the table allows (§3).

- **Plain press:** one selected unit that can make the type starts morphing. Nothing
  morphs if the picked one can't be afforded (money or supply); the game's own "not enough"
  message shows, as now.
- **Shift+press:** every selected unit that can make the type starts morphing, as far as
  money and supply allow (vanilla's behaviour, unchanged).

Building morphs (Hatchery → Lair, Spire → Greater Spire…: command 0x35) are not touched.

### 2.2 Which unit a plain press uses

Among the selected units that can make the type (the same test as Shift's, §3):

1. A **larva** is grouped with its Hatchery (`connectedUnit`); a larva whose Hatchery is
   gone is a group of its own. Any other unit is a group of its own.
2. The pick is the first unit, in selection order, of the **largest group**. Ties go to the
   group whose first unit comes first in the selection.

So with larvae of three Hatcheries selected (3, 2, 1), presses take H1, then H1 or H2 by
selection order, and so on: the Hatcheries that are full (3 larvae, not regenerating) are
used first. A morphing larva becomes an Egg, which is no longer a candidate.

The user chose "most selected larvae", not "most larvae the Hatchery has": with Select
Larva or a box over a Hatchery they are the same.

### 2.3 What does not change

- AI players morph by their own code, not by these commands.
- What a unit may become is still the requirement data (`canMakeUnit`, FireGraft's
  requirements), except Hydralisk → Lurker, which vanilla gates on the Lurker Aspect tech.

## 3. The morph table

One table, in `SCBW/unit_morphs.cpp`, is the only place to edit to make a unit morphable.
A row is:

| Field | Meaning |
|---|---|
| source | the unit that morphs |
| egg | the egg it becomes while morphing: Egg (36), Cocoon (59) or Lurker Egg (97) |
| techTarget, tech | if set, the source may morph only into techTarget, and only with tech researched (Hydralisk → Lurker, Lurker Aspect); else any type `canMakeUnit` allows |

Rows at build time:

| source | egg | tech |
|---|---|---|
| Larva (35) | Egg (36) | none |
| Hydralisk (38) | Lurker Egg (97) | Lurker (103), Lurker Aspect |
| Mutalisk (43) | Cocoon (59) | none |
| Zergling (37) | Lurker Egg (97) | none |

The egg must be one of the three existing egg units: about 20 places in the exe know those
three (AI, console, cancel, birth, the progress percentage), and a new egg unit would need
each of them hooked (the user chose to reuse Lurker Egg for the Baneling, 2026-10-15). A
separate Baneling cocoon is a later job (§8).

The Zergling row is harmless before the user's data exists: a Zergling can make nothing
until a requirement says "current unit is Zergling".

### 3.1 The exe's sites that read the table

Each hardcodes Larva/Hydralisk/Mutalisk (or their eggs) today and is rerouted:

1. **The morph receive** 0x4C1990 (command 0x23): reimplemented with the table (from
   upstream's `CMDRECV_Morph.cpp`, whose hook stays off), so vanilla types behave exactly
   as before. The new 0x3F shares its per-unit steps.
2. **The Morph1 order** (0x45DEA0): its "may morph" check and its egg choice read the
   table: the check at 0x45DFB0 and the egg choice at 0x45E019 (settled in the plan).
3. **The button condition** 0x428E60 (BTNSCOND_CanBuildUnit): with more than one unit
   selected it shows the button only for Larva/Mutalisk/Hydralisk; smart-build already
   patches that gate (workers may build too). It now also accepts any table source.
4. **Cancel** 0x468280: an egg that is not Egg reverts to the unit it was
   (`previousUnitType`), not to a hardcoded Hydralisk or Mutalisk, so a cancelled Baneling
   is a Zergling again. The revert is picked at 0x468351; 0x49F170 writes the old id to
   `previousUnitType` on every type change, so an egg's is its source (the plan).
   Its guard for unfinished units (0x4682E5) also reads the table: a source back from its
   egg, or a unit hatching from a Cocoon or Lurker Egg, is not cancelled again.
5. **Cancel Last** (`selext::cancelsTraining`): its Larva/Hydralisk/Mutalisk become "a
   table source".

The other compares of these ids in the exe are the AI's, the console's and the egg
compares that reusing Lurker Egg keeps right (checked in planning, 2026-10-15).

Not rerouted, because reusing Lurker Egg keeps them right: the egg compares (birth offset,
no rally after a Lurker Egg, AI, console progress, the progress percentage 0x4669E0).

## 4. Commands (synced)

- **Send:** the morph button's action 0x423790 builds `0x23 [u16 type]` and sends it at
  0x423846 (`call 0x485BD0`, ECX = the 3 bytes, EDX = 3). A stub there reads
  `IS_HOLDING_SHIFT` (0x596A28, the byte the button dispatcher passes to every action):
  without Shift the first byte becomes **0x3F**; with Shift it stays 0x23.
- **0x3F** `[u16 type]`, 3 bytes: morph one (§2.2). It goes through dispatch slot 50 of the
  executor 0x4865D0 with 0x3C/0x3D/0x3E (`sel_inject.cpp`), and its length is in
  `selext::variableCommandLength`.
- **0x23** keeps its meaning (morph all): replays recorded before this play as before,
  except that a plain press recorded now is 0x3F.
- Both iterate the command player's selection (`selsync::nextSelected`), as vanilla's
  receive does.
- Nothing is saved: no new state, the save chunk stays at version 4.
- 0x3F is untested in multiplayer, like 0x3D and 0x3E.

## 5. Code

- `SCBW/unit_morphs.{h,cpp}`: the table and its pure lookups (`morphRowOf`, `eggOf`,
  `isMorphSource`, `revertsOnCancel`), host-tested.
- `SCBW/smart_larva.{h,cpp}`: the pick rule (§2.2) over a list of (group key) candidates,
  host-tested.
- `hooks/recv_commands/smart_morph.{h,cpp}` and `smart_morph_inject.cpp`: the receive of
  0x23 and 0x3F, the send stub, and the table's exe sites (§3.1); `injectSmartMorphHooks()`
  in `initialize.cpp`'s enabled list. Its object joins the naked-wrapper check in
  `tests/verify.ps1`.
- `tests/unit_morphs_test` and `tests/smart_larva_test` (or one test for both), run by
  `verify.ps1`.

## 6. The user's data (not code)

For Zergling → Baneling: the Baneling unit (a unit slot, with Morph From Other Unit like
the Lurker so the supply check works as the Lurker's), its requirement ("current unit is
Zergling", plus whatever tech), and a Baneling button on the Zergling's button set (action
Unit_Morph 0x423790, condition 0x428E60, actVar the Baneling's id).

## 7. Testing

Host: the table lookups and the pick rule (§2.2's 3/2/1 example, ties, a larva without a
Hatchery, non-larva sources in selection order, none eligible).

In game (rounds the user runs):

1. Select larvae of one Hatchery, press Drone: one larva morphs. Again: a second.
2. Shift+press Zergling with three larvae selected: all three morph.
3. Larvae of two or three Hatcheries (box): presses take from the Hatchery with the most
   first, then spread.
4. Not enough minerals: a plain press shows the message, nothing morphs.
5. Several Hydralisks, press Lurker: one morphs; Shift: all.
6. Several Mutalisks, Guardian: one; Shift: all.
7. Cancel a Lurker Egg: it is a Hydralisk again; cancel a Cocoon: a Mutalisk again.
8. A replay of a game with plain and Shift presses plays the same.
9. When the user's Baneling data exists: Zerglings show the Baneling button with several
   selected; one per press; the egg is a Lurker Egg; cancel gives the Zergling back; the
   Baneling hatches where the Zergling was.

## 8. Later

- A separate Baneling cocoon unit (SC2's look): needs the exe's egg sites (§3) hooked.
- Morphing larvae straight from a selected Hatchery (offered, not chosen, 2026-10-15).
