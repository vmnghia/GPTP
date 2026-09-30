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
//Whether the unit may join a selection of more than one.
bool canMultiSelect(CUnit* unit);							//0x47B770
//Whether unit ranks above best for the console portrait.
bool outranks(CUnit* unit, CUnit* best);					//0x49A350
//Alt-click: selects the recent selection holding the unit, if any.
bool selectRecentGroupOf(u32 tag);							//0x496D30
void centerViewOnGroup(u32 group);							//0x4967E0
void queueCommand(const void* data, u32 size);				//0x485BD0
//Cancels building placement (0x48D9A0, then 0x48E310).
void cancelPlacement();
void cancelTargetOrder();									//0x48CA10
bool writeCompressed(FILE* file, const void* data, u32 size);	//0x4C3450
bool readCompressed(FILE* file, void* data, u32 size);			//0x4C3280
//The exe's own CRT: the save file's FILE* belongs to it.
size_t fwriteExe(const void* data, size_t size, size_t count, FILE* file);	//0x411931
size_t freadExe(void* data, size_t size, size_t count, FILE* file);			//0x4117DE

} //selexe
