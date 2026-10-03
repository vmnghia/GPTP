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
//A build order gave up on its way (SCV 0x46817C, Probe 0x4E4EDB): the
//message if the worker never got within reach of the site.
void gaveUp(CUnit* unit);
//A new game: no stamps.
void reset();

} //selbuild
