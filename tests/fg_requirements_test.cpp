//Host test of FireGraft's requirement sections (SCBW/fg_requirements.cpp).
//Build and run with fg_requirements_test.bat (MSVC x86); it needs no game.
#include <SCBW/fg_requirements.h>
#include <cstdio>
#include <cstring>
#include <vector>

using namespace fgreq;

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

void putRecord(std::vector<u8>& v, u8 id, const std::vector<u16>& words, u8 used) {
	v.push_back(id);
	v.push_back((u8)words.size());
	for (size_t i = 0; i < words.size(); i++)
		put16(v, words[i]);
	v.push_back(used);
}

void putBytes(std::vector<u8>& v, const char* text, size_t n) {
	v.insert(v.end(), (const u8*)text, (const u8*)text + n);
}

std::vector<u8> project(const char* tag, const std::vector<u8>& data) {
	std::vector<u8> fgp;
	putBytes(fgp, "FgPa", 4);
	put32(fgp, 7);
	putBytes(fgp, "1.16.1", 7);
	std::vector<u8> other(3, 0xAA);
	putBytes(fgp, "Unit", 4);
	put32(fgp, (u32)other.size());
	fgp.insert(fgp.end(), other.begin(), other.end());
	putBytes(fgp, tag, 4);
	put32(fgp, (u32)data.size());
	fgp.insert(fgp.end(), data.begin(), data.end());
	return fgp;
}

void testSections() {
	std::vector<u8> data(5, 0x11);
	const std::vector<u8> fgp = project("UntR", data);
	u32 length = 0;
	const char* error = "unset";
	const u8* found = findSection(fgp.data(), (u32)fgp.size(), "UntR", &length, &error);
	CHECK(found != NULL && error == NULL && length == 5 && found[0] == 0x11);
	found = findSection(fgp.data(), (u32)fgp.size(), "OrdR", &length, &error);
	CHECK(found == NULL && error == NULL);
	std::vector<u8> cut(fgp.begin(), fgp.end() - 1);
	found = findSection(cut.data(), (u32)cut.size(), "UntR", &length, &error);
	CHECK(found == NULL && error != NULL);
	const u8 notFgp[] = "MPQ\x1a....";
	found = findSection(notFgp, sizeof(notFgp), "UntR", &length, &error);
	CHECK(found == NULL && error != NULL);
}

void testTable() {
	//The Wraith (8) and the Viking (91) as FireGraft saves them, a record left
	//unused, and an upgrade with level blocks (inner 0xFFFFs).
	std::vector<u8> s;
	put16(s, 4);
	putRecord(s, 8, { 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF }, 1);
	putRecord(s, 30, { 0xFF02, 0x0071, 0xFFFF }, 0);
	putRecord(s, 0, { 0xFF02, 0x007A, 0xFF1F, 0xFFFF, 0xFF20, 0x0074, 0xFFFF }, 1);
	putRecord(s, 91, { 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF }, 1);
	std::vector<u16> table;
	CHECK(buildTable(s.data(), (u32)s.size(), table) == NULL);
	const u16 expected[] = { 0,
		8, 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF,
		0, 0xFF02, 0x007A, 0xFF1F, 0xFFFF, 0xFF20, 0x0074, 0xFFFF,
		91, 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF,
		0xFFFF };
	CHECK(table.size() == sizeof(expected) / 2 && memcmp(table.data(), expected, sizeof(expected)) == 0);

	std::vector<u8> extra(s);
	extra.push_back(0);
	CHECK(buildTable(extra.data(), (u32)extra.size(), table) != NULL && table.empty());
	std::vector<u8> cut(s.begin(), s.end() - 1);
	CHECK(buildTable(cut.data(), (u32)cut.size(), table) != NULL);
	std::vector<u8> open;
	put16(open, 1);
	putRecord(open, 8, { 0xFF02, 0x0072 }, 1);
	CHECK(buildTable(open.data(), (u32)open.size(), table) != NULL);
	std::vector<u8> empty;
	put16(empty, 0);
	CHECK(buildTable(empty.data(), (u32)empty.size(), table) == NULL && table.size() == 2 && table[0] == 0 && table[1] == 0xFFFF);
}

void testDatalist() {
	const char list[] = "Firegraft\\1161.fgd\r\nFiregraft\\SCManifold.fgp\r\n";
	CHECK(fgpName(list, sizeof(list) - 1) == "Firegraft\\SCManifold.fgp");
	const char last[] = "Firegraft\\1161.fgd\nFiregraft\\Other.FGP";
	CHECK(fgpName(last, sizeof(last) - 1) == "Firegraft\\Other.FGP");
	const char none[] = "Firegraft\\1161.fgd\r\n";
	CHECK(fgpName(none, sizeof(none) - 1).empty());
}

} //unnamed namespace

int main() {
	testSections();
	testTable();
	testDatalist();
	if (failures != 0) {
		printf("FAIL: %u checks, first at line %u\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
