//Host test of the smart rally rules (SCBW/smart_rally.cpp). Build and run
//with smart_rally_test.bat (MSVC x86); it needs no game.
#include <SCBW/smart_rally.h>
#include <cstdio>
#include <cstring>

using namespace smartrally;

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

//The game's unit ids (SCBW/enumerations/UnitId.h).
const u16 MARINE = 0, OVERLORD = 42, CC = 106, REFINERY = 110, INFESTED_CC = 130, HATCHERY = 131, LAIR = 132,
          HIVE = 133, EXTRACTOR = 149, NEXUS = 154, ASSIMILATOR = 157, MINERAL_1 = 176, MINERAL_2 = 177,
          MINERAL_3 = 178, GEYSER = 188;
//The game's order ids (SCBW/enumerations/OrderId.h).
const u8 ATTACK_UNIT = 0x0A, RALLY_UNIT = 0x27, RALLY_POINT = 0x28;

} //unnamed namespace

int main() {
	//Two rallies: Hatchery, Lair, Hive only.
	CHECK(hasTwoRallies(HATCHERY) && hasTwoRallies(LAIR) && hasTwoRallies(HIVE));
	CHECK(!hasTwoRallies(CC) && !hasTwoRallies(NEXUS) && !hasTwoRallies(INFESTED_CC));

	//Resources: minerals and geysers of anyone, gas buildings of the owner.
	CHECK(isRallyResource(MINERAL_1, false) && isRallyResource(MINERAL_2, true) && isRallyResource(MINERAL_3, false));
	CHECK(isRallyResource(GEYSER, false) && isRallyResource(GEYSER, true));
	CHECK(isRallyResource(REFINERY, true) && isRallyResource(EXTRACTOR, true) && isRallyResource(ASSIMILATOR, true));
	CHECK(!isRallyResource(REFINERY, false) && !isRallyResource(EXTRACTOR, false) && !isRallyResource(ASSIMILATOR, false));
	CHECK(!isRallyResource(MARINE, true) && !isRallyResource(CC, true));

	//The right-click table (spec 2.3).
	CHECK(rightClickSets(false, false, false) == SETS_NORMAL);	//the ground
	CHECK(rightClickSets(true, false, false) == SETS_NORMAL);	//a unit, not a resource
	CHECK(rightClickSets(true, false, true) == SETS_NORMAL);
	CHECK(rightClickSets(true, true, false) == SETS_WORKER);	//a resource
	CHECK(rightClickSets(true, true, true) == SETS_BOTH);		//the resource the worker rally is on

	//Which rally a new unit uses.
	CHECK(rallySource(false, true, true) == USE_NORMAL);		//an SCV of a CC
	CHECK(rallySource(false, false, false) == USE_NORMAL);
	CHECK(rallySource(true, false, true) == USE_NORMAL);		//a Zergling of a Hatchery
	CHECK(rallySource(true, true, true) == USE_WORKER);		//a Drone, worker rally set
	CHECK(rallySource(true, true, false) == USE_NONE);		//a Drone, none: it stays

	//What it does there.
	CHECK(rallyAction(true, MINERAL_1, true, false, false) == ACT_MINERALS);
	CHECK(rallyAction(true, MINERAL_3, true, false, false) == ACT_MINERALS);
	CHECK(rallyAction(true, EXTRACTOR, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, REFINERY, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, ASSIMILATOR, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, EXTRACTOR, false, true, false) == ACT_VANILLA);	//unfinished
	CHECK(rallyAction(true, EXTRACTOR, true, false, false) == ACT_VANILLA);	//an enemy's
	CHECK(rallyAction(true, GEYSER, true, false, false) == ACT_VANILLA);		//bare
	CHECK(rallyAction(false, MINERAL_1, true, false, false) == ACT_VANILLA);	//not a worker
	CHECK(rallyAction(false, CC, true, true, true) == ACT_ENTER);				//a Bunker or transport that loads it
	CHECK(rallyAction(true, OVERLORD, true, true, true) == ACT_ENTER);			//a Drone into an Overlord
	CHECK(rallyAction(false, CC, true, true, false) == ACT_VANILLA);			//full, or a hallucination
	CHECK(rallyAction(true, MINERAL_2, true, false, true) == ACT_MINERALS);	//gathering first

	//The rally button's click as the worker rally command.
	const u8 onUnit[11] = { 0x15, 0x34, 0x12, 0x78, 0x56, 0x05, 0x08, 0xE4, 0x00, RALLY_UNIT, 0 };
	u8 out[WORKER_RALLY_BYTES];
	CHECK(toWorkerRallyCommand(onUnit, out));
	const u8 expectUnit[WORKER_RALLY_BYTES] = { CMD_WORKER_RALLY, 0x34, 0x12, 0x78, 0x56, 0x05, 0x08 };
	CHECK(memcmp(out, expectUnit, WORKER_RALLY_BYTES) == 0);
	const u8 onGround[11] = { 0x15, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00, 0xE4, 0x00, RALLY_POINT, 0 };
	CHECK(toWorkerRallyCommand(onGround, out));
	const u8 expectGround[WORKER_RALLY_BYTES] = { CMD_WORKER_RALLY, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00 };
	CHECK(memcmp(out, expectGround, WORKER_RALLY_BYTES) == 0);
	//Any other order (Tab to another subgroup, then Attack): sent as it is.
	const u8 attack[11] = { 0x15, 1, 0, 2, 0, 3, 0, 0xE4, 0x00, ATTACK_UNIT, 0 };
	memset(out, 0xAA, sizeof(out));
	CHECK(!toWorkerRallyCommand(attack, out) && out[0] == 0xAA);
	const u8 rightClick[10] = { 0x14, 1, 0, 2, 0, 3, 0, 0xE4, 0x00, 0 };
	CHECK(!toWorkerRallyCommand(rightClick, out) && out[0] == 0xAA);

	if (failures != 0) {
		printf("FAIL: %u checks (first at smart_rally_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
