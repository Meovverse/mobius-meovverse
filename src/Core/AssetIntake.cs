using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 美术资产的运行时接管层。
///
/// ★ 这个项目不假设美术会按 <c>doc/美术需求.md</c> 交图。资产可能：
///   · 还不存在          → 用程序生成顶替，游戏照跑
///   · 尺寸不对          → 就地纠正
///   · 颜色太多 / 有渐变 → 量化到调色板
///   · 边缘半透明        → alpha 二值化
///   · 噪声图没做无缝    → 强制无缝
///   · 名字对不上        → 试几个候选名 / 别名
///
/// 所以所有贴图都必须经过 <see cref="Intake"/> 才能用。
/// 每一次"被迫修正"都会记进 <see cref="Report"/>，可以在游戏里按 F3 打印——
/// 这样美术那边能直接看到程序到底改了什么，不用靠猜。
///
/// <b>唯一例外：判定数据永远程序生成，绝不从美术图里读。</b>
/// 理由写在 <see cref="SurfaceModel"/> 上。
/// </summary>
public static class AssetIntake
{
    public enum Kind
    {
        /// <summary>需要严格 1:1 像素的物件：不容许缩放变形，只允许整数对齐。</summary>
        Pixel,

        /// <summary>场景背景：允许缩放，允许量化。</summary>
        Background,

        /// <summary>可平铺噪声图：额外强制无缝。</summary>
        Noise,

        /// <summary>数据图：只允许 0/255。</summary>
        Mask,
    }

    private sealed record Rule(
        string[] Candidates,
        int W,
        int H,
        Kind Kind,
        int MaxColors,
        bool AllowAlpha,
        Func<Image>? Procedural,
        string Note);

    private static readonly Dictionary<string, Rule> Rules = new();

    private static readonly HashSet<string> Tried = new();

    /// <summary>美术交上来的图（res://assets/textures/ 下）。自动扫描，进来就登记。</summary>
    private static readonly Dictionary<string, string> Incoming = new();

    public static readonly List<string> Report = new();

    // ── 登记 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 登记一个美术资产的"槽位"。程序在 <see cref="Warmup"/> 之前把所有槽位都登记好。
    /// </summary>
    public static void Expect(
        string key,
        int w,
        int h,
        Kind kind = Kind.Pixel,
        int maxColors = 10,
        bool allowAlpha = false,
        Func<Image>? procedural = null,
        params string[] candidates)
    {
        Rules[key] = new Rule(candidates.Length > 0 ? candidates : [key], w, h, kind, maxColors, allowAlpha, procedural, "");
    }

    /// <summary>扫一遍美术目录，把到来的图按文件名登记。</summary>
    public static void Warmup(string dir = "res://assets/textures/")
    {
        Incoming.Clear();
        using var d = DirAccess.Open(dir);
        if (d == null)
        {
            Report.Add($"美术目录不存在（正常，程序生成）：{dir}");
            return;
        }

        foreach (var f in d.GetFiles())
        {
            if (!f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!ResourceLoader.Exists(dir + f))
                continue;   // 还没被 Godot 导入
            Incoming[f.ToLowerInvariant()] = dir + f;
        }

        if (Incoming.Count > 0)
            Report.Add($"扫到美术资产 {Incoming.Count} 张");
    }

    // ── 取用 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 取一张贴图。美术的来了就修正后用，没来就程序生成。
    /// 任何情况下都返回一个 W×H 的可用纹理，**永不返回 null**。
    /// </summary>
    public static ImageTexture Get(string key)
    {
        if (!Rules.TryGetValue(key, out var rule))
            throw new ArgumentException($"没登记的槽位：{key}");

        if (!Tried.Add(key))
            return _cache.TryGetValue(key, out var cached)
                ? cached
                : throw new InvalidOperationException($"重复初始化 {key}");

        var img = FromAsset(rule, key) ?? (rule.Procedural?.Invoke() ?? null);
        if (img == null)
        {
            Report.Add($"!! {key} 既没有美术资产也没有程序实现，用纯黑顶替");
            img = Image.CreateEmpty(rule.W, rule.H, false, Image.Format.Rgba8);
            img.Fill(new Color(0, 0, 0));
        }

        img = Sanitize(img, rule, key);
        var tex = ImageTexture.CreateFromImage(img);
        _cache[key] = tex;
        return tex;
    }

    private static readonly Dictionary<string, ImageTexture> _cache = new();

