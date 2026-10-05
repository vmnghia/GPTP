//Measures the lag that grows with the selection (docs/resolution.md §6).
//Every PRINT_FRAMES game frames it prints the average frame time and, per
//suspect, its calls and milliseconds per frame. Ctrl+Alt+P toggles the
//command card's subgroup checks to the leader only, for an A/B in one run.
//
//Local only: nothing here may feed back into game state. Set SEL_PROFILE to
//0 to compile it all out.
#pragma once
#include "../../types.h"

#define SEL_PROFILE 1

namespace selprof {

//QueryPerformanceCounter ticks (types.h has no 64-bit type).
typedef unsigned long long Ticks;

enum Slot {
	CARD,			//updateButtonSet_Sub4591D0: the card's buttons and conditions
	CARD_CHECKS,	//req_check calls made for subgroup members (count only)
	CLIENT_COPY,	//sellocal::clientCopy: the console list, its sort and leader
	PANEL_CHANGED,	//selpanel::changed (count only)
	PANEL_FILL,		//selpanel::fill
	WIRE_DIM,		//selsub::dimWireframe, once per wireframe drawn
	HEALTH_BAR,		//sellocal::unitForHealthBar (count only)
	HEALTH_BAR_SCAN,	//the same, past slot 255: a scan of the selection (count only)
	CIRCLES,		//sellocal::redrawCircles
	BUILD_ACTIVE,	//sellocal::buildActive
	NEXT_SELECTED,	//selsync::nextSelected (count only)
	VIEW,			//selsub::viewBegin
	PLUGIN_FRAME,	//plugins::nextFrame's own work
	SLOT_COUNT
};

Ticks now();
void add(Slot slot, Ticks elapsed);
void count(Slot slot);
//Once per game frame, from plugins::nextFrame: records the frame interval and
//prints the report every PRINT_FRAMES frames.
void frame(u32 selected);

//Ctrl+Alt+P: the card checks the leader alone, as with one unit selected.
extern bool cardLeaderOnly;
//The key handler's part: returns true if it took the key.
bool keyDown(u16 key, bool shift, bool ctrl, bool alt);

struct Scope {
	Slot slot;
	Ticks start;
	explicit Scope(Slot s) : slot(s), start(now()) {}
	~Scope() { add(slot, now() - start); }
};

} //selprof

#if SEL_PROFILE
#define SEL_PROFILE_SCOPE(slot) selprof::Scope selProfileScope_(selprof::slot)
#define SEL_PROFILE_COUNT(slot) selprof::count(selprof::slot)
#else
#define SEL_PROFILE_SCOPE(slot) ((void)0)
#define SEL_PROFILE_COUNT(slot) ((void)0)
#endif
