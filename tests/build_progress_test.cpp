//Host test of the build progress rules (SCBW/build_progress.cpp). Build and
//run with build_progress_test.bat (MSVC x86); it needs no game.
#include <SCBW/build_progress.h>
#include <cstdio>

using namespace buildprogress;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

//The game's order ids (SCBW/enumerations/OrderId.h).
const u32 PLAYER_GUARD = 0x03, NOTHING = 0x17, TRAIN = 0x26, UNIT_MORPH = 0x2A, BUILDING_MORPH = 0x2B,
          TERRAN_BUILD_SELF = 0x2C, ZERG_BUILD_SELF = 0x2D, PROTOSS_BUILD_SELF = 0x30, TRAIN_FIGHTER = 0x3F,
          RESEARCH = 0x4B, UPGRADE = 0x4C;
//The game's tech and upgrade ids (TechId.h, UpgradeId.h): the last of each,
//and the value for none.
const u32 STIM_PACKS = 0, LAST_TECH = 43, NO_TECH = 44, INFANTRY_ARMOR = 0, LAST_UPGRADE = 60, NO_UPGRADE = 61;
//The game's unit ids (SCBW/enumerations/UnitId.h).
const u32 MARINE = 0, ZERGLING = 37, BARRACKS = 111, HATCHERY = 131, LAIR = 132, HIVE = 133,
          GREATER_SPIRE = 137, SPIRE = 141, SPAWNING_POOL = 142, CREEP_COLONY = 143, SPORE_COLONY = 144,
          SUNKEN_COLONY = 146, GATEWAY = 160, NONE = 228;

} //unnamed namespace

int main() {
	//being built: each race's order for a building under construction.
	CHECK(isBeingBuilt(TERRAN_BUILD_SELF, 400, false));
	CHECK(isBeingBuilt(PROTOSS_BUILD_SELF, 400, false));
	//a Zerg building, grown from a Drone or morphing into its next form
	//(Hatchery to Lair): the same order for both. It got no bar.
	CHECK(isBeingBuilt(ZERG_BUILD_SELF, 400, false));
	//at the end, with no time left (the Protoss warp-in still plays).
	CHECK(isBeingBuilt(PROTOSS_BUILD_SELF, 0, false));
	CHECK(isBeingBuilt(ZERG_BUILD_SELF, 0, false));

	//not being built: the order that starts a Zerg building's morph. For
	//its one frame the building is still complete and has no build time, so
	//a bar there showed full, then went.
	CHECK(!isBeingBuilt(BUILDING_MORPH, 0, true));
	CHECK(!isBeingBuilt(BUILDING_MORPH, 0, false));
	//not being built: a finished building, idle or training; a unit morphing.
	CHECK(!isBeingBuilt(PLAYER_GUARD, 0, true));
	CHECK(!isBeingBuilt(TRAIN, 0, true));
	CHECK(!isBeingBuilt(UNIT_MORPH, 300, false));

	//no order: being built while there is build time left and the unit is
	//not completed (a Vespene Geyser left by a destroyed Refinery is
	//completed and keeps its build time).
	CHECK(isBeingBuilt(NOTHING, 400, false));
	CHECK(!isBeingBuilt(NOTHING, 400, true));
	CHECK(!isBeingBuilt(NOTHING, 0, false));

	//the type built: a Zerg building morphing keeps its old type and has the
	//new one in its queue; the build time is the new one's.
	CHECK(typeBeingBuilt(HATCHERY, false, LAIR) == LAIR);
	CHECK(typeBeingBuilt(LAIR, false, HIVE) == HIVE);
	CHECK(typeBeingBuilt(SPIRE, false, GREATER_SPIRE) == GREATER_SPIRE);
	CHECK(typeBeingBuilt(CREEP_COLONY, false, SUNKEN_COLONY) == SUNKEN_COLONY);
	CHECK(typeBeingBuilt(CREEP_COLONY, false, SPORE_COLONY) == SPORE_COLONY);
	//the type built: a building under construction is its own type, with
	//its own type or nothing in the queue.
	CHECK(typeBeingBuilt(HATCHERY, false, HATCHERY) == HATCHERY);
	CHECK(typeBeingBuilt(SPAWNING_POOL, false, NONE) == SPAWNING_POOL);
	CHECK(typeBeingBuilt(BARRACKS, false, NONE) == BARRACKS);
	CHECK(typeBeingBuilt(GATEWAY, false, NONE) == GATEWAY);
	//the type built: a queue entry that is no morph target is not it (the
	//game names the five).
	CHECK(typeBeingBuilt(HATCHERY, false, ZERGLING) == HATCHERY);
	CHECK(typeBeingBuilt(BARRACKS, false, MARINE) == BARRACKS);
	//the type built: a completed building is its own type whatever it has
	//queued (a Hatchery in the frame its morph starts; a Barracks training).
	CHECK(typeBeingBuilt(HATCHERY, true, LAIR) == HATCHERY);
	CHECK(typeBeingBuilt(BARRACKS, true, MARINE) == BARRACKS);

	//researching: the order, and a tech to research.
	CHECK(isResearching(RESEARCH, STIM_PACKS));
	CHECK(isResearching(RESEARCH, LAST_TECH));
	//a cancel takes the tech away at once and leaves the order to end on its
	//next run: nothing is being researched in between (the bar showed full).
	CHECK(!isResearching(RESEARCH, NO_TECH));
	CHECK(!isResearching(PLAYER_GUARD, STIM_PACKS));
	CHECK(!isResearching(UPGRADE, STIM_PACKS));

	//upgrading: the same, with the upgrade.
	CHECK(isUpgrading(UPGRADE, INFANTRY_ARMOR));
	CHECK(isUpgrading(UPGRADE, LAST_UPGRADE));
	CHECK(!isUpgrading(UPGRADE, NO_UPGRADE));
	CHECK(!isUpgrading(PLAYER_GUARD, INFANTRY_ARMOR));
	CHECK(!isUpgrading(RESEARCH, INFANTRY_ARMOR));

	//training: the order, and a unit in the queue's active slot.
	CHECK(isTraining(TRAIN, MARINE));
	CHECK(isTraining(TRAIN, ZERGLING));
	//the last unit cancelled, or done: the queue is empty and the order ends
	//a frame or two later (the bar showed empty in between).
	CHECK(!isTraining(TRAIN, NONE));
	CHECK(!isTraining(NOTHING, MARINE));
	//a Carrier building Interceptors has another order, and no bar.
	CHECK(!isTraining(TRAIN_FIGHTER, MARINE));

	//the fill: elapsed of the time, as a part of the bar's width.
	CHECK(fillWidth(0, 100, 50) == 0);
	CHECK(fillWidth(50, 100, 50) == 25);
	CHECK(fillWidth(100, 100, 50) == 50);
	CHECK(fillWidth(1, 3, 100) == 33);
	CHECK(fillWidth(2, 3, 100) == 66);
	//the fill: never outside the bar, whatever the times say.
	CHECK(fillWidth(150, 100, 50) == 50);
	CHECK(fillWidth(-20, 100, 50) == 0);
	//the fill: a time of 0 (data can say so) is done, not a division by 0.
	CHECK(fillWidth(0, 0, 50) == 50);
	CHECK(fillWidth(7, 0, 50) == 50);
	//the fill: the longest time on the widest bar.
	CHECK(fillWidth(65535, 65535, 2000) == 2000);
	CHECK(fillWidth(65534, 65535, 2000) == 1999);
	//the fill: a bar with no width.
	CHECK(fillWidth(50, 100, 0) == 0);

	if (failures != 0) {
		printf("FAIL: %u checks (first at build_progress_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
