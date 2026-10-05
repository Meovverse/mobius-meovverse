# tools —— 像素资产工具链

配合 `doc/美术需求.md` 使用。美术主要用 Stable Diffusion 出图，
本目录负责把 SD 的高分辨率、带渐变、带抗锯齿的输出，
变成引擎能吃的**硬像素资产**，并守住验收标准。

---

## 安装

```bash
pip3 install Pillow numpy
```

（Linux/WSL 用 `pip3`，Windows 上如果用 WSL 跑，路径用 `/mnt/c/...`。）

---

## 六个子命令

### `convert` —— 像素化 + 调色板吸附 + alpha 二值化

最常用的一条。SD 出的图必须过它。

```bash
# 场景背景：SD 的 1024×576 → 640×360 硬像素，12 阶灰
python3 tools/pixelart.py convert raw/bg_graveyard_area_A.png \
    --out assets/textures/bg_graveyard_area_A.png \
    --size 640x360 --resize --smooth 2 --palette levels:12

# 道具：裁到主体 → 等比放进画布 → 吸附调色板 → alpha 二值化
python3 tools/pixelart.py convert raw/prop_toolbox.png \
    --out assets/textures/prop_toolbox.png \
    --size 160x100 --trim --smooth 2 --palette stone --binarize-alpha

# 纸类道具：偏暖的调色板
python3 tools/pixelart.py convert raw/prop_ledger_page.png \
    --out assets/textures/prop_ledger_page.png \
    --size 280x360 --trim --smooth 2 --palette paper --binarize-alpha

# 带一点冷色的（苏老师的外套）
python3 tools/pixelart.py convert raw/char_suteacher_wheelchair.png \
    --out assets/textures/char_suteacher_wheelchair.png \
    --size 160x220 --trim --smooth 2 --palette stone+su --binarize-alpha
```

不给 `--size` 时，脚本会从内置的资产规格表（`SPEC`）里按文件名查尺寸。

| 参数 | 作用 |
|---|---|
| `--size WxH` | 目标尺寸 |
| `--native` | 用输入图的尺寸 |
| `--resize` | 先缩放到目标尺寸（**降采样用 BOX，天然去抗锯齿**） |
| `--pixelate-nearest` | 缩放改用最近邻（保硬边，用于已经像素化的图） |
| `--trim` | 裁掉四周透明边 |
| `--fit` | 等比缩放居中放进画布 |
| `--palette` | `stone` / `stone+su` / `paper` / `gray8` / `gray16` / `ui` / `levels:N` / `逗号分隔hex` / 文件路径 |
| `--smooth N` | ★ 量化前做半径 N 的盒式模糊。**SD 输出一律用 `--smooth 2`** |
| `--binarize-alpha` | **alpha 二值化**（0 / 255）。道具类几乎都要加 |
| `--alpha-threshold` | 二值化阈值，默认 0.5 |

### `snap` —— 只做调色板吸附，不改尺寸

已经手工像素化好的图，只想把颜色收到调色板上时用。

```bash
python3 tools/pixelart.py snap assets/textures/prop_scrap_stone.png --palette stone
```

会报出平均/最大色差，方便判断是否收得太狠。

### `tile` —— 无缝平铺修复

```bash
python3 tools/pixelart.py tile assets/textures/noise_chisel_a_32.png
```

把右半边镜像到左半边、下半边镜像到上半边，修掉接缝。
程序生成的噪声图也应该过一遍这道保险。

### `palette` —— 导出调色板

```bash
python3 tools/pixelart.py palette stone            # → palette_stone.png / .gpl
python3 tools/pixelart.py palette stone+su --out /tmp/pal
```

生成 PNG（肉眼预览）和 GIMP `.gpl`（Aseprite / Photoshop / Krita 都能导）。

**美术在 Aseprite 里作画时**：先 `File → Import Palette` 导 `.gpl`，
然后新建图层用 `Index` 模式画 —— 这样画出来的图天然就在调色板里，
`check` 就不会再报"调色板外颜色"。

