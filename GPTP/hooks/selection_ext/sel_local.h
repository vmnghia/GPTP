//Local selection: what the local player sees (circles, the console). None of
//it is synced, and none of it may feed synced state.
#pragma once
#include <SCBW/api.h>

namespace sellocal {

//Makes list the drawn selection, replacing subunits by their parents in
//list too (0x49AE40).
void buildActive(CUnit** list, u32 count);
//Drops a dying unit from the drawn selection (0x49F7A0).
void localRemove(CUnit* unit);
//Draws every circle again after a save stripped them (0x499A60).
void redrawCircles();
//Copies the drawn selection to the console and picks the portrait (0x4C38B0).
void clientCopy();
//Drops one unit from the drawn selection and sends the rest (0x4C3B40).
void deselectAndSend(CUnit* unit);
//Selects the saved local selection when a game starts (0x4D0820).
void reselectAtStart();
//At the start of 0x4EED10: clears every selection array.
void gameStartClear();
//At 0x4EEDC6: keeps the local player's selection for reselectAtStart and
//clears the synced ones, as vanilla does with its 12.
void gameStartKeepLocal();
//The egg's second Zergling or Scourge joins the selection (0x45D040).
void addTwin(CUnit* twin);
//Tells the console to rebuild the selection, buttons, portrait and wireframes.
void requestRefresh();
//The unit whose health bar is being made for this sprite (0x4D603C): vanilla
//reads activeSel[sprite->selectionIndex], which is a byte.
CUnit* unitForHealthBar(CSprite* sprite, u32 slot);

} //sellocal
