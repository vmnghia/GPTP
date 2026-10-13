//Synced selection logic. Everything here runs on every computer from the same
//commands, so it may read game state only, never local state.
#pragma once
#include <SCBW/api.h>

namespace selsync {

//The next unit of the command player's selection (0x49A850). Writing 0 to
//selectionIndexStart restarts it, as in vanilla.
CUnit* nextSelected();
//Removes a unit from every player's selection (0x49A7F0).
void removeFromAllSelections(CUnit* unit);
//Clears a player's selection (0x49A740).
void clearSelection(u32 player);

void recvSelect(const u8* packet);			//0x09, 0x4C2750
void recvShiftSelect(const u8* packet);		//0x0A, 0x4C2560
void recvShiftDeselect(const u8* packet);	//0x0B, 0x4BFB40
void recvHotkey(const u8* packet);			//0x13, 0x4C2870
void recvSelectChunk(const u8* packet);		//0x3C (selext::CMD_SELECT_CHUNK)
//The end of a worker's mining (0x4697C0): 1 if it goes on with the orders
//queued behind the gather (selext::gatherYieldsToQueue), 0 for vanilla's next
//harvest step, which drops them.
u32 miningYieldsToQueue(CUnit* unit);
//Cancel Train: Cancel Last is obeyed by every selected unit able to
//(selext::cancelTrainObeyed); vanilla obeyed with one unit selected only.
void recvCancelTrain(const u8* packet);		//0x20, 0x4C0100

} //selsync
