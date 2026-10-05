#!/usr/bin/env python3
"""
pixelart.py —— SD 产出 → 像素资产的工具链

用途
----
美术用 Stable Diffusion 出高分辨率草图，本脚本负责把它变成引擎能吃的
硬像素资产，并守住《美术需求.md》里的三条铁律（三阶明暗 / 无抗锯齿 /
无半透明）。

子命令
------
  convert   像素化 + 调色板吸附 + alpha 二值化 + 尺寸对齐
  check     验收检查（尺寸 / 半透明 / 颜色数 / 调色板外颜色 / 无缝平铺）
  palette   导出调色板为 PNG 或 .gpl，方便美术在画图软件里导入
  tile      无缝平铺修复（noise_* 强制需要）
  snap      只做调色板吸附，不改尺寸（给已经像素化的图微调）

依赖
----
  pip install Pillow numpy

用法示例
--------
  # SD 出 1024x576 草图 → 640x360 硬像素背景
  python3 tools/pixelart.py convert raw/bg_graveyard_area_A.png \
      --out assets/textures/bg_graveyard_area_A.png --levels 12

  # 道具：分离主体 → 像素化 → alpha 二值化
  python3 tools/pixelart.py convert raw/prop_toolbox.png \
      --out assets/textures/prop_toolbox.png --size 160x100 \
      --palette paper --alpha-threshold 0.5

  # 验收（目录批量）
  python3 tools/pixelart.py check assets/textures/ --strict

  # 噪声图无缝化
  python3 tools/pixelart.py tile assets/textures/noise_chisel_a_32.png
"""

from __future__ import annotations

import argparse
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

# --------------------------------------------------------------------------
# 调色板（与 doc/美术需求.md §1.3 保持一致，改这里也要改文档）
# --------------------------------------------------------------------------

STONE = [
    "#1a1a1a",  # 山西黑·深（字口最深处）
    "#3a3a3a",  # 山西黑·中（石面中间调）
    "#6e6e6e",  # 山西黑·亮（磨过的/反光面）
    "#a8a8a8",  # 山西黑·高光
    "#d8d8d0",  # 石粉·白
    "#b9ad91",  # 纸·暗（发黄、旧）
    "#d5cbb2",  # 纸·中
    "#ece4cf",  # 纸·亮
    "#141414",  # 墨·黑
    "#a8352c",  # 印泥·朱（全篇唯一彩色）
    "#8a867c",  # 水泥·中
    "#5f5b52",  # 阴影中调（补齐灰阶用）
]

# 冷色一档：只给苏老师的深蓝外套用，见需求 §D2
SU_LAN = [
    "#2a3038",
    "#3a4450",
    "#4c5a68",
]


def hex_to_rgb(h: str) -> np.ndarray:
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64)


def hex_to_linear_rgb(h: str) -> np.ndarray:
    c = hex_to_rgb(h) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def gray_ramp(n: int, dark="#0a0a0a", light="#f0f0ee") -> list[str]:
    """n 阶灰阶，末端微微偏暖（这个游戏的光是阴天的天光，不是纯白）。"""
    a = hex_to_rgb(dark)
    b = hex_to_rgb(light)
    out = []
    for i in range(n):
        t = i / max(1, n - 1)
        c = np.round(a + (b - a) * t).astype(int)
        out.append("#%02x%02x%02x" % tuple(c))
    return out


PAPER = gray_ramp(4, "#b9ad91", "#ece4cf") + gray_ramp(4, "#141414", "#6e6e6e") + ["#a8352c"]
GRAY16 = gray_ramp(16)
UI = gray_ramp(8) + ["#a8352c"]

PALETTES = {
    "stone": STONE,
    "stone+su": STONE + SU_LAN,
    "paper": PAPER,
    "gray8": gray_ramp(8),
    "gray16": GRAY16,
    "ui": UI,
}

