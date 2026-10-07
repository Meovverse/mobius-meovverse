#!/usr/bin/env python3
"""walkmask.py —— 为 RpgWalk 生成离线「可走面」碰撞掩码（像素级）。

为什么离线：内景图是单张 PNG、没有独立碰撞层，运行期按颜色分类会误判家具。
离线可以「颜色分类 + 形态学清理 + 手工障碍矩形」，存成 data/gen/walk_<map>.png，
RpgWalk 优先读它（没有才回退到颜色分类）。

用法：  python3 tools/walkmask.py            # 生成全部
        python3 tools/walkmask.py map_shop   # 只生成一张

新增地图：在 MAPS 里加一条（map 路径 / 障碍矩形列表，坐标是图内像素）。
障碍矩形就是"家具/墙/陈列"的外接框，逐个减掉。
"""
import sys, os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "assets", "textures")
OUT = os.path.join(ROOT, "data", "gen")

# 地图 → 障碍矩形 (x0,y0,x1,y1)，像素坐标（图内）
MAPS = {
    "map_office": [
        (378, 236, 568, 408),   # 书柜
        (700, 285, 880, 625),   # 右侧石堆
        (25, 555, 325, 705),    # 左下石料
        (146, 230, 312, 418),   # 床
        (410, 420, 568, 512),   # 桌
    ],
    "map_shop": [
        (378, 108, 1012, 568),  # 中央碑样品陈列台
        (0, 688, 300, 832),     # 左下账台
        (250, 758, 1152, 960),  # 底部碑阵
    ],
    "map_archive_room": [
        (0, 0, 480, 88),        # 顶墙
        (78, 88, 480, 152),     # 顶层架
        (0, 0, 92, 588),        # 左墙/抽屉列
        (128, 225, 480, 308),   # 中层架
        (132, 368, 480, 588),   # 底层架
    ],
}


def warm(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    warmish = (r > 110) & (r - b > 28) & (r >= g - 6)
    gray = (np.abs(r - g) < 20) & (np.abs(g - b) < 20)
    dark = (r < 45) & (g < 45) & (b < 45)
    return warmish & ~gray & ~dark


def gen(name, obstacles):
    src = os.path.join(TEX, name + ".png")
    a = np.asarray(Image.open(src).convert("RGB")).astype(int)
    m = warm(a)
    mi = Image.fromarray((m * 255).astype("uint8"))
    # 形态学开（去碎点）→ 闭（补小洞）
    mi = (mi.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))
            .filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(5)))
    m = np.asarray(mi) > 128
    for x0, y0, x1, y1 in obstacles:
        m[max(0, y0):y1, max(0, x0):x1] = False
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((m * 255).astype("uint8")).save(os.path.join(OUT, f"walk_{name}.png"))
    ov = a.copy()
    ov[m] = (ov[m] * 0.4 + np.array([0, 255, 0]) * 0.6).astype(int)   # 调试图
    Image.fromarray(ov.astype("uint8")).save(os.path.join(OUT, f"walkprev_{name}.png"))
    print(f"{name}: 可走 {m.mean()*100:.1f}%")


if __name__ == "__main__":
    want = sys.argv[1:] or list(MAPS)
    for n in want:
        gen(n, MAPS.get(n, []))
