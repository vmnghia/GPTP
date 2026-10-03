//Calls into StarCraft.exe used by the extended selection. Each wrapper
//follows the exe function's own register convention (see the spec).
#pragma once
#include <SCBW/api.h>
#include <cstdio>

namespace selexe {

//Selection circle and health bar; slot goes to CSprite::selectionIndex.
void createSelectionCircle(CUnit* unit, u32 slot);			//0x4E6180
void removeSelectionCircle(CUnit* unit);					//0x4E6290
//Whether a player is a team-melee ally of the local human (local state:
//only gates dashed ally circles, which are local visuals).
bool isTeamAlly(u32 player);								//0x49A110
void removeDashedCircle(CSprite* sprite);					//0x497590
void addDashedCircle(CUnit* unit);							//0x4E65C0
void freeImage(CImage* image);								//0x4D4FA0
//Sets up a health-bar pool image the way the pool init does (its frame buffer
//is vanilla image 0's; the caller points it at its own).
void initHealthBarImage(CImage* image);						//0x4D68C0
void showControl(BinDlg* control);							//0x4186A0
void hideControl(BinDlg* control);							//0x418700
void invalidateControl(BinDlg* control);					//0x41C400
//Whether the unit may join a selection of more than one.
bool canMultiSelect(CUnit* unit);							//0x47B770
//Whether unit ranks above best for the console portrait.
bool outranks(CUnit* unit, CUnit* best);					//0x49A350
//Alt-click: selects the recent selection holding the unit, if any.
bool selectRecentGroupOf(u32 tag);							//0x496D30
void centerViewOnGroup(u32 group);							//0x4967E0
void queueCommand(const void* data, u32 size);				//0x485BD0
//Moves the view's top-left corner (clamped to the map by the exe).
void moveScreen(s32 x, s32 y);								//0x49C440
//Cancels building placement (0x48D9A0, then 0x48E310).
void cancelPlacement();
void cancelTargetOrder();									//0x48CA10
//The minimap's own toggles, as its key handler 0x4A5900 runs them: terrain
//(0x4A597B) or the ally colour cycle (0x4A5948), with their redraw.
void minimapToggle(bool allyColours);
bool writeCompressed(FILE* file, const void* data, u32 size);	//0x4C3450
bool readCompressed(FILE* file, void* data, u32 size);			//0x4C3280
//The exe's own CRT: the save file's FILE* belongs to it.
size_t fwriteExe(const void* data, size_t size, size_t count, FILE* file);	//0x411931
size_t freadExe(void* data, size_t size, size_t count, FILE* file);			//0x4117DE

//-------- Smart-build: the build receive path --------//

//Whether unit may take this build order for type (0x48DBD0).
bool placeBuildingAllowed(CUnit* unit, u8 order, u16 type);
//Vanilla's build placement for the current command's selection (0x48E190):
//tiles = x tile | y tile << 16.
void placeBuilding(u8 order, u16 type, u32 tiles);
//The placement check for builder at a tile position (0x473FB0, as 0x48E010
//calls it): 0 or a placement code (7 = couldn't reach the site).
u32 placementCheck(CUnit* builder, u8 player, s32 tileX, s32 tileY, u16 type);
//Shows a placement code's message to its player (0x48D930); true if 0.
bool placementMessage(u32 code);
//Whether player has the supply for type, showing the error if not (0x42CF70).
bool hasSupplies(u16 type, u8 player);
void refundQueueSlots(CUnit* unit);							//0x466E80
//Puts type in unit's build slot (0x467250).
bool fillBuildSlot(CUnit* unit, u16 type);
//Whether unit is within distance of (x, y) (0x401240).
bool withinReach(CUnit* unit, s32 x, s32 y, u32 distance);
//Takes order out of unit's queue and frees it (0x4742D0).
void removeQueuedOrder(CUnit* unit, COrder* order);
//Shows stat_txt string stringId (1-based) to player's screen (0x48CF00).
void showStatTextTo(u32 stringId, u8 player);

} //selexe
