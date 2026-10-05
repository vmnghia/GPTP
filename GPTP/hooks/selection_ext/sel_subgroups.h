//Subgroups of the console selection (stage 5): the selection split by unit
//type, one subgroup active, whose leader is the portrait and whose buttons
//are the card. Local UI state: never synced.
#pragma once
#include <SCBW/api.h>

namespace selsub {

//A unit's subgroup key (selext::subgroupKey with its priority and type).
u32 keyOf(CUnit* unit);
//Sorts selext::clientSel (count n) by subgroup, updates the active subgroup
//from the change since the last call, and returns the leader (the active
//subgroup's unit selexe::outranks picks), or NULL for n == 0.
CUnit* sortAndPickLeader(u32 n);
//The next sort follows a fresh selection: the top subgroup becomes active.
void markFresh();
//Tab / Shift+Tab. Returns false if nothing changed (one subgroup or none).
bool cycle(bool back);
//The active subgroup's units, in panel order; returns how many.
u32 activeMembers(CUnit** out);
//Index in clientSel of the active subgroup's first unit (0 if none).
u32 activeFirstIndex();
//Forgets the active subgroup (a new game).
void reset();
//Puts into the client mirror the 12 best units for a send-side check: the
//active subgroup's first (most energy first), then the rest of the selection.
void viewBegin();
//Puts the real client mirror back.
void viewEnd();
//Whether a panel wireframe of unit is drawn dimmed: several subgroups are
//selected and unit is outside the active one.
bool isDimmed(CUnit* unit);
//Replaces the wireframe colour remap (0x50CE81-82, 0x50CE91-94,
//0x50CE99-9C) with its dimmed colours, if unit is dimmed. The draw proc
//0x456F50 puts the remap back after the frame is drawn.
void dimWireframe(CUnit* unit);
//Changes whenever the active subgroup does (for the panel's redraw).
u32 activeStamp();
//Changes whenever the console list is rebuilt (clientCopy), so a cache of
//anything computed from its units knows to drop it.
u32 selectionVersion();

} //selsub