MAX_COLORS = {
    "bg": 16,    # 全屏背景放宽到 16
    "prop": 10,
    "char": 10,
    "ui": 8,
    "noise": 0,  # 0 = 不限
    "mask": 0,   # 遮罩只用黑/白/透明
    "fx": 6,
}


def despeckle(arr: np.ndarray, radius: int = 1) -> np.ndarray:
    """量化前先抹掉单像素噪点。

    为什么要这一步：SD 的输出永远带高频噪点。直接吸附到 N 阶调色板时，
    噪点会跨过色阶边界，在每条色阶的分界线上炸出一片"抖动点"——
    看起来像老式抖动（dithering），而这个项目的负向提示词里明确禁止 dithering。
    先做一次盒式模糊，色阶边界就干净了。
    """
    if radius <= 0:
        return arr
    n = 2 * radius + 1
    a = arr[..., 3:4]
    rgb = arr[..., :3] * a + (1.0 - a)   # 透明处填中性灰，避免黑边
    img = Image.fromarray(
        np.clip(np.concatenate([rgb, a], axis=2) * 255, 0, 255).astype(np.uint8),
        mode="RGBA")
    img = img.filter(ImageFilter.BoxBlur(radius))
    out = np.asarray(img, dtype=np.float64) / 255.0
    oa = out[..., 3:4]
    safe = np.clip(oa, 1e-4, 1.0)
    return np.concatenate([np.clip(out[..., :3] / safe, 0, 1) * oa, oa], axis=2)


def load_palette(spec: str) -> np.ndarray:
    if spec in PALETTES:
        hexes = PALETTES[spec]
    elif spec.startswith("levels:"):
        hexes = gray_ramp(int(spec.split(":")[1]))
    elif spec.startswith("#"):
        hexes = [t for t in spec.split(",") if t.strip()]
    else:
        p = Path(spec)
        if not p.exists():
            raise SystemExit(f"找不到调色板文件：{spec}")
        hexes = [ln.strip() for ln in p.read_text(encoding="utf-8").splitlines()
                 if ln.strip() and not ln.startswith("#")]
    lin = np.stack([hex_to_linear_rgb(h) for h in hexes])
    return np.concatenate([lin, np.array([hex_to_rgb(h) / 255.0 for h in hexes])], axis=1)


# --------------------------------------------------------------------------
# 核心运算
# --------------------------------------------------------------------------

# 感知加权（近似 luma），比裸 RGB 欧氏距离更接近眼睛的判断
W = np.array([0.2126, 0.7152, 0.0722])


def parse_size(s: str) -> tuple[int, int]:
    w, _, h = s.lower().partition("x")
    return int(w), int(h)


def load(src: Path) -> np.ndarray:
    """返回 float64 RGBA，0..1。"""
    return np.asarray(Image.open(src).convert("RGBA"), dtype=np.float64) / 255.0


def save(arr: np.ndarray, dst: Path):
    dst.parent.mkdir(parents=True, exist_ok=True)
    out = np.clip(np.rint(arr * 255.0), 0, 255).astype(np.uint8)
    Image.fromarray(out, mode="RGBA").save(dst, optimize=True)


def resize_rgba(arr: np.ndarray, w: int, h: int, mode: str) -> np.ndarray:
    """带 alpha 预乘的缩放，避免透明边缘出现黑边或白边。

    预乘后的图是 (r*a, g*a, b*a, a)，四个通道一起做盒式滤波，
    alpha 通道必须是**原 alpha**，不能填 1 —— 否则降采样会把 alpha 平均成常数，
    边缘的过渡带会整片塌掉。
    """
    if arr.shape[1] == w and arr.shape[0] == h:
        return arr
    a = arr[..., 3:4]
    pre = np.concatenate([arr[..., :3] * a, a], axis=2)
    img = Image.fromarray(np.clip(pre * 255, 0, 255).astype(np.uint8), mode="RGBA")
    filt = Image.NEAREST if mode == "nearest" else Image.BOX
    small = np.asarray(img.resize((w, h), filt), dtype=np.float64) / 255.0
    sa = np.clip(small[..., 3:4], 1e-4, 1.0)
    rgb = np.clip(small[..., :3] / sa, 0.0, 1.0)
    a2 = small[..., 3:4]
    return np.concatenate([rgb * a2, a2], axis=2)


