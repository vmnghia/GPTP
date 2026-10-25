//The Mecha Viking's transform between its ground form (unit 10) and its air
//form (unit 91): the plugin's own command and order (design: sc2grp
//docs/research/2026-10-23-viking-transform-scratch.md; hooks in
//hooks/recv_commands/viking_transform*). Pure constants, so the host test of
//the command lengths (SCBW/selection_ext_core.cpp) can read them.
#pragma once
#include "../types.h"

namespace vikingtransform {

//Transform, [0x40][u8 to form][u8 queued]: one of the plugin's commands in
//dispatch slot 50 (beside 0x3C-0x3F).
const u8 CMD_VIKING_TRANSFORM = 0x40;
const u32 VIKING_TRANSFORM_BYTES = 3;

//The two forms. A transforming Viking is always the ground form: landing
//changes the type at its start, lift-off at its end.
const u16 VIKING_ASSAULT = 10;	//ground (Gui Montag's slot)
const u16 VIKING_FIGHTER = 91;	//air (the Cargo Ship's slot)

//The transform's order: GuardianAspect, which the exe never runs or compares
//(the design's A5), with SiegeMode's orders.dat flags.
const u8 ORDER_VIKING_TRANSFORM = 0x68;

//The flight to a landing spot, when there is no room where the fighter is:
//ReaverStop (0x07) reused - a rarely used order, run by the plugin only for
//units 10 and 91, which never use it themselves (the Reaver keeps its own;
//the hook checks the unit). Its orders.dat entry is Move's but for
//waypoints_slowdown: interruptable and queueable, so any command cancels the
//flight, as SC2's; the transform's own order (above) stays uninterruptable.
const u8 ORDER_VIKING_FLY = 0x07;
//How near the landing spot the flight counts as arrived (pixels)
const s32 FLY_ARRIVED = 8;

//The buttons' variables (the Manifold Editor's actVar), on BTNSACT_DoNothing
//0x423180: to the ground form (on unit 91's set), to the air form (on unit 10's).
const u32 ACTVAR_TO_ASSAULT = 0xF00A;
const u32 ACTVAR_TO_FIGHTER = 0xF05B;

//How far the landing may nudge the Viking off other units (pixels); farther,
//it flies to the spot first, then lands.
const s32 MAX_LANDING_NUDGE = 16;
//How far the landing searches for a spot (pixels either side): the search's own
//default, 128, missed walkable ground past a cliff or water (2026-10-25)
const s16 LANDING_SEARCH = 256;

//The order's states (mainOrderState).
//STATE_FLYING: a fighter on its way to the nearest spot it can land on
enum State { STATE_STOP = 0, STATE_START = 1, STATE_LANDING = 2, STATE_LIFTING = 3, STATE_FLYING = 4 };

//The form a command's "to form" byte is, as a unit id; 0 for anything else.
inline u16 toFormUnit(u8 toForm) {
	return toForm == VIKING_ASSAULT ? VIKING_ASSAULT : toForm == VIKING_FIGHTER ? VIKING_FIGHTER : 0;
}

//The form a Viking must be to go to @p toForm: the other one.
inline u16 sourceForm(u16 toForm) {
	return toForm == VIKING_ASSAULT ? VIKING_FIGHTER : VIKING_ASSAULT;
}

} //vikingtransform
