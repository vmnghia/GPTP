//Control groups on the local side (stage 4): Alt-click and double-tap
//centring read the extended groups (SCBW/selection_ext.h).
#pragma once
#include <SCBW/api.h>

namespace selgroups {

//Alt-click (0x496D30): selects the newest recent selection holding the unit.
//Returns whether there was one.
bool selectRecentGroupOf(u32 tag);
//Double-tap (0x4967E0): centres the view on a Ctrl+number group's units.
void centerViewOnGroup(u32 group);

} //selgroups