def snap_to_palette(arr: np.ndarray, pal: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """把 RGB 吸附到调色板。返回 (新数组, 每个像素到最近色的距离)。"""
    pal_lin = pal[:, :3]
    pal_srgb = pal[:, 3:6]
    flat = arr.reshape(-1, 4)
    rgb = flat[:, :3]
    alpha = flat[:, 3]

    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    d = lin[:, None, :] - pal_lin[None, :, :]
    dist = np.sqrt((d ** 2 * W).sum(axis=2))          # (N, P)
    idx = dist.argmin(axis=1)
    mind = dist[np.arange(len(idx)), idx]

    out = flat.copy()
    vis = alpha > 0.004
    out[vis, :3] = pal_srgb[idx[vis]]
    return out.reshape(arr.shape), mind.reshape(arr.shape[:-1])


def binarize_alpha(arr: np.ndarray, threshold: float = 0.5) -> tuple[np.ndarray, int]:
    a = arr[..., 3]
    hard = (a >= threshold).astype(np.float64)
    n_half = int(((a > 0.004) & (a < 0.996)).sum())
    arr = arr.copy()
    arr[..., 3] = hard
    if hard.any():
        arr[..., :3] = np.where(hard[..., None] > 0.5, arr[..., :3], 0.0)
    return arr, n_half


def trim(arr: np.ndarray, pad: int = 2) -> np.ndarray:
    m = arr[..., 3] > 0.5
    if not m.any():
        return arr
    ys, xs = np.where(m)
    y0, y1 = ys.min(), ys.max() + 1
    x0, x1 = xs.min(), xs.max() + 1
    out = np.zeros_like(arr)
    out[max(0, y0 - pad):y1 + pad, max(0, x0 - pad):x1 + pad] = \
        arr[max(0, y0 - pad):y1 + pad, max(0, x0 - pad):x1 + pad]
    return out


def fit_into(arr: np.ndarray, w: int, h: int, trim_first: bool = False) -> np.ndarray:
    """缩放并居中放进 w×h 的画布，保持比例。"""
    src = trim(arr) if trim_first else arr
    sh, sw = src.shape[:2]
    if sh == 0 or sw == 0:
        return arr
    scale = min(w / sw, h / sh)
    nw, nh = max(1, round(sw * scale)), max(1, round(sh * scale))
    small = resize_rgba(src, nw, nh, "area")
    out = np.zeros((h, w, 4), dtype=np.float64)
    oy, ox = (h - nh) // 2, (w - nw) // 2
    out[oy:oy + nh, ox:ox + nw] = small
    return out


# --------------------------------------------------------------------------
# 资产规格表（美术需求.md §2 的机器可读版本）
# --------------------------------------------------------------------------

SPEC: dict[str, tuple[int, int, str]] = {
    # 文件名: (宽, 高, 类别)
    "noise_stone_grain_64.png": (64, 64, "noise"),
    "noise_chisel_a_32.png": (32, 32, "noise"),
    "noise_chisel_b_32.png": (32, 32, "noise"),
    "noise_paper_fiber_64.png": (64, 64, "noise"),
    "noise_dust_32.png": (32, 32, "noise"),
    "noise_cement_64.png": (64, 64, "noise"),

    "stele_A7_face_base.png": (640, 360, "bg"),
    "mask_stone_glyphs.png": (640, 360, "mask"),
    "map_stone_carve.png": (640, 360, "mask"),
    "stele_A7_full.png": (320, 360, "prop"),
    "stele_A7_new.png": (320, 360, "prop"),
    "stele_A7_base.png": (320, 200, "bg"),

    "bg_graveyard_area_A.png": (640, 360, "bg"),
    "bg_cemetery_qingming.png": (640, 360, "bg"),
    "bg_office_desk.png": (640, 360, "bg"),
    "bg_archive_room.png": (640, 360, "bg"),
    "bg_shop_front.png": (640, 360, "bg"),
    "bg_title_shopfront.png": (640, 360, "bg"),

    "char_luyun_back.png": (160, 240, "char"),
    "char_suteacher_wheelchair.png": (160, 220, "char"),
    "char_photographer.png": (120, 200, "char"),

    "prop_ledger_page.png": (280, 360, "prop"),
    "prop_ledger_cover.png": (300, 380, "prop"),
    "prop_scrap_stone.png": (96, 96, "prop"),
    "prop_toolbox.png": (160, 100, "prop"),
    "prop_deed_ledger.png": (300, 360, "prop"),
    "prop_archive_form.png": (280, 200, "prop"),

    "stamp_civil_bureau_64.png": (64, 64, "fx"),
    "stamp_cremation_64.png": (64, 64, "fx"),
    "stamp_insurance_64.png": (64, 64, "fx"),
    "stamp_archive_64.png": (64, 64, "fx"),
    "ink_signature_suhang_128.png": (128, 48, "prop"),
    "ink_alteration_17to16.png": (96, 32, "prop"),
    "fx_crease_set.png": (128, 128, "prop"),

    "ui_cursor_loupe_96.png": (96, 96, "ui"),
    "ui_loupe_glass_96.png": (96, 96, "ui"),
    "ui_cursor_hand_32.png": (32, 32, "ui"),
    "ui_caliper_480.png": (480, 32, "ui"),
    "ui_rubber_tool_48.png": (48, 48, "ui"),
    "ui_hammer_64.png": (64, 64, "ui"),
    "ui_paper_sheet_256.png": (256, 320, "prop"),

    "fx_dust_particle_8.png": (8, 8, "fx"),
    "fx_cement_chip_8.png": (8, 8, "fx"),
    "sign_lu_shi_160.png": (160, 48, "prop"),
    "endcard_paper_512.png": (512, 288, "bg"),
}

# 允许半透明的文件（需求 §6 白名单）
ALPHA_OK = {"ui_loupe_glass_96.png"}

# 已取消的资产：文档里还有条目（写明「取消」），但不再有产物
CANCELLED = {"doc_type_specimen.png"}

# 不由 SD 生成、必须程序/手绘的文件（见需求 §8）
NOT_SD = {
    "mask_stone_glyphs.png", "map_stone_carve.png",
    "noise_chisel_a_32.png", "noise_chisel_b_32.png",
    "noise_stone_grain_64.png", "noise_paper_fiber_64.png",
    "noise_dust_32.png", "noise_cement_64.png",
    "stamp_civil_bureau_64.png", "stamp_cremation_64.png",
    "stamp_insurance_64.png", "stamp_archive_64.png",
    "fx_crease_set.png", "fx_dust_particle_8.png", "fx_cement_chip_8.png",
    "ink_signature_suhang_128.png", "ink_alteration_17to16.png",
    "stele_A7_face_base.png",
}


# --------------------------------------------------------------------------
# 子命令
# --------------------------------------------------------------------------

def cmd_convert(a: argparse.Namespace):
    src = Path(a.infile)
    arr = load(src)
    spec = SPEC.get(Path(a.out).name)
    if a.size:
        w, h = parse_size(a.size)
    elif spec:
        w, h = spec[0], spec[1]
    elif a.native:
        w, h = arr.shape[1], arr.shape[0]
    else:
        raise SystemExit("缺少 --size 或 --native，且文件名不在规格表里")

    levels = a.levels if a.levels else (0 if (spec and spec[2] == "noise") else None)
    if a.resize:
        arr = resize_rgba(arr, w, h, "nearest" if a.pixelate_nearest else "area")
    if a.trim or a.fit:
        arr = fit_into(arr, w, h, trim_first=a.trim)

    if a.smooth:
        arr = despeckle(arr, a.smooth)

    n_half = 0
    if a.palette != "none":
        pal = load_palette(a.palette)
        arr, _ = snap_to_palette(arr, pal)
    if a.binarize_alpha:
        arr, n_half = binarize_alpha(arr, a.alpha_threshold)

    save(arr, Path(a.out))
    msg = f"→ {a.out}  {w}x{h}  调色板={a.palette}"
    if n_half:
        msg += f"  （已二值化 {n_half} 个半透明像素）"
    print(msg)


def cmd_snap(a: argparse.Namespace):
    src = Path(a.infile)
    arr = load(src)
    pal = load_palette(a.palette)
    arr, d = snap_to_palette(arr, pal)
    save(arr, Path(a.out or a.infile))
    print(f"→ {a.out or a.infile}  平均色差 {d.mean():.4f}  最大 {d.max():.4f}")


def tile_seams(arr: np.ndarray) -> np.ndarray:
    """把右半边镜像到左半边、下半边镜像到上半边，做成无缝。"""
    out = arr.copy()
    h, w = out.shape[:2]
    out[:, :w // 2] = out[:, ::-1][:, :w // 2]
    out[h // 2:, :] = out[::-1, :][h // 2:, :]
    # 修四角
    out[h // 2:, :w // 2] = out[::-1, ::-1][h // 2:, :w // 2]
    return out


def cmd_tile(a: argparse.Namespace):
    p = Path(a.infile)
    arr = load(p)
    arr = tile_seams(arr)
    save(arr, p)
    print(f"→ {p}  已无缝化")


def cmd_palette(a: argparse.Namespace):
    hexes = PALETTES[a.name]
    n = len(hexes)
    cols = min(n, 8)
    rows = math.ceil(n / cols)
    cell = 48
    img = Image.new("RGB", (cols * cell, rows * cell), (24, 24, 24))
    for i, h in enumerate(hexes):
        x, y = (i % cols) * cell, (i // cols) * cell
        img.paste(Image.new("RGB", (cell, cell), tuple(hex_to_rgb(h).astype(int))), (x, y))
    out = Path(a.out or f"palette_{a.name}.png")
    img.save(out)
    Path(out.with_suffix(".gpl")).write_text(
        "GIMP Palette\nName: " + a.name + "\n#\n" +
        "\n".join(f"{int(hex_to_rgb(h)[0])} {int(hex_to_rgb(h)[1])} {int(hex_to_rgb(h)[2])}\t{h}"
                  for h in hexes), encoding="utf-8")
    print(f"→ {out} / {out.with_suffix('.gpl')}  ({n} 色)")


def cmd_check(a: argparse.Namespace):
    roots = [Path(p) for p in a.paths]
    files = sorted({f for r in roots for f in ([r] if r.is_file() else r.rglob("*.png"))})
    if not files:
        print("没找到 PNG"); return 1

    strict = a.strict
    bad = 0
    print(f"{'文件':<38}{'尺寸':>12}  {'状态'}")
    print("-" * 78)
    for f in files:
        name = f.name
        arr = load(f)
        h, w = arr.shape[:2]
        issues = []

        spec = SPEC.get(name)
        if spec and (w, h) != (spec[0], spec[1]):
            issues.append(f"尺寸应为 {spec[0]}x{spec[1]}")
        elif not spec:
            issues.append("规格表里没有（可忽略）")
            if strict:
                bad += 1

        # 半透明
        alpha = arr[..., 3]
        n_half = int(((alpha > 0.004) & (alpha < 0.996)).sum())
        if n_half and name not in ALPHA_OK:
            issues.append(f"{n_half}px 半透明（应二值化）")
            if strict:
                bad += 1

        # 颜色数
        op = alpha > 0.5
        cat = spec[2] if spec else "bg"
        limit = MAX_COLORS.get(cat, 16)
        if limit:
            cols = np.unique(np.rint(arr[..., :3][op] * 255).astype(np.uint8).reshape(-1, 3), axis=0)
            if len(cols) > limit:
                issues.append(f"{len(cols)} 色 > 上限 {limit}")
                if strict:
                    bad += 1
        # mask/map 只能是纯黑/白
        if cat == "mask":
            uniq = np.unique(np.rint(arr * 255).astype(np.uint8).reshape(-1, 4), axis=0)
            if not np.isin(uniq, [0, 255]).all():
                issues.append("mask 必须只用 0/255")
                if strict:
                    bad += 1

        # 无缝
        if cat == "noise":
            dx = np.abs(arr[:, 0, :] - arr[:, -1, :]).max()
            dy = np.abs(arr[0, :, :] - arr[-1, :, :]).max()
            if dx > 0.02 or dy > 0.02:
                issues.append(f"接缝不平（横 {dx:.2f} 纵 {dy:.2f}，需 0）")
                if strict:
                    bad += 1

        mark = "OK" if not issues else "!! " + "; ".join(issues)
        print(f"{name:<38}{f'{w}x{h}':>12}  {mark}")

    print("-" * 78)
    print(f"{len(files)} 个文件，{bad} 个不合格。")
    return 1 if (strict and bad) else 0


def cmd_spec(a: argparse.Namespace):
    """校验 doc/美术需求.md 与本脚本的规格表是否还一致。

    资产清单和规格表是同一份事实的两个副本，改了一边忘了另一边是
    GameJam 里最容易出的错（美术等程序给尺寸，程序等美术给图）。
    放进 CI 就能挡住。
    """
    import re
    doc_path = Path(a.doc)
    if not doc_path.exists():
        raise SystemExit(f"找不到文档：{doc_path}")
    doc = doc_path.read_text(encoding="utf-8")
    ok = True

    try:
        table = doc.split("## 2. 资产总表")[1].split("## 3. 逐张详细说明")[0]
    except IndexError:
        print("!! 文档里找不到「## 2. 资产总表」小节")
        return 1
    names = set(re.findall(r"`([A-Za-z0-9_]+\.png)`", table)) - CANCELLED

    print("── 资产清单 ──")
    if names == set(SPEC):
        print(f"  {len(names)} 张，文件名与 SPEC 完全一致")
    else:
        ok = False
        for n in sorted(names - set(SPEC)):
            print(f"  !! 只在文档里（SPEC 没有尺寸）: {n}")
        for n in sorted(set(SPEC) - names):
            print(f"  !! 只在 SPEC 里（文档没有条目）: {n}")

    # 尺寸也要比对——「文件名对但尺寸改了」是最常见的一种漂移
    rows = re.findall(
        r"\|\s*`([A-Za-z0-9_]+\.png)`\s*\|\s*(\d+)\s*×\s*(\d+)\s*\|", table)
    if rows:
        bad = 0
        for name, w, h in rows:
            if name in CANCELLED or name not in SPEC:
                continue
            if SPEC[name][:2] != (int(w), int(h)):
                print(f"  !! 尺寸漂移 {name}: 文档 {w}x{h} / SPEC {SPEC[name][0]}x{SPEC[name][1]}")
                bad += 1
                ok = False
        if not bad:
            print(f"  {len(rows)} 条尺寸，全部一致")
    else:
        print("  !! 没从总表里解析到尺寸列（表头格式变了？）")
        ok = False

    print("── 调色板 ──")
    try:
        s_blk = doc.split("**主调色板 `stone`（12 色）**")[1].split("**冷色一档")[0]
        u_blk = doc.split("**冷色一档 `stone+su`（+3 色）**")[1].split("> ★ **阴影不要用半透明黑")[0]
        doc_stone = re.findall(r"`(#[0-9a-f]{6})`", s_blk)
        doc_su = re.findall(r"`(#[0-9a-f]{6})`", u_blk)
        for label, doc_hexes, script_hexes in (("stone", doc_stone, STONE),
                                               ("stone+su", doc_su, SU_LAN)):
            a_ = sorted(hex_to_rgb(h).tolist() for h in doc_hexes)
            b_ = sorted(hex_to_rgb(h).tolist() for h in script_hexes)
            if a_ == b_:
                print(f"  {label}: {len(b_)} 色，一致")
            else:
                ok = False
                print(f"  !! {label} 不一致")
                only_doc = set(map(tuple, a_)) - set(map(tuple, b_))
                only_scr = set(map(tuple, b_)) - set(map(tuple, a_))
                for c in sorted(only_doc):
                    print("      只在文档: #%02x%02x%02x" % tuple(int(v) for v in c))
                for c in sorted(only_scr):
                    print("      只在脚本: #%02x%02x%02x" % tuple(int(v) for v in c))
    except (IndexError, TypeError, KeyError) as e:
        ok = False
        print(f"  !! 解析文档调色板失败：{e}")

    print("── 颜色数上限 ──")
    for cat, doc_n in (("bg", 16), ("prop", 10), ("char", 10), ("ui", 8), ("fx", 6)):
        got = MAX_COLORS.get(cat)
        mark = "OK" if got == doc_n else "!!"
        if got != doc_n:
            ok = False
        print(f"  {mark} {cat}: 文档 {doc_n} / 脚本 {got}")

    print("\n" + ("全部一致。" if ok else "有不一致，需要修。"))
    return 0 if ok else 1


# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(
        description="SD 产出 → 像素资产",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def common(p):
        p.add_argument("--palette", default="stone",
                       help="stone / stone+su / paper / gray8 / gray16 / ui / levels:N / 逗号分隔hex / 文件路径")
        p.add_argument("--alpha-threshold", type=float, default=0.5)

    c = sub.add_parser("convert", help="像素化 + 吸附 + 二值化")
    c.add_argument("infile")
    c.add_argument("--out", required=True)
    c.add_argument("--size", help="WxH，不给就用规格表")
    c.add_argument("--native", action="store_true", help="用输入图尺寸")
    c.add_argument("--levels", type=int, help="覆盖：生成 n 阶灰阶调色板")
    c.add_argument("--resize", action="store_true", help="先缩放到目标尺寸")
    c.add_argument("--pixelate-nearest", action="store_true", help="缩放用最近邻（保硬边）")
    c.add_argument("--trim", action="store_true", help="裁到主体再居中放进画布")
    c.add_argument("--fit", action="store_true", help="等比缩放居中放进画布")
    c.add_argument("--binarize-alpha", action="store_true", help="alpha 二值化")
    c.add_argument("--smooth", type=int, default=0, metavar="N",
                   help="量化前做半径 N 的盒式模糊，去掉 SD 噪点炸出的抖动点（SD 图建议 1）")
    common(c)
    c.set_defaults(func=cmd_convert)

    s = sub.add_parser("snap", help="只做调色板吸附")
    s.add_argument("infile")
    s.add_argument("--out")
    common(s)
    s.set_defaults(func=cmd_snap)

    t = sub.add_parser("tile", help="无缝平铺修复")
    t.add_argument("infile")
    t.set_defaults(func=cmd_tile)

    p = sub.add_parser("palette", help="导出调色板")
    p.add_argument("name", choices=list(PALETTES))
    p.add_argument("--out")
    p.set_defaults(func=cmd_palette)

    k = sub.add_parser("check", help="验收检查")
    k.add_argument("paths", nargs="+")
    k.add_argument("--strict", action="store_true", help="不合格则退出码 1")
    k.set_defaults(func=cmd_check)

    sp = sub.add_parser("spec", help="校验 美术需求.md 与本脚本的规格表是否一致")
    sp.add_argument("--doc", default="doc/美术需求.md")
    sp.set_defaults(func=cmd_spec)

    args = ap.parse_args()
    sys.exit(args.func(args) or 0)


if __name__ == "__main__":
    main()