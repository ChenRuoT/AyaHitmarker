"""列出屏上可能出现的全部中文字符，供 font-check.txt 的 `glyphs` 指令使用。

扫描 Game/*.cs 里的字符串字面量（跳过注释行和日志调用）。用法：

    python -P tools/verify/onscreen_chars.py            # 打印去重排序后的字符
    python -P tools/verify/onscreen_chars.py --update   # 同时把 font-check.txt 里的 glyphs 行换成最新字符集

新增 / 修改中文文案后跑一次 --update，再用 font-check.txt 进图复核。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "verify" / "font-check.txt"

LOG_CALL = re.compile(r"Logger\.|LogInfo|LogWarning|LogError|LogDebug")
LITERAL = re.compile(r'"[^"\n]*"')
CJK = re.compile(r"[一-鿿　-〿＀-￯·×]")


def collect() -> str:
    chars = set()
    for path in sorted((ROOT / "Game").glob("*.cs")):
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.lstrip().startswith("//") or LOG_CALL.search(line):
                continue
            for literal in LITERAL.findall(line):
                chars.update(CJK.findall(literal))
    return "".join(sorted(chars))


def main() -> int:
    chars = collect()
    print(chars)
    print(f"# {len(chars)} 个字符", file=sys.stderr)

    if "--update" in sys.argv[1:]:
        text = SCRIPT.read_text(encoding="utf-8")
        updated, count = re.subn(r"(?m)^glyphs .*$", lambda _m: f"glyphs {chars}", text)
        if count != 1:
            print(f"{SCRIPT.name} 里应该恰好有一行 glyphs，实际 {count} 行", file=sys.stderr)
            return 1
        SCRIPT.write_text(updated, encoding="utf-8", newline="\n")
        print(f"# 已更新 {SCRIPT.name}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
