//Smart rally in the game (docs/superpowers/specs/2026-10-14-smart-rally-design.md):
//the worker rally of Hatchery, Lair and Hive, what a new unit does at its
//rally, the right-click rule and the worker rally command. Synced: it runs
//on every client from synced state, except noteRallyButton and
//workerRallySendAs, which only pick the command the local player sends.
//The rules: SCBW/smart_rally.h.
#pragma once
#include <SCBW/api.h>

namespace smartrally {

//A building's worker rally, kept like the game's own rally (CUnit::rally).
struct WorkerRally {
	bool set;
	CUnit* target;	//NULL for the ground, or once its unit died
	u16 x, y;
};

//A new game: no worker rallies.
void clearAll();
//The worker rally of a Hatchery, Lair or Hive; NULL for any other unit.
WorkerRally* workerRallyOf(CUnit* building);
//Replaces 0x466F50: orders a new unit to the rally of the building that made
//it (for a larva's unit, its Hatchery).
void orderNewUnitToRally(CUnit* unit, CUnit* factory);
//A unit is dying (0x4A0740, before the game clears references to it): its
//own worker rally goes.
void unitDying(CUnit* unit);
//The game clears holder's references to dying (0x49E590): a worker rally on
//it keeps its point and loses its target, as the game's rally does (0x49E643).
void referenceCleared(CUnit* holder, CUnit* dying);

} //smartrally

namespace hooks {

void injectSmartRallyHooks();

} //hooks
