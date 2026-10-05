# 中文字体放这里

## 需要的文件

**Fusion Pixel 12px**（缝合像素字体）—— 12px 像素中文字体，OFL-1.1 协议，可商用。

下载地址（官方仓库）：
https://github.com/TakWolf/fusion-pixel-font

放到本目录，文件名建议：

```
res://assets/fonts/fusion_pixel_12px.ttf
```

如果是 `.otf` 或 `.ttc` 也行，改成实际文件名即可。

## 导入设置（放好之后需要设一次）

选中字体文件 → 导入（Import）标签页：

| 项 | 值 | 为什么 |
|---|---|---|
| Font → Antialiasing | **None** | 像素字不能抗锯齿 |
| Font → Subpixel Positioning | **Disabled** | 关掉字距微调，12px 网格才对得齐 |
| Font → Hinting | **None** | |
| Font → Force Autohinter | **Off** | |
| Font → Multichannel SDF | **Off** | 走位图路径，不要 SDF |
| Font → Allow System Fallback | **Off** ★ | 见下 |

> ★ **`Allow System Fallback` 必须关掉。**
> 开着的话，遇到字体里没有的字，Godot 会悄悄用系统字体顶上——
> 于是画面里会**混进一段非像素的矢量文字**，而且不报任何错。
> 这个游戏全篇都是像素字，混进来就是穿帮。
> 关掉之后，缺字会直接显示成豆腐块，一眼能看见。
>
> （已核：本字体 `Fusion Pixel 12px Prop zh-Hans` 共 36999 个字形，
> 项目里用到的汉字**全部覆盖，无缺字**。所以关掉 fallback 是安全的。）

主字体定完之后，`project.godot` 里要加一行（我会做）：

```
gui/theme/custom_font="res://assets/fonts/fusion_pixel_12px.ttf"
```

> 已写入，`project.godot` 的 `[gui]` 段。

## 当前状态（已落地）

```
res://assets/fonts/fusion_pixel_12px.ttf        Fusion Pixel 12px Prop zh-Hans · 36999 字形
res://assets/fonts/fusion_pixel_12px.ttf.import  导入设置已按上表配好
```

`project.godot` 已设 `gui/theme/custom_font`。**M0 验收项达成**：
编辑器里 F5，`Boot` 场景左上角应显示「字体已加载：fusion_pixel_12px.ttf」。

## 排版规则

- 所有 UI 字号只用 **12**（正文字）和 **24**（标题），不允许 13/14/15 这类。
- 缩放只用整数倍（2× / 3× / 4×）。
- 640×360 下一行放得下约 **53 个汉字**；正文栏宽不要超过 **44 个汉字**。
- 行距：字号 + 2px。

## 如果这个字体下不到

备选（同样请放到本目录，我改一行配置即可）：

1. **Zpix（最像素）** 12px —— 免费，但许可条款需要你自己再确认一遍商业使用
2. 思源黑体 / 思源宋体 —— **不推荐**，非像素，和整体风格冲突

实在拿不到像素中文字体，退路是：正文用矢量字（可读性优先），只让石碑、印章、
账本上的**印刷体**用像素处理。视觉一致性会打折，但不影响游戏做完。

---

相关文档：`doc/技术方案.md`（3.3 节）、`doc/美术需求.md`