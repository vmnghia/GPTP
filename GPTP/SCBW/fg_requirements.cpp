#include "fg_requirements.h"
#include <cstring>

namespace fgreq {

const char* const SECTION_TAGS[TABLE_COUNT] = { "UntR", "UpgR", "TecR", "TecU", "OrdR" };

namespace {

u32 read16(const u8* p) {
	return p[0] | (p[1] << 8);
}

u32 read32(const u8* p) {
	return p[0] | (p[1] << 8) | (p[2] << 16) | ((u32)p[3] << 24);
}

} //unnamed namespace

const u8* findSection(const u8* fgp, u32 size, const char* tag, u32* length, const char** error) {
	*error = NULL;
	if (size < 9 || memcmp(fgp, "FgPa", 4) != 0) {
		*error = "not a FireGraft project";
		return NULL;
	}
	//"FgPa", u32 version, the game version NUL-terminated, then the sections.
	u32 at = 8;
	while (at < size && fgp[at] != 0)
		at++;
	if (at >= size) {
		*error = "no game version";
		return NULL;
	}
	at++;
	while (at < size) {
		if (size - at < 8) {
			*error = "a section header is cut short";
			return NULL;
		}
		const u32 sectionLength = read32(fgp + at + 4);
		if (sectionLength > size - at - 8) {
			*error = "a section is cut short";
			return NULL;
		}
		if (memcmp(fgp + at, tag, 4) == 0) {
			*length = sectionLength;
			return fgp + at + 8;
		}
		at += 8 + sectionLength;
	}
	return NULL;
}

const char* buildTable(const u8* section, u32 length, std::vector<u16>& table, u32* blanks) {
	table.clear();
	*blanks = 0;
	if (length < 2)
		return "cut short";
	const u32 count = read16(section);
	std::vector<u16> built;
	built.push_back(0);
	u32 at = 2;
	for (u32 r = 0; r < count; r++) {
		if (length - at < 2)
			return "cut short";
		const u32 id = section[at];
		const u32 n = section[at + 1];
		at += 2;
		if (n == 0 || length - at < n * 2 + 1)
			return "cut short";
		if (read16(section + at + (n - 1) * 2) != 0xFFFF)
			return "a record does not end with 0xFFFF";
		const bool used = section[at + n * 2] != 0;
		if (used) {
			built.push_back((u16)id);
			for (u32 i = 0; i < n; i++) {
				const u16 word = (u16)read16(section + at + i * 2);
				if (word == BLANK)
					(*blanks)++;
				else
					built.push_back(word);
			}
		}
		at += n * 2 + 1;
	}
	if (at != length)
		return "data after the last record";
	built.push_back(0xFFFF);
	table.swap(built);
	return NULL;
}

std::string fgpName(const char* text, u32 size) {
	u32 start = 0;
	while (start < size) {
		u32 end = start;
		while (end < size && text[end] != '\r' && text[end] != '\n')
			end++;
		std::string line(text + start, end - start);
		if (line.size() > 4 && _stricmp(line.c_str() + line.size() - 4, ".fgp") == 0)
			return line;
		start = end + 1;
	}
	return std::string();
}

} //fgreq
