//Host test of the morph table (SCBW/unit_morphs.cpp) and the smart larva pick
//(SCBW/smart_larva.cpp). Build and run with unit_morphs_test.bat (MSVC x86).
#include <SCBW/unit_morphs.h>
#include <SCBW/smart_larva.h>
#include <cstdio>

using namespace unitmorphs;
using namespace smartlarva;

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

//The game's ids (SCBW/enumerations/UnitId.h).
const u16 MARINE = 0, LARVA = 35, ZERGLING = 37, HYDRALISK = 38, MUTALISK = 43, GUARDIAN = 44, DRONE = 41,
          DEVOURING_ONE = 54, DEVOURER = 62, LURKER = 103, HATCHERY = 131;

} //unnamed namespace

int main() {
	//The table's rows.
	CHECK(isMorphSource(LARVA) && isMorphSource(HYDRALISK) && isMorphSource(MUTALISK) && isMorphSource(ZERGLING));
	CHECK(!isMorphSource(MARINE) && !isMorphSource(DRONE) && !isMorphSource(HATCHERY) && !isMorphSource(LURKER_EGG));
	CHECK(morphRowOf(MARINE) == NULL && morphRowOf(LARVA) != NULL && morphRowOf(LARVA)->source == LARVA);
	CHECK(eggOf(LARVA) == EGG && eggOf(HYDRALISK) == LURKER_EGG && eggOf(MUTALISK) == COCOON);
	CHECK(eggOf(ZERGLING) == LURKER_EGG && eggOf(MARINE) == NONE);

	//How a morph into a type is tested.
	CHECK(morphCheck(LARVA, DRONE) == CHECK_MAKE && morphCheck(MUTALISK, GUARDIAN) == CHECK_MAKE);
	CHECK(morphCheck(HYDRALISK, LURKER) == CHECK_TECH);
	CHECK(morphCheck(HYDRALISK, GUARDIAN) == CHECK_NONE);	//a Hydralisk becomes only a Lurker
	CHECK(morphCheck(MARINE, DRONE) == CHECK_NONE && morphCheck(HATCHERY, DRONE) == CHECK_NONE);
	CHECK(morphRowOf(HYDRALISK)->tech == 0x20);				//Lurker Aspect
	//The Baneling (Devouring One, 54): only it, once tech 35 is researched.
	//Tech 35 has no use requirements in the data, so it is not the game's
	//can-use test (0x46DD80) but the research itself.
	CHECK(morphCheck(ZERGLING, DEVOURING_ONE) == CHECK_RESEARCHED);
	CHECK(morphCheck(ZERGLING, DRONE) == CHECK_NONE && morphCheck(ZERGLING, LURKER) == CHECK_NONE);
	CHECK(morphRowOf(ZERGLING)->tech == 35);

	//What a cancelled egg becomes.
	CHECK(cancelRevert(LURKER_EGG, HYDRALISK) == HYDRALISK);
	CHECK(cancelRevert(LURKER_EGG, ZERGLING) == ZERGLING);
	CHECK(cancelRevert(COCOON, MUTALISK) == MUTALISK);
	CHECK(cancelRevert(LURKER_EGG, MARINE) == HYDRALISK);	//not a source of that egg: vanilla's
	CHECK(cancelRevert(COCOON, ZERGLING) == MUTALISK);
	CHECK(cancelRevert(EGG, LARVA) == NONE);				//removed, as vanilla
	CHECK(cancelRevert(MARINE, LARVA) == NONE);

	//An unfinished unit cancel leaves alone.
	CHECK(keptFromCancel(HYDRALISK, MARINE) && keptFromCancel(MUTALISK, MARINE));	//vanilla's
	CHECK(keptFromCancel(ZERGLING, LURKER_EGG));		//back from its egg
	CHECK(keptFromCancel(DEVOURING_ONE, LURKER_EGG));	//a Baneling hatching
	CHECK(keptFromCancel(GUARDIAN, COCOON) && keptFromCancel(DEVOURER, COCOON) && keptFromCancel(LURKER, LURKER_EGG));
	CHECK(!keptFromCancel(LARVA, MARINE));				//a larva's egg is removed, never reverts
	CHECK(!keptFromCancel(MARINE, MARINE) && !keptFromCancel(DRONE, EGG));	//as vanilla

	//The command a press sends.
	CHECK(morphCommandId(true) == CMD_MORPH_ALL && CMD_MORPH_ALL == 0x23);
	CHECK(morphCommandId(false) == CMD_MORPH_ONE && CMD_MORPH_ONE == 0x3F && MORPH_ONE_BYTES == 3);

	//The pick: the first of the largest group, ties to the group met first.
	const u32 h1 = 1000, h2 = 2000, h3 = 3000;
	const u32 threeTwoOne[] = { h3, h2, h1, h2, h1, h1 };
	CHECK(pickMorpher(threeTwoOne, 6) == 2);			//H1 has 3: its first is at 2
	const u32 tie[] = { h2, h1, h1, h2 };
	CHECK(pickMorpher(tie, 4) == 0);					//2 and 2: H2 comes first
	const u32 alone[] = { 11, 12, 13 };					//no Hatchery, or Hydralisks: each its own
	CHECK(pickMorpher(alone, 3) == 0);
	const u32 one[] = { h1 };
	CHECK(pickMorpher(one, 1) == 0);
	CHECK(pickMorpher(one, 0) == 0);					//none: n
	const u32 later[] = { 11, h1, h1 };
	CHECK(pickMorpher(later, 3) == 1);

	if (failures != 0) {
		printf("FAIL: %u checks (first at unit_morphs_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
