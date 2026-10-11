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
          TERRAN_BUILD_SELF = 0x2C, ZERG_BUILD_SELF = 0x2D, PROTOSS_BUILD_SELF = 0x30;
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

	if (failures != 0) {
		printf("FAIL: %u checks (first at build_progress_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
