#include "sel_subgroups.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u32 keys[SEL_MAX];			//of clientSel, sorted with it
CUnit* previous[SEL_MAX];	//the selection at the last sort
u32 previousCount;
u32 activeKey;

u16 priorityOf(u16 type) {
	if (type >= UNIT_TYPES)
		return 0;
	return defaultPriority((units_dat::BaseProperty[type] & UnitProperty::Hero) != 0,
	                       units_dat::BuildScore[type]);
}

} //unnamed namespace

namespace selsub {

u32 keyOf(CUnit* unit) {
	const u16 type = subgroupType(unit->id);
	return subgroupKey(priorityOf(type), type, (unit->status & UnitStatus::IsHallucination) != 0);
}

CUnit* sortAndPickLeader(u32 n) {
	if (n == 0) {
		previousCount = 0;
		return NULL;
	}
	for (u32 i = 0; i < n; i++)
		keys[i] = keyOf(clientSel[i]);
	sortBySubgroup(clientSel, keys, n);
	const bool keep = previousCount != 0 && keepsActive(previous, previousCount, clientSel, n);
	activeKey = activeKeyAfter(keys, n, activeKey, keep);
	memcpy(previous, clientSel, n * sizeof(CUnit*));
	previousCount = n;
	CUnit* leader = NULL;
	for (u32 i = 0; i < n; i++)
		if (keys[i] == activeKey && selexe::outranks(clientSel[i], leader))
			leader = clientSel[i];
	return leader;
}

bool cycle(bool back) {
	if (clientCount == 0)
		return false;
	const u32 next = keyAfterTab(keys, clientCount, activeKey, back);
	if (next == activeKey)
		return false;
	activeKey = next;
	return true;
}

u32 activeMembers(CUnit** out) {
	u32 m = 0;
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			out[m++] = clientSel[i];
	return m;
}

u32 activeFirstIndex() {
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			return i;
	return 0;
}

void reset() {
	previousCount = 0;
	activeKey = 0;
}

} //selsub
