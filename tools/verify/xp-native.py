"""Read-only disassembly of selected build-47965 XP methods; dependencies in artifacts/xp-research/deps."""
from pathlib import Path
import sys, re
sys.path.insert(0, str(Path('artifacts/xp-research/deps').resolve()))
import capstone, pefile

root = Path('D:/EFTdev/Client.1.2.0.0.47965')
pe = pefile.PE(str(root / 'GameAssembly.dll'), fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
data = (root / 'GameAssembly.dll').read_bytes()
dump = Path('ref/Il2CppDumperOutput/dump.cs').read_text(encoding='utf-8')
names = {}
for m in re.finditer(r'// RVA: (0x[0-9A-F]+).*\n\s*(.+)', dump):
    names.setdefault(base + int(m[1], 16), m[2])
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for arg in sys.argv[1:]:
    start, size = (int(v, 16) for v in arg.split(':'))
    offset = pe.get_offset_from_rva(start)
    print('\nMETHOD', hex(start), names.get(base+start, ''))
    for ins in md.disasm(data[offset:offset+size], base+start):
        note = ''
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
            note = names.get(int(ins.op_str, 16), '')
        print(f'{ins.address-base:X}: {ins.mnemonic:8} {ins.op_str:48} {note}')
