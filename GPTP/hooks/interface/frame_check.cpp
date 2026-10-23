#include "frame_check.h"
#include <hook_tools.h>
#include <cstdio>

namespace {

//Each image and frame is noted once a game, and at most this many of them.
const u32 SEEN_MAX = 16;
struct Seen { u16 imageId; u16 frame; };
Seen seen[SEEN_MAX];
u32 seenCount = 0;
u32 seenGameFrame = 0;	//*elapsedTimeFrames when last noted: a smaller one is a new game

const u32 PENDING_MAX = 4;
char pending[PENDING_MAX][192];
u32 pendingCount = 0;

bool alreadySeen(u16 imageId, u16 frame) {
	if (*elapsedTimeFrames < seenGameFrame)
		seenCount = 0;
	seenGameFrame = *elapsedTimeFrames;
	for (u32 i = 0; i < seenCount; ++i)
		if (seen[i].imageId == imageId && seen[i].frame == frame)
			return true;
	if (seenCount == SEEN_MAX)
		return true;
	seen[seenCount].imageId = imageId;
	seen[seenCount].frame = frame;
	++seenCount;
	return false;
}

//Who the sprite belongs to: a unit, or a weapon's bullet.
void describeOwner(const CSprite* sprite, char* text, size_t size) {
	for (int i = 0; i < UNIT_ARRAY_LENGTH; ++i) {
		const CUnit* unit = &unitTable[i];
		if (unit->sprite == sprite) {
			sprintf_s(text, size, "unit %u (index %d)", unit->id, i + 1);
			return;
		}
	}
	for (int i = 0; i < BULLET_ARRAY_LENGTH; ++i) {
		const CBullet* bullet = &bulletTable[i];
		if (bullet->sprite == sprite) {
			if (bullet->sourceUnit != NULL)
				sprintf_s(text, size, "bullet of weapon %u from unit %u", bullet->weaponType, bullet->sourceUnit->id);
			else
				sprintf_s(text, size, "bullet of weapon %u", bullet->weaponType);
			return;
		}
	}
	sprintf_s(text, size, "no unit or bullet");
}

void note(const CImage* image) {
	const u16 frameCount = image->grpOffset != NULL ? image->grpOffset->frameCount : 0;
	if (alreadySeen(image->id, image->frameIndex) || pendingCount == PENDING_MAX)
		return;
	const u32* iscriptIds = (const u32*)images_dat::imagesDat[7].address;
	const CSprite* sprite = image->parentSprite;
	char owner[80];
	if (sprite != NULL)
		describeOwner(sprite, owner, sizeof(owner));
	else
		sprintf_s(owner, "no sprite");
	sprintf_s(pending[pendingCount], sizeof(pending[pendingCount]),
	          "Bad frame: image %u frame %u, GRP has %u (set %u dir %u) iscript %u anim %u; sprite %u, %s",
	          image->id, image->frameIndex, frameCount, image->frameSet, image->direction,
	          iscriptIds[image->id], image->animation,
	          sprite != NULL ? sprite->spriteId : 0xFFFF, owner);
	++pendingCount;
}

//0x498CA0 in the sprite draw: its call of the image draw 0x497CE0 (ESI = the
//image, kept, and read on by the loop for the next one). A skipped image gets
//the end of the draw only: its Redraw flag cleared.
const u32 Func_DrawImage = 0x00497CE0;
void __declspec(naked) imageDrawStub() {
	static CImage* image;
	static u32 drawable;
	__asm {
		MOV image, ESI
		PUSHAD
	}
	drawable = framecheck::imageDrawable(image);
	__asm {
		POPAD
		CMP drawable, 0
		JE skip
		JMP Func_DrawImage
	skip:
		AND BYTE PTR [ESI+0x0C], 0xFE
		RETN
	}
}

} //unnamed namespace

namespace framecheck {

u32 imageDrawable(CImage* image) {
	//The draw itself skips an Invisible image without reading its frame.
	if (image->flags & CImage_Flags::Invisible)
		return 1;
	if (image->grpOffset != NULL && image->frameIndex < image->grpOffset->frameCount)
		return 1;
	note(image);
	return 0;
}

void printPending() {
	for (u32 i = 0; i < pendingCount; ++i)
		scbw::printText(pending[i], GameTextColor::Yellow);
	pendingCount = 0;
}

} //framecheck

namespace hooks {

void injectFrameCheckHooks() {
	callPatch(imageDrawStub, 0x00498CA0, 0);
}

} //hooks
