"""把 DebugHarness 的截图序列裁剪后拼成带编号的联系表，方便一眼看完一段动画。

    python -P imgsheet.py <截图目录> <前缀> <输出.png> x0 y0 x1 y1 <列数> <缩略图宽>

<前缀>_NN.png（seq 指令产生的序列）或单张 <前缀>.png。裁剪框 (x0,y0)-(x1,y1) 是整屏截图里的像素坐标
（2560×1600 下卡片大约在 480..1300 × 690..950，命中标记在屏幕中心附近）。

依赖 opencv-python + numpy。用 -P 运行（不把脚本目录 / 当前目录加进 sys.path）。
"""
import glob
import os
import sys

import cv2
import numpy as np

if len(sys.argv) < 10:
    sys.exit(__doc__)

d, prefix, out = sys.argv[1], sys.argv[2], sys.argv[3]
x0, y0, x1, y1 = map(int, sys.argv[4:8])
cols = int(sys.argv[8])
tw = int(sys.argv[9])

files = sorted(glob.glob(os.path.join(d, prefix + "_[0-9][0-9].png")))
if not files:
    single = os.path.join(d, prefix + ".png")
    files = [single] if os.path.exists(single) else []
if not files:
    sys.exit("no files for " + prefix)

thumbs = []
for f in files:
    im = cv2.imread(f)
    if im is None:
        continue
    im = im[y0:y1, x0:x1]
    h, w = im.shape[:2]
    th = int(h * tw / w)
    im = cv2.resize(im, (tw, th), interpolation=cv2.INTER_AREA if tw < w else cv2.INTER_CUBIC)
    label = os.path.splitext(os.path.basename(f))[0]
    cv2.putText(im, label, (6, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 0), 3, cv2.LINE_AA)
    cv2.putText(im, label, (6, 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1, cv2.LINE_AA)
    thumbs.append(im)

rows = (len(thumbs) + cols - 1) // cols
H, W = thumbs[0].shape[:2]
sheet = np.zeros((rows * H, cols * W, 3), np.uint8)
for i, t in enumerate(thumbs):
    r, c = divmod(i, cols)
    sheet[r * H:(r + 1) * H, c * W:(c + 1) * W] = t
cv2.imwrite(out, sheet)
print(out, sheet.shape[1], "x", sheet.shape[0], "frames:", len(thumbs))
