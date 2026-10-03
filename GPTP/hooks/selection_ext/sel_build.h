//Smart-build (docs/superpowers/specs/2026-10-03-smart-build-design.md):
//several selected workers build; Shift-placement queues buildings shared out
//across them. The receive side and the order hooks are synced; the
//placement side is local.
#pragma once
#include <SCBW/api.h>

namespace selbuild {

//Synced: per unit (index - 1) the stamp of its last smart-build pick, and
//per player the last stamp given. Saved (SELX version 3).
extern u32 stamps[UNIT_ARRAY_LENGTH];
extern u32 lastStamp[8];
//Synced, per unit: what its prepaid Shift-placements cost (all it holds), and
//its current build's type if that one is prepaid (0xFFFF if not). Saved.
extern u32 paidMinerals[UNIT_ARRAY_LENGTH];
extern u32 paidGas[UNIT_ARRAY_LENGTH];
extern u16 paidCurrentType[UNIT_ARRAY_LENGTH];
//While 0x48E190 runs for a picked builder, the builder 0x48E010/0x48E0A0
//take instead of the selection's first unit; NULL otherwise.
extern CUnit* chosenBuilder;

//0x0C (0x4C23C0): the selected worker able to build it nearest the site.
void recvBuild(const u8* packet);
//0x3D: a queued build, given to the worker the balanced or Drone pick names.
void recvQueuedBuild(const u8* packet);
//Before the order dispatcher (0x4EC4D0) runs unit's order: sets up a queued
//build that just became current, or moves the unit on if it can't be made.
void beforeOrder(CUnit* unit);
//A worker about to pay for its building on arrival (SCV 0x468064, Probe
//0x4E4DF5, Drone 0x45E189): a prepaid one gets its cost back first.
void arriving(CUnit* unit);
//A build order gave up on its way (SCV 0x46817C; Probe 0x4E4D90 when it
//got stuck): the message if the worker got stuck or stopped out of reach.
void gaveUp(CUnit* unit);
//A new game: no stamps.
void reset();

//-------- Local: placing --------//

//At the placement click, before cmd (an 8-byte 0x0C) is sent: with Shift
//held and a worker build order, turns it into 0x3D and returns true (placing
//then goes on).
bool sendAsQueued(u8* cmd);
//At the end of the placement click (0x48E4E0, before the send): whether
//placing goes on instead of ending (a Shift-queued build).
bool keepsPlacing();
//Whether any member of the active subgroup can still place the building
//being placed (replaces 0x48DDA0, which asks the portrait only).
bool placementStillValid();
//Every frame: Shift released while Shift-placing ends placing; the build
//menu stays.
void frame();
//Local: the build menu (a special button set) last opened by a button
//(0x459AF0), shown again after a Shift-placement and when Shift is released.
extern u16 lastSubmenu;

} //selbuild
