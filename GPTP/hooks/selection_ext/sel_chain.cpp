#include "sel_chain.h"
#include <cstring>

namespace {

//By unit index, as the game's unit table.
bool chained[UNIT_ARRAY_LENGTH];

//The unit's slot, or -1 for a pointer that is no unit of the table.
int slotOf(const CUnit* unit) {
	if (unit < unitTable || unit >= unitTable + UNIT_ARRAY_LENGTH)
		return -1;
	return (int)(unit - unitTable);
}

} //unnamed namespace

namespace selchain {

void reset() {
	memset(chained, 0, sizeof(chained));
}

void queued(const CUnit* unit) {
	const int slot = slotOf(unit);
	if (slot >= 0)
		chained[slot] = true;
}

void replaced(const CUnit* unit) {
	const int slot = slotOf(unit);
	if (slot >= 0)
		chained[slot] = false;
}

bool holds(const CUnit* unit) {
	const int slot = slotOf(unit);
	return slot >= 0 && chained[slot];
}

void sweep(const CUnit* unit) {
	const int slot = slotOf(unit);
	if (slot >= 0 && chained[slot] && unit->orderQueueHead == NULL)
		chained[slot] = false;
}

} //selchain
