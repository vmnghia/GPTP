#include "sel_profile.h"
#include <SCBW/api.h>
#include <definitions.h>
#include <windows.h>
#include <cstdio>

namespace {

//About 3 seconds at Fastest (42 ms a frame).
const u32 PRINT_FRAMES = 72;
//A longer gap is a pause, a menu or a load, not a slow frame.
const double GAP_IGNORED_MS = 1000.0;

selprof::Ticks ticks[selprof::SLOT_COUNT];
u32 calls[selprof::SLOT_COUNT];
selprof::Ticks lastFrameStart;
u32 frames;
double frameMsSum;
double frameMsMax;
u32 selectedMax;

double frequency() {
	static double perMs = 0;
	if (perMs == 0) {
		LARGE_INTEGER f;
		QueryPerformanceFrequency(&f);
		perMs = (double)f.QuadPart / 1000.0;
	}
	return perMs;
}

double msPerFrame(selprof::Slot slot) {
	return frames ? (double)ticks[slot] / frequency() / frames : 0.0;
}

u32 callsPerFrame(selprof::Slot slot) {
	return frames ? (calls[slot] + frames / 2) / frames : 0;
}

void report() {
	using namespace selprof;
	char line[160];
	sprintf_s(line, sizeof(line),
		"prof %u f: frame avg %.1f max %.1f ms | sel max %u | ours %.2f ms/f%s",
		frames, frameMsSum / frames, frameMsMax, selectedMax,
		msPerFrame(PLUGIN_FRAME) + msPerFrame(CARD) + msPerFrame(CLIENT_COPY)
			+ msPerFrame(PANEL_FILL) + msPerFrame(WIRE_DIM) + msPerFrame(CIRCLES)
			+ msPerFrame(BUILD_ACTIVE) + msPerFrame(VIEW),
		cardLeaderOnly ? " | card: LEADER ONLY" : "");
	scbw::printText(line, GameTextColor::Yellow);
	//Per frame: calls (x) and milliseconds.
	sprintf_s(line, sizeof(line),
		"card %ux %.2f chk %u | copy %ux %.2f | fill %ux %.2f chg %u | dim %u %.2f",
		callsPerFrame(CARD), msPerFrame(CARD), callsPerFrame(CARD_CHECKS),
		callsPerFrame(CLIENT_COPY), msPerFrame(CLIENT_COPY),
		callsPerFrame(PANEL_FILL), msPerFrame(PANEL_FILL), callsPerFrame(PANEL_CHANGED),
		callsPerFrame(WIRE_DIM), msPerFrame(WIRE_DIM));
	scbw::printText(line, GameTextColor::Yellow);
	sprintf_s(line, sizeof(line),
		"hb %u scan %u | circ %ux %.2f | act %ux %.2f | next %u | view %ux %.2f | plug %.2f",
		callsPerFrame(HEALTH_BAR), callsPerFrame(HEALTH_BAR_SCAN),
		callsPerFrame(CIRCLES), msPerFrame(CIRCLES),
		callsPerFrame(BUILD_ACTIVE), msPerFrame(BUILD_ACTIVE),
		callsPerFrame(NEXT_SELECTED), callsPerFrame(VIEW), msPerFrame(VIEW),
		msPerFrame(PLUGIN_FRAME));
	scbw::printText(line, GameTextColor::Yellow);
}

void resetWindow() {
	for (u32 i = 0; i < selprof::SLOT_COUNT; i++) {
		ticks[i] = 0;
		calls[i] = 0;
	}
	frames = 0;
	frameMsSum = 0;
	frameMsMax = 0;
	selectedMax = 0;
}

} //unnamed namespace

namespace selprof {

bool cardLeaderOnly = false;

Ticks now() {
	LARGE_INTEGER t;
	QueryPerformanceCounter(&t);
	return (Ticks)t.QuadPart;
}

void add(Slot slot, Ticks elapsed) {
	ticks[slot] += elapsed;
	calls[slot]++;
}

void count(Slot slot) {
	calls[slot]++;
}

void frame(u32 selected) {
#if SEL_PROFILE
	const Ticks start = now();
	if (lastFrameStart != 0) {
		const double ms = (double)(start - lastFrameStart) / frequency();
		if (ms < GAP_IGNORED_MS) {
			frames++;
			frameMsSum += ms;
			if (ms > frameMsMax)
				frameMsMax = ms;
		}
	}
	lastFrameStart = start;
	if (selected > selectedMax)
		selectedMax = selected;
	if (frames >= PRINT_FRAMES) {
		report();
		resetWindow();
	}
#else
	(void)selected;
#endif
}

bool keyDown(u16 key, bool shift, bool ctrl, bool alt) {
#if SEL_PROFILE
	const u16 VK_P = 0x50;
	if (key != VK_P || !ctrl || !alt || shift)
		return false;
	cardLeaderOnly = !cardLeaderOnly;
	scbw::printText(cardLeaderOnly
		? PLUGIN_NAME ": profile: card checks the leader only"
		: PLUGIN_NAME ": profile: card checks the whole subgroup",
		GameTextColor::Yellow);
	//Show the switched card now, not at its next refresh.
	*(u32*)0x0068C1B0 = 1;
	return true;
#else
	(void)key; (void)shift; (void)ctrl; (void)alt;
	return false;
#endif
}

} //selprof
