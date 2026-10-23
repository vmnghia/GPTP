#pragma once
#include <SCBW/api.h>

//The Mecha Viking's transform (SCBW/viking_transform.h; design: sc2grp
//docs/research/2026-10-23-viking-transform-scratch.md).
namespace vikingtransform {

//A press of BTNSACT_DoNothing (0x423180) with @p actVar: one of the Viking's
//buttons sends command 0x40 (local, the pressing player); any other returns.
void buttonPressed(u32 actVar, u8 queued);

//Command 0x40 received (synced): every selected Viking of the other form,
//completed and not loaded, is given the transform order.
void recvTransform(const u8* packet);

//The transform order (0x68), run each frame by the order switch (0x4EC55D)
//for a unit that is not frozen.
void orderTransform(CUnit* unit);

} //vikingtransform

namespace hooks {
void injectVikingTransformHooks();
} //hooks
