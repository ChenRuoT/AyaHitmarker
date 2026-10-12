"""把一次 DebugHarness 预览跑出来的截图，按预定义版式拼成 _scratch/<版本>-preview 里的联系表。

    python -P preview_sheets.py <截图目录> <输出目录>

`imgsheet.py` 一次只能拼一个前缀，而预览图需要把不同前缀的截图放进同一张（例如
「单杀稳定态 + 五杀累加」）。这里把整套版式写死成下面的 SPEC，所以预览集是可一键复现的，
不是手工拼的。截图来源见 preview-0.5.0.txt。

依赖 opencv-python + numpy。用 -P 运行（不把脚本目录 / 当前目录加进 sys.path）。
"""
import os
import sys

import cv2
import numpy as np

try:
    from PIL import Image, ImageDraw, ImageFont
    _FONT_CANDIDATES = [
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\simhei.ttf",
        r"C:\Windows\Fonts\segoeui.ttf",
    ]
    _FONT_PATH = next((p for p in _FONT_CANDIDATES if os.path.exists(p)), None)
except Exception:                                     # pragma: no cover
    Image = ImageDraw = ImageFont = None
    _FONT_PATH = None

# 2560x1600 下实测的裁剪框（见 preview-0.5.0.txt 的说明）
CARD = (800, 715, 1190, 925)        # 击杀卡片：10 张暗底卡片的并集 bbox 是 (818,726)-(1173,909)
MARK = (1110, 660, 1440, 880)       # 命中/击杀标记 + 伤害数字：并集 bbox (836,740)-(1392,909)

# (输出文件名, 裁剪框, 列数, 缩略图宽, [(前缀或文件名, 显示标签)])
SPEC = [
    ("01_card_single_and_penta.png", CARD, 2, 1100, [
        ("p_single", "单杀稳定态"),
        ("p_penta", "五连杀累加"),
    ]),
    ("02_multikill_banners.png", CARD, 1, 1000, [
        ("p_dbl", "双杀 横幅 + 2 骷髅徽记"),
        ("p_triple", "三杀 横幅 + 3 骷髅徽记"),
        ("p_quad", "四杀 横幅 + 4 骷髅徽记"),
    ]),
    ("03_card_on_bright_backgrounds.png", CARD, 1, 1000, [
        ("p_white", "纯白底（满级 + 长名字）"),
        ("p_snow", "雪地底"),
    ]),
    ("04_hit_and_kill_markers.png", MARK, 2, 900, [
        ("p_hit_white", "白底命中标记（深色描边）"),
        ("p_kill_00", "击杀瞬间：标记变红粗线"),
        ("p_kill_04", "红色标记向外扩散"),
        ("p_kmark_04", "扩散 + 伤害数字变红"),
    ]),
    ("05_enter_animation.png", CARD, 4, 380, [("p_enter", "入场逐帧（每帧 33ms）")]),
    ("06_exit_animation.png", CARD, 6, 260, [("p_exit", "退场逐帧（错峰闪烁熄灭）")]),
    ("07_kill_marker_sequence.png", MARK, 4, 380, [("p_kmark", "击杀标记扩散序列")]),
    ("11_ribbon_variants.png", CARD, 1, 1000, [
        ("p_r_first", "精锐猎杀（87m 远程）"),
        ("p_r_revenge", "复仇者"),
        ("p_r_marksman", "神射手（史诗档）"),
        ("p_r_melee", "冷兵器（SCAV 近战）"),
        ("p_r_boss", "首领猎杀者"),
    ]),
]


def collect(shots_dir, key):
    """`key` 可以是单张图的名字，也可以是一段前缀（自动收集 key_NN.png 序列）。"""
    single = os.path.join(shots_dir, key + ".png")
    if os.path.exists(single):
        return [single]
    seq = []
    for i in range(0, 200):
        p = os.path.join(shots_dir, "%s_%02d.png" % (key, i))
        if not os.path.exists(p):
            break
        seq.append(p)
    if not seq:
        raise SystemExit("no shots for " + key)
    return seq


def _label(bgr, text, px=20):
    """在缩略图左上角画标签。cv2 的 Hershey 字体画不出中文（会变成 ???），所以走 PIL。"""
    if _FONT_PATH is None:
        cv2.putText(bgr, text.encode('ascii', 'replace').decode(), (8, px + 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(bgr, text.encode('ascii', 'replace').decode(), (8, px + 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 1, cv2.LINE_AA)
        return bgr
    im = Image.fromarray(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
    d = ImageDraw.Draw(im)
    f = ImageFont.truetype(_FONT_PATH, px)
    d.text((9, 6), text, font=f, fill=(0, 255, 255),
           stroke_width=3, stroke_fill=(0, 0, 0))
    return cv2.cvtColor(np.array(im), cv2.COLOR_RGB2BGR)


def build(shots_dir, out_dir, name, crop, cols, tw, groups):
    x0, y0, x1, y1 = crop
    tiles = []
    for key, label in groups:
        for f in collect(shots_dir, key):
            im = cv2.imread(f)
            if im is None:
                continue
            im = im[y0:y1, x0:x1]
            h, w = im.shape[:2]
            im = cv2.resize(im, (tw, int(round(h * tw / w))),
                            interpolation=cv2.INTER_AREA if tw < w else cv2.INTER_CUBIC)
            base = os.path.splitext(os.path.basename(f))[0]
            im = _label(im, "%s  %s" % (base, label), px=max(13, min(22, tw // 26)))
            tiles.append(im)

    th, tW = tiles[0].shape[:2]
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.zeros((rows * th, cols * tW, 3), np.uint8)
    for i, t in enumerate(tiles):
        r, c = divmod(i, cols)
        sheet[r * th:(r + 1) * th, c * tW:(c + 1) * tW] = t
    out = os.path.join(out_dir, name)
    cv2.imwrite(out, sheet)
    print("%-38s %5d x %-5d frames=%d" % (name, sheet.shape[1], sheet.shape[0], len(tiles)))


if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    shots, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for name, crop, cols, tw, groups in SPEC:
        build(shots, out, name, crop, cols, tw, groups)
