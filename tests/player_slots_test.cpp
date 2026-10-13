//Host test of the per-player slots (SCBW/player_slots.h). Build and run with
//player_slots_test.bat (MSVC x86); it needs no game.
#include <SCBW/player_slots.h>
#include <cstdio>
#include <cstring>
#include <new>

using namespace playerslots;

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

//Slots between two guards, built in memory that is not zero to begin with,
//as a Debug build's stack is.
struct Guarded {
	u8 before[32];
	Slots<int> slots;
	u8 after[64];
};

bool guardsKept(const Guarded& g) {
	for (u32 i = 0; i < sizeof(g.before); i++)
		if (g.before[i] != 0xCC)
			return false;
	for (u32 i = 0; i < sizeof(g.after); i++)
		if (g.after[i] != 0xCC)
			return false;
	return true;
}

} //unnamed namespace

int main() {
	static u8 raw[sizeof(Guarded)];
	memset(raw, 0xCC, sizeof(raw));
	Guarded* const g = new (raw) Guarded;
	memset(g->before, 0xCC, sizeof(g->before));
	memset(g->after, 0xCC, sizeof(g->after));
	Slots<int>& slots = g->slots;

	//nothing kept yet: every playing player reads as none (a player without
	//a resource depot is never given a mineral field).
	for (u32 p = 0; p < PLAYING_PLAYERS; p++)
		CHECK(slots.get(p) == NULL);

	//what is kept for a player is that player's alone.
	int a = 1, b = 2;
	slots.set(3, &a);
	slots.set(7, &b);
	CHECK(slots.get(3) == &a && slots.get(7) == &b);
	CHECK(slots.get(0) == NULL && slots.get(2) == NULL && slots.get(4) == NULL && slots.get(6) == NULL);
	slots.set(3, &b);
	CHECK(slots.get(3) == &b);
	slots.set(3, NULL);
	CHECK(slots.get(3) == NULL);

	//the players past the playing ones (a neutral Command Center is player
	//11's): nothing is kept, nothing outside the slots is written, and they
	//read as none.
	slots.set(8, &a);
	slots.set(11, &a);
	slots.set(255, &a);
	CHECK(guardsKept(*g));
	CHECK(slots.get(8) == NULL && slots.get(11) == NULL && slots.get(255) == NULL);
	CHECK(slots.get(0xFFFFFFFF) == NULL);
	CHECK(slots.get(7) == &b);

	if (failures != 0) {
		printf("FAIL: %u checks (first at player_slots_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
