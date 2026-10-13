//Smart rally's rules (docs/superpowers/specs/2026-10-14-smart-rally-design.md):
//which rally a new unit uses, what it does there, what a right-click sets,
//and the worker rally command. Pure, so the host test covers it
//(tests/smart_rally_test.cpp); hooks/rally/smart_rally_hooks.cpp applies it.
#pragma once
#include "../types.h"

namespace smartrally {

//Set Worker Rally Point's command: [0x3E][u16 x][u16 y][u16 target tag], tag
//0 for the ground. Beside the plugin's 0x3C and 0x3D (SCBW/selection_ext.h).
const u8 CMD_WORKER_RALLY = 0x3E;
const u32 WORKER_RALLY_BYTES = 7;

//Hatchery, Lair and Hive: a normal rally and a worker rally.
bool hasTwoRallies(u16 buildingId);

//A resource for a two-rally building's right-click: a mineral field, a
//Vespene Geyser, or a Refinery, Assimilator or Extractor of the building's
//owner, finished or not.
bool isRallyResource(u16 targetId, bool sameOwner);

enum RightClickSets { SETS_NORMAL, SETS_WORKER, SETS_BOTH };
//What a right-click with a two-rally building sets: the ground or a unit that
//is not a resource, the normal rally; a resource, the worker rally; the
//resource the worker rally is already on, the normal rally too.
RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget);

enum RallySource { USE_NORMAL, USE_WORKER, USE_NONE };
//Which rally a new unit uses: a worker of a two-rally building its worker
//rally, or none (it stays) while there is none; everything else the normal
//rally.
RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet);

//Whether a worker rally counts as set: not while it is on the building
//itself, which is none, as for the game's rally (0x466F50). So pointing Set
//Worker Rally Point at the Hatchery clears it.
bool workerRallyCounts(bool set, bool onBuildingItself);

enum RallyAction { ACT_VANILLA, ACT_MINERALS, ACT_GAS, ACT_ENTER };
//What a new unit does at its rally target: gather where its right-click
//gathers (a mineral field; a finished gas building of its owner, the rule of
//0x468930), else go in where its right-click loads it, else vanilla's Follow
//or Move.
RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads);

//A rally button's click, the targeted command 0x15 ([0x15][u16 x][u16 y]
//[u16 tag][u16 type][u8 order][u8 queued], built at 0x4C0300), as the worker
//rally command. False, with out untouched, for any other command or order.
bool toWorkerRallyCommand(const u8* targeted, u8* out);

} //smartrally
