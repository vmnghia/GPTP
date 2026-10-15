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
const u32 MARINE = 0, ZERGLING = 37, GUARDIAN = 44, INTERCEPTOR = 73, SCARAB = 85, LURKER = 103, BARRACKS = 111,
          HATCHERY = 131, LAIR = 132, HIVE = 133, GREATER_SPIRE = 137, SPIRE = 141, SPAWNING_POOL = 142,
          CREEP_COLONY = 143, SPORE_COLONY = 144, SUNKEN_COLONY = 146, GATEWAY = 160, NONE = 228;
const u32 DIE = 0x00, ZERG_BIRTH = 0x29;

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
	//in an egg (Egg, Cocoon, Lurker Egg): the unit morph order, not
	//completed, with the type it becomes queued.
	CHECK(isMorphingInEgg(UNIT_MORPH, false, ZERGLING));
	CHECK(isMorphingInEgg(UNIT_MORPH, false, GUARDIAN));
	CHECK(isMorphingInEgg(UNIT_MORPH, false, LURKER));
	//not in an egg: the frame the order starts, the Larva or Hydralisk still
	//completed; the unit back from a cancelled egg (its birth order); an egg
	//dying; an egg with nothing queued.
	CHECK(!isMorphingInEgg(UNIT_MORPH, true, ZERGLING));
	CHECK(!isMorphingInEgg(UNIT_MORPH, true, LURKER));
	CHECK(!isMorphingInEgg(ZERG_BIRTH, false, NONE));
	CHECK(!isMorphingInEgg(DIE, false, ZERGLING));
	CHECK(!isMorphingInEgg(UNIT_MORPH, false, NONE));

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
	//a Carrier building Interceptors or a Reaver building Scarabs: the
	//game's other order for making a unit inside another. No unit types are
	//named, so a unit that comes to produce by either order has the bar.
	CHECK(isTraining(TRAIN_FIGHTER, INTERCEPTOR));
	CHECK(isTraining(TRAIN_FIGHTER, SCARAB));
	//a Carrier keeps that order when its queue is empty (it repairs its
	//Interceptors by it): nothing queued, no bar.
	CHECK(!isTraining(TRAIN_FIGHTER, NONE));
	CHECK(!isTraining(PLAYER_GUARD, INTERCEPTOR));

	//the bar's frame: over a building, as wide as the building's box and 24
	//px above it; 5 rows tall, the border included.
	{
		//a Barracks at (1000, 800): its box reaches 48 left, 40 up, 56 right.
		const Frame f = barFrame(1000, 800, 48, 40, 56, true, false, true);
		CHECK(f.left == 952 && f.right == 1056 && f.top == 736 && f.bottom == 740);
		//the same without a queue (under construction).
		const Frame g = barFrame(1000, 800, 48, 40, 56, true, false, false);
		CHECK(g.left == 952 && g.right == 1056 && g.top == 736 && g.bottom == 740);
	}
	//the bar's frame: an add-on's is 8 px right and 4 px down of that.
	{
		const Frame f = barFrame(1000, 800, 37, 16, 31, true, true, false);
		CHECK(f.left == 971 && f.right == 1039 && f.top == 764 && f.bottom == 768);
	}
	//the bar's frame: over a unit, 16 px above its box.
	{
		//a Carrier: 32 left, 32 up, 31 right.
		const Frame f = barFrame(500, 400, 32, 32, 31, false, false, true);
		CHECK(f.left == 468 && f.right == 531 && f.top == 352 && f.bottom == 356);
	}
	//the bar's frame: with queue boxes it is at least as wide as the five of
	//them (41 px), centred on the unit.
	{
		//a Reaver: 16 left, 16 up, 15 right, narrower than its queue.
		const Frame f = barFrame(500, 400, 16, 16, 15, false, false, true);
		CHECK(f.left == 480 && f.right == 520 && f.top == 368 && f.bottom == 372);
		CHECK(f.right - f.left == QUEUE_WIDTH);
		//a narrow building that trains would get the same.
		const Frame b = barFrame(500, 400, 16, 12, 16, true, false, true);
		CHECK(b.left == 480 && b.right == 520 && b.top == 364);
	}
	//the bar's frame: without queue boxes a narrow one stays as wide as its
	//box (a Pylon warping in).
	{
		const Frame f = barFrame(500, 400, 16, 12, 16, true, false, false);
		CHECK(f.left == 484 && f.right == 516 && f.top == 364 && f.bottom == 368);
	}

	//whose bars show: the local player's own.
	CHECK(isShownFor(2, 2, false, false, true));
	//an ally's, where the local player sees the unit; not in the fog (a bar
	//there gave the unit away).
	CHECK(isShownFor(3, 2, false, true, true));
	CHECK(!isShownFor(3, 2, false, true, false));
	//never an enemy's, seen or not.
	CHECK(!isShownFor(5, 2, false, false, true));
	CHECK(!isShownFor(5, 2, false, false, false));
	//in a replay every player's.
	CHECK(isShownFor(5, 2, true, false, false));
	//never the neutral players' (a Vespene Geyser), in a replay either.
	CHECK(!isShownFor(11, 2, false, false, true));
	CHECK(!isShownFor(11, 2, true, false, true));
	CHECK(!isShownFor(8, 2, true, false, true));

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
