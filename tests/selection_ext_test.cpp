//Host test of the extended selection's pure logic. Build and run with
//selection_ext_test.bat (MSVC x86); it needs no game.
#include <SCBW/selection_ext.h>
#include <cstdio>

int main() {
	u32 line;
	const u32 failed = selext::selfTest(&line);
	if (failed != 0) {
		printf("FAIL: %u checks (first at sel_selftest.cpp line %u)\n", failed, line);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
