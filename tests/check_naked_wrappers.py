# Checks that the extended selection's naked wrappers never touch [ebp...].
#
# A naked function has no stack frame of its own: EBP is still the exe
# caller's. In a Debug build, a C++ statement with a temporary (e.g.
# "x = f() ? 1 : 0;") makes the compiler use an [ebp-N] slot, which then
# writes into the caller's frame. Only the stubs that deliberately use the exe
# function's frame may touch EBP, at the offsets listed in ALLOWED.
#
# Usage: python check_naked_wrappers.py <dumpbin /disasm output file>
import re
import sys

ALLOWED = {
    'selectChunkDispatch': ('ebp+8', 'ebp-4'),   # the executor's bytes left / command size
    'healthBarUnitStub': ('ebp+8',),             # 0x4D6010's sprite argument
    'rightClickOrderAllowedStub': ('ebp+18',),   # 0x4560D0's queued-command flag (read only)
}

def naked(name):
    return name.endswith('Wrapper') or name.endswith('Stub') or name == 'selectChunkDispatch'

current = None
problems = []
for line in open(sys.argv[1]):
    m = re.match(r'^\?(\w+)@', line)
    if m:
        current = m.group(1)
        continue
    if current is None or not naked(current):
        continue
    for ref in re.findall(r'\[(ebp[+-][0-9A-Fa-fh]+)\]', line.replace(' ', '')):
        ref = ref.lower().rstrip('h')
        allowed = ALLOWED.get(current, ())
        if ref not in allowed:
            problems.append('%s: %s' % (current, line.strip()))

if problems:
    print('naked wrappers touch the caller\'s frame:')
    for p in problems:
        print('  ' + p)
    sys.exit(1)
print('naked wrappers: ok')