### `spec` —— 校验「文档」和「脚本」没有漂移

`doc/美术需求.md` 的资产表 / 调色板，和本脚本里的 `SPEC` / `STONE` 表是
**同一份事实的两个副本**。改了一边忘了另一边是 GameJam 里最容易出的错。

```bash
python3 tools/pixelart.py spec
```

比对四项：**文件名 / 尺寸 / 调色板色号 / 颜色数上限**。有差异返回退出码 1。

```
── 资产清单 ──
  45 张，文件名与 SPEC 完全一致
  45 条尺寸，全部一致
── 调色板 ──
  stone: 12 色，一致
  stone+su: 3 色，一致
── 颜色数上限 ──
  OK bg: 文档 16 / 脚本 16
  ...
全部一致。
```

> ★ **改资产清单、尺寸、调色板、颜色数上限时，务必跑一次这个。**
> 已取消的资产要加进脚本里的 `CANCELLED` 集合，否则会一直报「只在文档里」。

### `check` —— 验收

```bash
python3 tools/pixelart.py check assets/textures/ --strict
python3 tools/pixelart.py check assets/textures/prop_toolbox.png
```

检查项：

- [ ] 文件名在规格表里、尺寸正确
- [ ] 半透明像素（alpha 只允许 0 / 255）——白名单：`ui_loupe_glass_96.png`
- [ ] 颜色数上限（背景 16 / 道具 10 / 角色 10 / UI 8 / 特效 6）
- [ ] `mask_*` 只用纯黑和纯白（通道数据的硬要求）
- [ ] `noise_*` 无缝：左右边缘、上下边缘的差异必须为 0

`--strict` 会在有不合格项时返回退出码 1，可以进 CI。

---

## 内置调色板

| 名称 | 色数 | 用途 |
|---|---|---|
| `stone` | 12 | 主调色板。石材 + 纸 + 墨 + 朱红 + 水泥（对应 `美术需求.md` §1.3） |
| `stone+su` | 15 | `stone` + 苏老师外套的三档冷色 |
| `paper` | 9 | 纸类：暖灰 4 阶 + 冷灰 4 阶 + 朱红 |
| `gray8` / `gray16` | 8 / 16 | 纯灰阶 |
| `ui` | 9 | UI 用（8 灰 + 朱红） |
| `levels:N` | N | 自动生成 N 阶灰阶（末端微偏暖，因为这个游戏的光是阴天） |

---

## 已知的坑

- **`--resize` 不要和 `--pixelate-nearest` 一起用**。
  已经是像素图的资产应该用 `--pixelate-nearest`（保硬边），
  SD 原图才用 `--resize`（BOX 降采样天然去抗锯齿）。
- **`--trim` 之后再 `--fit`**：本脚本的 `--trim` 已经包含"等比放进画布"，
  两个一起加不会更糟，但会多做一次缩放。
- **`--smooth` 只能用于 SD 原始输出**。已经手工像素化的资产加 `--smooth` 会糊掉硬边。
- **mask 类不要过 `convert`**。`mask_*` / `map_*` 是通道数据，
  必须手工在像素软件里画、或者由程序从 `B2` 推导。跑 `check` 就行。
- **中文路径**：`--in` / `--out` 尽量用 ASCII 路径，避免 Windows 下的编码问题。

---

## 批量处理

如果一次出了一堆 SD 草图：

```bash
for f in raw/*.png; do
  n=$(basename "$f")
  python3 tools/pixelart.py convert "$f" \
    --out "assets/textures/$n" \
    --resize --trim --binarize-alpha \
    --palette stone || echo "FAILED: $n"
done
python3 tools/pixelart.py check assets/textures/ --strict
```

尺寸不用给——`SPEC` 表里有。

---

相关文档：`doc/美术需求.md`（§1.5 SD 工作流、附录 A 提示词库）