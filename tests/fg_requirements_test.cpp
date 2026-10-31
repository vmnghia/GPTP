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
	u32 blanks = 99;
	CHECK(buildTable(s.data(), (u32)s.size(), table, &blanks) == NULL && blanks == 0);
	const u16 expected[] = { 0,
		8, 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF,
		0, 0xFF02, 0x007A, 0xFF1F, 0xFFFF, 0xFF20, 0x0074, 0xFFFF,
		91, 0xFF02, 0x0072, 0xFF08, 0xFF05, 0xFFFF,
		0xFFFF };
	CHECK(table.size() == sizeof(expected) / 2 && memcmp(table.data(), expected, sizeof(expected)) == 0);

	std::vector<u8> extra(s);
	extra.push_back(0);
	CHECK(buildTable(extra.data(), (u32)extra.size(), table, &blanks) != NULL && table.empty());
	std::vector<u8> cut(s.begin(), s.end() - 1);
	CHECK(buildTable(cut.data(), (u32)cut.size(), table, &blanks) != NULL);
	std::vector<u8> open;
	put16(open, 1);
	putRecord(open, 8, { 0xFF02, 0x0072 }, 1);
	CHECK(buildTable(open.data(), (u32)open.size(), table, &blanks) != NULL);
	std::vector<u8> empty;
	put16(empty, 0);
	CHECK(buildTable(empty.data(), (u32)empty.size(), table, &blanks) == NULL && table.size() == 2 && table[0] == 0 && table[1] == 0xFFFF);

	//Healing as FireGraft saved it after three removes (2026-10-30): the
	//blanks go, a blank in an unused record isn't counted.
	std::vector<u8> healing;
	put16(healing, 2);
	putRecord(healing, 34, { 0xFF02, 0x0022, 0xFF00, 0xFF00, 0xFF00, 0xFFFF }, 1);
	putRecord(healing, 35, { 0xFF00, 0xFFFF }, 0);
	CHECK(buildTable(healing.data(), (u32)healing.size(), table, &blanks) == NULL && blanks == 3);
	const u16 healed[] = { 0, 34, 0xFF02, 0x0022, 0xFFFF, 0xFFFF };
	CHECK(table.size() == sizeof(healed) / 2 && memcmp(table.data(), healed, sizeof(healed)) == 0);
}

//Manifold\requirements.bin, as the Manifold Editor writes it: the .fgp layout
//with only the five requirement sections, one record each.
void testRequirementsFile() {
	const char* const tags[TABLE_COUNT] = { "UntR", "UpgR", "TecR", "TecU", "OrdR" };
	std::vector<u8> file;
	putBytes(file, "FgPa", 4);
	put32(file, 7);
	putBytes(file, "1.16.1", 7);
	for (u32 t = 0; t < TABLE_COUNT; t++) {
		std::vector<u8> section;
		put16(section, 1);
		putRecord(section, (u8)(t + 1), { 0xFF05, 0xFFFF }, 1);
		putBytes(file, tags[t], 4);
		put32(file, (u32)section.size());
		file.insert(file.end(), section.begin(), section.end());
	}
	for (u32 t = 0; t < TABLE_COUNT; t++) {
		u32 length = 0;
		const char* error = "unset";
		const u8* found = findSection(file.data(), (u32)file.size(), SECTION_TAGS[t], &length, &error);
		CHECK(found != NULL && error == NULL);
		if (found == NULL)
			continue;
		std::vector<u16> table;
		u32 blanks = 99;
		CHECK(buildTable(found, length, table, &blanks) == NULL && blanks == 0);
		CHECK(table.size() == 5 && table[0] == 0 && table[1] == t + 1 && table[2] == 0xFF05 && table[3] == 0xFFFF && table[4] == 0xFFFF);
	}
	u32 length = 0;
	const char* error = "unset";
	CHECK(findSection(file.data(), (u32)file.size(), "Buts", &length, &error) == NULL && error == NULL);
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
	testRequirementsFile();
	testDatalist();
	if (failures != 0) {
		printf("FAIL: %u checks, first at line %u\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