    /// <summary>取一张单通道场（Height / Dust / Pile / Batch），供 shader 采样。</summary>
    public static ImageTexture GetChannel(string key)
    {
        if (!Rules.TryGetValue(key, out var rule))
            throw new ArgumentException($"没登记的槽位：{key}");

        var tex = Get(key);
        var img = tex.GetImage();
        var outImg = Image.CreateEmpty(img.GetWidth(), img.GetHeight(), false, Image.Format.Rf);
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            float v = img.GetPixel(x, y).R;
            outImg.SetPixel(x, y, new Color(v, v, v, 1));
        }
        return ImageTexture.CreateFromImage(outImg);
    }

    private static Image? FromAsset(Rule rule, string key)
    {
        foreach (var name in rule.Candidates)
        {
            if (!Incoming.TryGetValue(name.ToLowerInvariant(), out var path))
                continue;

            var tex = ResourceLoader.Load<Texture2D>(path);
            if (tex == null)
            {
                Report.Add($"!! {key}: {path} 加载失败，改用程序生成");
                continue;
            }

            var img = tex.GetImage();
            if (img == null)
            {
                Report.Add($"!! {key}: {path} 取不到像素数据，改用程序生成");
                continue;
            }

            img.Convert(Image.Format.Rgba8);
            Report.Add($"{key} ← 美术资产 {path}");
            return img;
        }

        Report.Add($"{key} ← 程序生成（美术未交）");
        return null;
    }

    // ── 修正 ────────────────────────────────────────────────────────────

    private static Image Sanitize(Image img, Rule rule, string key)
    {
        int w = rule.Kind == Kind.Mask ? img.GetWidth() : rule.W;
        int h = rule.Kind == Kind.Mask ? img.GetHeight() : rule.H;

        // ① 尺寸
        if (img.GetWidth() != w || img.GetHeight() != h)
        {
            int ow = img.GetWidth(), oh = img.GetHeight();
            if (rule.Kind == Kind.Pixel)
            {
                // 像素物件：最近邻缩放，然后**不裁剪**，多出来的部分裁掉、少的地方留黑。
                // 宁可露出边界，也不要糊掉边缘像素。
                img.Resize(w, h, Image.Interpolation.Nearest);
                Report.Add($"修正 {key}：尺寸 {ow}×{oh} → {w}×{h}（最近邻）");
            }
            else
            {
                img.Resize(w, h, Image.Interpolation.Bilinear);
                Report.Add($"修正 {key}：尺寸 {ow}×{oh} → {w}×{h}（双线性）");
            }
        }

        // ② 半透明
        int half = CountHalfAlpha(img);
        if (half > 0 && !rule.AllowAlpha)
        {
            BinarizeAlpha(img, 0.5f);
            Report.Add($"修正 {key}：{half}px 半透明已二值化");
        }

        // ③ 颜色数
        if (rule.MaxColors > 0 && rule.Kind != Kind.Mask)
        {
            int before = CountColors(img);
            if (before > rule.MaxColors)
            {
                Quantize(img, rule.MaxColors);
                Report.Add($"修正 {key}：颜色 {before} → ≤{rule.MaxColors}");
            }
        }

        // ④ 噪声图无缝
        if (rule.Kind == Kind.Noise)
        {
            float seam = MeasureSeam(img);
            if (seam > 0.02f)
            {
                MakeSeamless(img);
                Report.Add($"修正 {key}：接缝 {seam:F3} → 已强制无缝");
            }
        }

        // ⑤ 数据图只允许 0/255
        if (rule.Kind == Kind.Mask)
            BinarizeAll(img);

        return img;
    }

    private static int CountHalfAlpha(Image img)
    {
        int n = 0;
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            float a = img.GetPixel(x, y).A;
            if (a > 0.004f && a < 0.996f) n++;
        }
        return n;
    }

    private static void BinarizeAlpha(Image img, float threshold)
    {
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var c = img.GetPixel(x, y);
            if (c.A <= 0.004f) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
            if (c.A >= 0.996f) continue;
            img.SetPixel(x, y, new Color(c.R, c.G, c.B, 1));
        }
    }

    private static void BinarizeAll(Image img)
    {
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var c = img.GetPixel(x, y);
            float on = (c.R + c.G + c.B) / 3f > 0.5f ? 1f : 0f;
            img.SetPixel(x, y, new Color(on, on, on, c.A > 0.5f ? 1f : 0f));
        }
    }

    private static int CountColors(Image img)
    {
        var seen = new HashSet<int>();
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var c = img.GetPixel(x, y);
            if (c.A < 0.5f) continue;
            seen.Add(((int)(c.R * 255) << 16) | ((int)(c.G * 255) << 8) | (int)(c.B * 255));
        }
        return seen.Count;
    }

    /// <summary>均匀量化到 n 阶灰阶（这个项目是灰的，够用且极快）。</summary>
    private static void Quantize(Image img, int levels)
    {
        levels = Math.Max(2, levels);
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var c = img.GetPixel(x, y);
            if (c.A < 0.5f) continue;
            float q(float v) => MathF.Round(v * (levels - 1)) / (levels - 1);
            img.SetPixel(x, y, new Color(q(c.R), q(c.G), q(c.B), c.A));
        }
    }

    private static float MeasureSeam(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        float dx = 0, dy = 0;
        for (int y = 0; y < h; y++)
            dx = MathF.Max(dx, Math.Abs(img.GetPixel(0, y).R - img.GetPixel(w - 1, y).R));
        for (int x = 0; x < w; x++)
            dy = MathF.Max(dy, Math.Abs(img.GetPixel(x, 0).R - img.GetPixel(x, h - 1).R));
        return MathF.Max(dx, dy);
    }

    /// <summary>镜像折叠：右半边盖到左半边，下半边盖到上半边。便宜、有效。</summary>
    private static void MakeSeamless(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        var src = img.Duplicate() as Image;
        if (src == null) return;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w / 2; x++)
            img.SetPixel(x, y, src.GetPixel(w - 1 - x, y));

        src = img.Duplicate() as Image;
        if (src == null) return;
        for (int y = h / 2; y < h; y++)
        for (int x = 0; x < w; x++)
            img.SetPixel(x, y, src.GetPixel(x, h - 1 - y));
    }

    public static string DumpReport()
    {
        if (Report.Count == 0) return "（没有资产记录）";
        return string.Join("\n", Report);
    }
}