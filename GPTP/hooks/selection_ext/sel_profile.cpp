#include "sel_profile.h"
#include <SCBW/api.h>
#include <definitions.h>
#include <windows.h>
#include <cstdio>
#include <cstring>

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

const char* const SLOT_NAMES[selprof::SLOT_COUNT] = {
	"card", "card_checks", "client_copy", "panel_changed", "panel_fill", "wire_dim",
	"health_bar", "health_bar_scan", "circles", "build_active", "next_selected", "view",
	"plugin_frame"
};
const char* const LOG_NAME = "Manifold-profile.csv";

//The log, next to the running exe (not the working directory, which is
//wherever the game was started from). Empty if the path doesn't fit.
const char* logPath() {
	static char path[MAX_PATH];
	static bool built = false;
	if (!built) {
		built = true;
		path[0] = 0;
		char exe[MAX_PATH];
		const DWORD n = GetModuleFileNameA(NULL, exe, sizeof(exe));
		char* slash = n != 0 && n < sizeof(exe) ? strrchr(exe, '\\') : NULL;
		if (slash != NULL) {
			slash[1] = 0;
			if (strlen(exe) + strlen(LOG_NAME) < sizeof(path)) {
				strcpy_s(path, exe);
				strcat_s(path, LOG_NAME);
			}
		}
	}
	return path;
}

double ourMsPerFrame() {
	using namespace selprof;
	return msPerFrame(PLUGIN_FRAME) + msPerFrame(CARD) + msPerFrame(CLIENT_COPY)
		+ msPerFrame(PANEL_FILL) + msPerFrame(WIRE_DIM) + msPerFrame(CIRCLES)
		+ msPerFrame(BUILD_ACTIVE) + msPerFrame(VIEW);
}

//One row per window, appended and closed each time so a crash keeps every
//row written before it. Per slot: calls per frame, then ms per frame.
void writeRow() {
	static bool announced = false;
	static bool failed = false;
	const char* const path = logPath();
	FILE* file = NULL;
	if (path[0] == 0 || fopen_s(&file, path, "a") != 0 || file == NULL) {
		if (!failed) {
			scbw::printText(PLUGIN_NAME ": profile: can't write " "Manifold-profile.csv"
				" next to the exe", GameTextColor::Red);
			failed = true;
		}
		return;
	}
	fseek(file, 0, SEEK_END);
	if (ftell(file) == 0) {
		fprintf(file, "time,card_mode,frames,frame_avg_ms,frame_max_ms,selected_max,ours_ms");
		for (u32 i = 0; i < selprof::SLOT_COUNT; i++)
			fprintf(file, ",%s_calls,%s_ms", SLOT_NAMES[i], SLOT_NAMES[i]);
		fprintf(file, "\n");
	}
	SYSTEMTIME t;
	GetLocalTime(&t);
	fprintf(file, "%04u-%02u-%02u %02u:%02u:%02u.%03u,%s,%u,%.2f,%.2f,%u,%.3f",
		t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, t.wMilliseconds,
		selprof::cardLeaderOnly ? "leader" : "subgroup",
		frames, frameMsSum / frames, frameMsMax, selectedMax, ourMsPerFrame());
	for (u32 i = 0; i < selprof::SLOT_COUNT; i++)
		fprintf(file, ",%.2f,%.3f", (double)calls[i] / frames, msPerFrame((selprof::Slot)i));
	fprintf(file, "\n");
	fclose(file);
	if (!announced) {
		char line[MAX_PATH + 48];
		sprintf_s(line, sizeof(line), PLUGIN_NAME ": profile: logging to %s", path);
		scbw::printText(line, GameTextColor::Yellow);
		announced = true;
	}
}

void report() {
	using namespace selprof;
	writeRow();
	//The file has everything; on screen, one line to follow along.
	char line[160];
	sprintf_s(line, sizeof(line),
		"prof: frame %.1f (max %.1f) ms | sel %u | ours %.2f | card %.2f, %u chk | copy %.2f%s",
		frameMsSum / frames, frameMsMax, selectedMax, ourMsPerFrame(),
		msPerFrame(CARD), callsPerFrame(CARD_CHECKS), msPerFrame(CLIENT_COPY),
		cardLeaderOnly ? " | LEADER ONLY" : "");
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
	//Each logged window is all one mode.
	resetWindow();
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
