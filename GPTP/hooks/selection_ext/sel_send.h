//Sending selection commands. A packet of 12 or fewer units goes out as
//vanilla 0x09/0x0A/0x0B; a larger one as select chunks (0x3C).
#pragma once
#include <SCBW/api.h>

namespace selsend {

//Sends the local player's new selection as the difference from the last one
//sent (0x4C0860).
void cmdactSelect(u32 count, CUnit** list);
//Sends a control-group command; a recall also becomes the last selection
//sent (0x4C07B0).
void cmdactHotkey(u32 group, u32 action, CUnit** list, u32 count);

} //selsend
