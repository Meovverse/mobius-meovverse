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
from collections import deque
import numpy as np
from PIL import Image, ImageFilter, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEX = os.path.join(ROOT, "assets", "textures")
OUT = os.path.join(ROOT, "data", "gen")

# 地图 → (闭运算半径, 障碍矩形 (x0,y0,x1,y1))，像素坐标（图内）
# ★ 必须"闭"(先膨胀后腐蚀)而不是"开"：木地板的木纹/阴影是被判成非可走的细线，
#   开运算会把这些细线越撕越宽、把地板碎成几百块（实测办公室 529 块），玩家被
#   困在小口袋里→"走动极其不流畅"。闭运算把细缝桥接起来，得到一整片连通地板。
MAPS = {
    "map_office": (5, [
        (378, 236, 568, 408),   # 书柜
        (700, 285, 880, 625),   # 右侧石堆
        (25, 555, 325, 705),    # 左下石料
        (146, 230, 312, 418),   # 床
        (410, 420, 568, 512),   # 桌
    ]),
    "map_shop": (3, [
        (378, 108, 1012, 568),  # 中央碑样品陈列台
        (0, 688, 300, 832),     # 左下账台
        (250, 758, 1152, 960),  # 底部碑阵
    ]),
    "map_archive_room": (3, [
        (0, 0, 480, 88),        # 顶墙
        (78, 88, 480, 152),     # 顶层架
        (0, 0, 92, 588),        # 左墙/抽屉列
        (128, 225, 480, 308),   # 中层架
        (132, 368, 480, 588),   # 底层架
    ]),
}


def warm(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    warmish = (r > 110) & (r - b > 28) & (r >= g - 6)
    gray = (np.abs(r - g) < 20) & (np.abs(g - b) < 20)
    dark = (r < 45) & (g < 45) & (b < 45)
    return warmish & ~gray & ~dark


def gen(name, radius, obstacles):
    src = os.path.join(TEX, name + ".png")
    a = np.asarray(Image.open(src).convert("RGB")).astype(int)
    m = warm(a)
    mi = Image.fromarray((m * 255).astype("uint8"))
    # 闭运算：先把细缝桥接起来（地板连通），再轻微开一下去碎点
    mi = (mi.filter(ImageFilter.MaxFilter(radius)).filter(ImageFilter.MinFilter(radius))
            .filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3)))
    m = np.asarray(mi) > 128
    # ★ 填内部**小**孔：颜色分类会在开阔地板里留下零散的非可走小坑（Citrate#40/#42）。
    #   从图边缘洪泛非可走区，未被边缘触及的=内部孔；**只填小孔**（大块留作墙/家具/陈列）。
    #   顺序：先填孔、再减障碍矩形，否则会把家具矩形也填回去。
    h, w = m.shape
    nw = Image.fromarray(((~m) * 255).astype("uint8"))
    for seed in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1),
                 (w // 2, 0), (w // 2, h - 1), (0, h // 2), (w - 1, h // 2)):
        if nw.getpixel(seed) == 255:
            ImageDraw.floodfill(nw, seed, 128)
    holes = np.asarray(nw) == 255
    MAX_HOLE = 12000
    visited = np.zeros_like(holes)
    filled = 0
    for y in range(h):
        for x in range(w):
            if not holes[y, x] or visited[y, x]:
                continue
            comp, dq = [], deque([(x, y)])
            visited[y, x] = True
            while dq:
                cx, cy = dq.popleft()
                comp.append((cx, cy))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < w and 0 <= ny < h and holes[ny, nx] and not visited[ny, nx]:
                        visited[ny, nx] = True
                        dq.append((nx, ny))
            if len(comp) <= MAX_HOLE:
                for cx, cy in comp:
                    m[cy, cx] = True
                filled += 1
    for x0, y0, x1, y1 in obstacles:
        m[max(0, y0):y1, max(0, x0):x1] = False
    print(f"{name}: 可走 {m.mean()*100:.1f}%（填了小孔 {filled} 个）")
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((m * 255).astype("uint8")).save(os.path.join(OUT, f"walk_{name}.png"))
    ov = a.copy()
    ov[m] = (ov[m] * 0.4 + np.array([0, 255, 0]) * 0.6).astype(int)   # 调试图
    Image.fromarray(ov.astype("uint8")).save(os.path.join(OUT, f"walkprev_{name}.png"))
    print(f"{name}: 可走 {m.mean()*100:.1f}%")


if __name__ == "__main__":
    want = sys.argv[1:] or list(MAPS)
    for n in want:
        r, obs = MAPS.get(n, (3, []))
        gen(n, r, obs)
