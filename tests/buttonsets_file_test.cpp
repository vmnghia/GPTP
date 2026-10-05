//Host test of the button set file's parser (SCBW/buttonsets_file.cpp). Build
//and run with buttonsets_file_test.bat (MSVC x86); it needs no game.
#include <SCBW/buttonsets_file.h>
#include <cstdio>
#include <cstring>
#include <vector>

using namespace bsfile;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

void put16(std::vector<u8>& v, u32 x) {
	v.push_back((u8)x);
	v.push_back((u8)(x >> 8));
}

void put32(std::vector<u8>& v, u32 x) {
	put16(v, x & 0xFFFF);
	put16(v, x >> 16);
}

const u32 CODE_START = 0x00401000;
const u32 CODE_END = 0x004FF000;
//The offset of set 5's first button in goodFile(): sets 0-4 are empty.
const u32 SET5_FIRST = HEADER_BYTES + 6 * SET_HEADER_BYTES;

//Set 5 has two buttons (positions 11 and 12), set 249 one; the rest are empty.
std::vector<u8> goodFile() {
	std::vector<u8> v;
	v.push_back('M'); v.push_back('B'); v.push_back('T'); v.push_back('S');
	put16(v, VERSION);
	put16(v, SET_COUNT);
	for (u32 s = 0; s < SET_COUNT; s++) {
		const u32 count = s == 5 ? 2 : s == 249 ? 1 : 0;
		put16(v, count);
		put16(v, 0);
		put32(v, s);
		for (u32 i = 0; i < count; i++) {
			put16(v, 11 + i);
			put16(v, 236);
			put32(v, 0x00428DA0);
			put32(v, 0x00424440);
			put16(v, 0);
			put16(v, 0);
			put16(v, 664);
			put16(v, 0);
		}
	}
	return v;
}

const char* parseOf(const std::vector<u8>& v, SetEntry* entries) {
	return parse(v.data(), (u32)v.size(), CODE_START, CODE_END, entries);
}

template <typename Spoil>
void refused(Spoil spoil, const char* reason, u32 line) {
	static SetEntry entries[SET_COUNT];
	std::vector<u8> v = goodFile();
	spoil(v);
	const char* got = parseOf(v, entries);
	check(got != NULL && strcmp(got, reason) == 0, line);
}

#define REFUSED(spoil, reason) refused(spoil, reason, __LINE__)

} //unnamed namespace

int main() {
	static SetEntry entries[SET_COUNT];
	const std::vector<u8> good = goodFile();
	CHECK(parseOf(good, entries) == NULL);
	CHECK(entries[5].buttonCount == 2 && entries[5].firstButton == SET5_FIRST && entries[5].connectedUnit == 5);
	CHECK(entries[0].buttonCount == 0 && entries[249].buttonCount == 1);

	REFUSED([](std::vector<u8>& v) { v.resize(4); }, "not a button set file");
	REFUSED([](std::vector<u8>& v) { v[0] = 'X'; }, "not a button set file");
	REFUSED([](std::vector<u8>& v) { v[4] = 2; }, "unknown version");
	REFUSED([](std::vector<u8>& v) { v[6] = 249; }, "set count is not 250");
	REFUSED([](std::vector<u8>& v) { v.pop_back(); }, "a set is cut short");
	REFUSED([](std::vector<u8>& v) { v.resize(HEADER_BYTES + 3); }, "a set is cut short");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST] = 0; }, "a position outside 1-15");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST] = 16; }, "a position outside 1-15");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST + 7] = 0x60; }, "an address outside the code");
	REFUSED([](std::vector<u8>& v) { v[SET5_FIRST + 11] = 0x60; }, "an address outside the code");
	REFUSED([](std::vector<u8>& v) { v.push_back(0); }, "data after the last set");

	if (failures != 0) {
		printf("FAIL: %u checks (first at buttonsets_file_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
