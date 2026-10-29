from pathlib import Path
import sys

p = Path('ref/Il2CppDumperOutput/dump.cs')
lines = p.read_text(encoding='utf-8').splitlines()
for arg in sys.argv[1:]:
    if ':' not in arg:
        for i, line in enumerate(lines):
            if arg in line:
                print('\n'.join(f'{j+1}: {lines[j]}' for j in range(max(0,i-8), min(len(lines),i+12))))
        continue
    start, end = map(int, arg.split(':'))
    for i in range(start-1, end):
        print(f'{i+1}: {lines[i]}')
