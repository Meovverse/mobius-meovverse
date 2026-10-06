using System;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 全部纹理的程序生成。
///
/// ★ 这个项目不假设美术会交图。程序先把每一类纹理都做出来，游戏立刻可玩；
/// 美术的图到了之后 <see cref="AssetIntake"/> 会自动接管并做修正。
/// 所以这里的代码不是"临时占位"，它是**默认路径**，美术是覆盖层。
///
/// 三个要点：
///  1. 噪声全部**可平铺**（用周期函数，不是 Math.Random）
///  2. 「刀口 A / 刀口 B」是玩法判据，差别做在**毛边朝向**上，不只是"乱不乱"
///  3. 石材/纸的明暗是三阶硬边，不是渐变
/// </summary>
public static class ProcGen
{
    // ── 可平铺噪声 ──────────────────────────────────────────────────────

    /// <summary>周期性值噪声。period 必须整除网格，保证左右上下接得上。</summary>
    private static int PosMod(int a, int m) => ((a % m) + m) % m;

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    private static float TileValue(int x, int y, int period, int seed)
    {
        int px = PosMod(x, period);
        int py = PosMod(y, period);
        // 整数散列 → [0,1)
        unchecked
        {
            uint h = (uint)(px * 374761393 + py * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>可平铺的双线性值噪声。</summary>
    private static float TileNoise(int x, int y, int period, int seed)
    {
        int p = Math.Max(1, period);
        int x0 = FloorDiv(x, p), y0 = FloorDiv(y, p);
        float fx = Smooth(x - x0 * p), fy = Smooth(y - y0 * p);
        float a = TileValue(x0, y0, p, seed);
        float b = TileValue(x0 + 1, y0, p, seed);
        float c = TileValue(x0, y0 + 1, p, seed);
        float d = TileValue(x0 + 1, y0 + 1, p, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // ── 给 Art / SteleBuilder 用的公开图元 ──────────────────────────────

    /// <summary>可平铺散列值，给别的类用。</summary>
    public static float TileValueFor(int x, int y, int seed) => TileValue(x, y, 256, seed);

    /// <summary>可平铺值噪声，给别的类用。</summary>
    public static float TileNoiseFor(int x, int y, int period, int seed) => TileNoise(x, y, period, seed);

    /// <summary>空白透明图。</summary>
    public static Image NewTransparent(int w, int h) => NewImage(w, h);

    /// <summary>项目字体。启动时设一次，供需要画字的地方使用。</summary>
    public static Font? CachedFont { get; set; }

    private static Image? _chiselA, _chiselB;
    private static Image? _dustN, _fiberN, _stoneN, _cementN;

    /// <summary>刀口 A（老吴那把錾子），缓存复用。</summary>
    public static Image ChiselA => _chiselA ??= ChiselA_();

    /// <summary>刀口 B（补刻），缓存复用。</summary>
    public static Image ChiselB => _chiselB ??= ChiselB_();

    public static Image DustNoise => _dustN ??= Dust_();
    public static Image FiberNoise => _fiberN ??= PaperFiber_();
    public static Image StoneNoise => _stoneN ??= StoneGrain_();
    public static Image CementNoise => _cementN ??= CementNoise_();

    /// <summary>给别的类用的可平铺分形噪声。</summary>
    public static float FractalAccessor(int x, int y, int period, int seed, int octaves)
        => Fractal(x, y, period, seed, octaves);

    private static float Fractal(int x, int y, int period, int seed, int octaves)
    {
        float sum = 0, amp = 1f, norm = 0;
        int per = period;
        for (int o = 0; o < octaves; o++)
        {
            sum += TileNoise(x, y, per, seed + o * 7919) * amp;
            norm += amp;
            amp *= 0.5f;
            per *= 2;
        }
        return sum / norm;
    }

    // ── 调色板（对应 doc/美术需求.md §1.3）────────────────────────────────

    public static readonly Color Ink = new("141414");
    public static readonly Color StoneDeep = new("1a1a1a");
    public static readonly Color StoneMid = new("3a3a3a");
    public static readonly Color StoneShade = new("5f5b52");
    public static readonly Color StoneLit = new("6e6e6e");
    public static readonly Color CementMid = new("8a867c");
    public static readonly Color StoneHi = new("a8a8a8");
    public static readonly Color DustWhite = new("d8d8d0");
    public static readonly Color PaperOld = new("b9ad91");
    public static readonly Color PaperMid = new("d5cbb2");
    public static readonly Color PaperLit = new("ece4cf");
    public static readonly Color SealRed = new("a8352c");
    public static readonly Color SuitDark = new("2a3038");
    public static readonly Color SuitMid = new("3a4450");
    public static readonly Color SuitLit = new("4c5a68");

    /// <summary>把连续值压成三阶硬边。这是这个项目的明暗规则。</summary>
    private static float Band(float v, float lo = 0.33f, float hi = 0.66f) =>
        v < lo ? 0f : v < hi ? 0.5f : 1f;

    private static Image NewImage(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        return img;
    }

    // ── 噪声图 ──────────────────────────────────────────────────────────

    /// <summary>石材颗粒：低频斑块 + 高频 1px 颗粒。64×64 可平铺。</summary>
    private static Image StoneGrain_()
    {
        const int S = 64;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float low = Fractal(x, y, 8, 11, 3);
            float grain = TileValue(x, y, S, 31);
            float v = low * 0.65f + grain * 0.35f;
            float c = Band(v, 0.30f, 0.62f);
            img.SetPixel(x, y, new Color(c, c, c));
        }
        return img;
    }

    /// <summary>
    /// ★ 刀口纹理 A —— 老吴 2021 年刻的那把錾子。
    ///
    /// 特征（程序版）：
    ///   · 横向条纹**长而连续**（>= 8px）
    ///   · 边缘有 1px 的翻起毛边，**全部朝同一个方向**（右）
    ///   · 密度中等、亮度均匀
    /// </summary>
    private static Image ChiselA_()
    {
        const int S = 32;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            // 长条纹：低频纵向噪声决定条纹高度
            float band = TileNoise(0, y, S / 4, 5);
            float inBand = band > 0.42f ? 1f : 0f;

            // 连续性：同一个 y 上的 x 几乎不变 → 长条
            float len = 0.55f + TileNoise(0, y, S / 4, 9) * 0.45f;

            // 毛边：朝右的一侧亮一格（★ A 的毛边只有一个朝向）
            float edgeRight = TileValue(x - 1, y, S, 77) > 0.80f ? 1f : 0f;

            float v = inBand * len + edgeRight * 0.25f;

            float c = Math.Clamp(v, 0f, 1f);
            c = Band(c, 0.40f, 0.70f);
            img.SetPixel(x, y, new Color(c, c, c));
        }
        return img;
    }

    /// <summary>
    /// ★ 刀口纹理 B —— 补刻。同一把錾子，但握姿不同、手劲没控制好。
    ///
    /// 特征：
    ///   · 横向条纹**断裂**（长度只有 A 的 1/3）
    ///   · 毛边**两个方向都有** ← 这是"不是同一个人同一把錾子"的硬证据
    ///   · 有崩口（黑缺口）、密度更高、亮度不匀
    /// </summary>
    private static Image ChiselB_()
    {
        const int S = 32;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            // 短条纹：条纹沿 x 方向也断
            float bandY = TileNoise(0, y, S / 4, 5);
            float bandX = TileNoise(x, 0, S / 4, 17);
            float inBand = (bandY > 0.42f && bandX > 0.38f) ? 1f : 0f;

            // 崩口
            float chip = TileValue(x, y, S, 23) > 0.86f ? 1f : 0f;

            // ★ 毛边双向：右翻起 + 左翻起
            float r = TileValue(x - 1, y, S, 77) > 0.86f ? 1f : 0f;
            float l = TileValue(x + 1, y, S, 77) > 0.86f ? 1f : 0f;

            float v = inBand * 0.9f + r * 0.3f + l * 0.3f - chip * 0.6f;
            float c = Math.Clamp(v, 0f, 1f);
            c = Band(c, 0.36f, 0.66f);
            img.SetPixel(x, y, new Color(c, c, c));
        }
        return img;
    }

    /// <summary>石粉：稀疏白点，1px 为主，少量 2px 团。32×32 可平铺。</summary>
    private static Image Dust_()
    {
        const int S = 32;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float v = TileValue(x, y, S, 3) > 0.72f ? 1f : 0f;
            // 少量 2px 团
            if (TileValue(x / 2, y / 2, S / 2, 9) > 0.80f) v = 1f;
            float c = v > 0.5f ? 1f : 0.15f;
            img.SetPixel(x, y, new Color(c, c, c, c));
        }
        return img;
    }

    /// <summary>纸纤维：横向拉丝，稀疏，每 6–10px 一根。64×64 可平铺。</summary>
    private static Image PaperFiber_()
    {
        const int S = 64;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            // 纤维是横向的：主要由 y 决定，沿 x 稀疏出现
            float row = TileNoise(0, y, S / 4, 41);
            float along = TileNoise(x, 0, S / 8, 43);
            float v = (row > 0.60f && along > 0.55f) ? 1f : 0.12f;
            img.SetPixel(x, y, new Color(v, v, v, v));
        }
        return img;
    }

    /// <summary>水泥：砂粒 + 2px 气孔。64×64 可平铺。</summary>
    private static Image CementNoise_()
    {
        const int S = 64;
        var img = NewImage(S, S);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float sand = Fractal(x, y, 16, 61, 2);
            float pit = TileValue(x / 2, y / 2, S / 2, 67) > 0.90f ? 0.25f : 1f;
            float v = sand * 0.7f * pit + 0.15f;
            float c = Band(v, 0.38f, 0.68f);
            img.SetPixel(x, y, new Color(c, c, c));
        }
        return img;
    }

    // ── 石碑 ────────────────────────────────────────────────────────────

    /// <summary>碑面石材底图（不含字）。三阶明暗 + 左上 45° 高光带 + 水平层理。</summary>
    public static Image SteleFace(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // 水平层理（山西黑的沉积纹）
            float strata = TileNoise(x * 0, y, Math.Max(2, h / 8), 71);
            // 大块斑驳
            float blotch = Fractal(x, y, Math.Max(2, w / 6), 13, 2);
            // 整体明暗：左上亮右下暗
            float lightDir = 1f - ((float)x / w * 0.55f + (float)y / h * 0.45f);

            float v = 0.30f + blotch * 0.22f + strata * 0.14f + lightDir * 0.30f;

            // 斜向高光带（约 8% 面积）
            float hl = (float)x / w * 0.6f + (float)y / h * 0.8f;
            bool inHighlight = hl is > 0.62f and < 0.74f;
            if (inHighlight) v += 0.42f;

            float c = Band(v, 0.36f, 0.62f);
            Color col = c switch
            {
                0f => StoneDeep,
                0.5f => StoneMid,
                _ => inHighlight ? StoneHi : StoneLit,
            };
            img.SetPixel(x, y, col);
        }
        return img;
    }

    /// <summary>新碑：更白更平，只有两阶，接缝很窄很亮。终章用。</summary>
    public static Image SteleNew(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float lightDir = 1f - ((float)x / w * 0.35f + (float)y / h * 0.25f);
            float v = 0.55f + lightDir * 0.35f + Fractal(x, y, Math.Max(2, w / 8), 91, 2) * 0.10f;
            img.SetPixel(x, y, v > 0.72f ? StoneHi : StoneLit);
        }
        return img;
    }

    /// <summary>底座水泥：一条很窄的新接缝 + 细裂纹。</summary>
    public static Image SteleBase(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int seamY = h / 2;
        int seamX = w / 2;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = Fractal(x, y, Math.Max(2, w / 6), 101, 2);

            // 十字接缝：很窄（3–4px）、很亮
            bool seam = Math.Abs(y - seamY) <= 2 || Math.Abs(x - seamX) <= 2;

            // 细裂纹：稀疏的暗线
            float crack = TileNoise(x, y, Math.Max(2, w / 4), 113) > 0.90f ? 0.2f : 1f;

            Color col;
            if (seam) col = StoneHi;
            else
            {
                float t = v * crack;
                col = t > 0.60f ? StoneHi : t > 0.42f ? CementMid : StoneShade;
            }
            img.SetPixel(x, y, col);
        }
        return img;
    }

    // ── 纸 ──────────────────────────────────────────────────────────────

    /// <summary>纸页底（带 alpha 的竖版纸）。程序版：边缘微不规则 + 1px 暗边。</summary>
    public static Image PaperSheet(int w, int h)
    {
        var img = NewImage(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // 边缘不规则：用周期噪声让边界抖动 ±2px
            int jitterX = (int)(TileNoise(0, y, Math.Max(2, h / 8), 7) * 3f) - 1;
            int jitterY = (int)(TileNoise(x, 0, Math.Max(2, w / 8), 13) * 3f) - 1;
            bool inside = x >= 1 + jitterX && x < w - 1 - jitterX
                       && y >= 1 + jitterY && y < h - 1 - jitterY;
            if (!inside) continue;

            float v = Fractal(x, y, Math.Max(2, w / 5), 29, 2);
            Color body = v > 0.58f ? PaperLit : v > 0.40f ? PaperMid : PaperOld;

            // 1px 暗边（纸的厚度）
            bool edge = x <= 2 + jitterX || x >= w - 3 - jitterX
                     || y <= 2 + jitterY || y >= h - 3 - jitterY;
            img.SetPixel(x, y, edge ? PaperOld : body);
        }
        return img;
    }

    /// <summary>
    /// 折痕。一横一竖两条，每条 = 1px 亮线 + 1–2px 外侧暗线，
    /// ★ 中段有 1px 错位 —— 这是"两张纸同源"的唯一可辨认特征。
    /// </summary>
    public static Image PaperCrease(int w, int h)
    {
        var img = NewImage(w, h);
        int cx = w / 2, cy = h / 2;
        int jogY = h / 2, jogX = w * 2 / 3;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // 竖折痕（带中段错位）
            int vx = x < jogX ? cx : cx + 1;
            // 横折痕
            int hy = y < jogY ? cy : cy + 1;

            if (Math.Abs(x - vx) == 0 && Math.Abs(y - cy) < h / 4)
            { img.SetPixel(x, y, PaperLit); continue; }
            if (Math.Abs(x - vx) == 1 && Math.Abs(y - cy) < h / 4)
            { img.SetPixel(x, y, PaperOld); continue; }

            if (Math.Abs(y - hy) == 0 && Math.Abs(x - cx) < w / 4)
            { img.SetPixel(x, y, PaperLit); continue; }
            if (Math.Abs(y - hy) == 1 && Math.Abs(x - cx) < w / 4)
            { img.SetPixel(x, y, PaperOld); continue; }
        }
        return img;
    }

    // ── 印章 / 签名 ─────────────────────────────────────────────────────

    /// <summary>
    /// 朱红圆章。外圈双环 + 五角星。★ 不画弧形文字（留给程序用字体绕排），
    /// 边缘故意缺墨两处。
    /// </summary>
    public static Image StampRound(int size)
    {
        var img = NewImage(size, size);
        float c = size / 2f, r = size / 2f - 1f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c + 0.5f, dy = y - c + 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > r) continue;

            // 外环 + 内环
            bool outer = d > r - 3f;
            bool inner = d > r * 0.62f && d < r * 0.62f + 2f;
            bool ring = outer || inner;

            // 中心五角星
            bool star = false;
            if (d < r * 0.34f)
            {
                // 五角星：半径按 5 瓣余弦起伏
                double ang = Math.Atan2(dy, dx);
                double petal = 0.72 + 0.28 * Math.Cos(5 * ang);
                star = d < r * 0.30f * petal;
            }

            if (!ring && !star) continue;

            // 缺墨：两处固定的斑
            float wear = TileNoise(x, y, size, 5);
            if (wear > 0.86f && d > r * 0.5f) continue;

            img.SetPixel(x, y, new Color(SealRed.R, SealRed.G, SealRed.B, 1));
        }
        return img;
    }

    /// <summary>方形存档章（档案室的章通常是方的），和圆章区分得开。</summary>
    public static Image StampSquare(int size)
    {
        var img = NewImage(size, size);
        int m = 3, b = 2;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool border = (x >= m && x < size - m && y >= m && y < size - m)
                       && (x < m + b || x >= size - m - b || y < m + b || y >= size - m - b);
            bool inner = x == m + 5 || y == m + 5 || x == size - m - 6 || y == size - m - 6;
            if (!border && !inner) continue;
            float wear = TileNoise(x, y, size, 9);
            if (wear > 0.88f) continue;
            img.SetPixel(x, y, new Color(SealRed.R, SealRed.G, SealRed.B, 1));
        }
        return img;
    }

    // ── 粒子 ────────────────────────────────────────────────────────────

    private static Image? _dustP, _chipP;

    /// <summary>石粉粒子（1px 硬边白点）。</summary>
    public static Image DustParticle => _dustP ??= Dot(0xd8d8d0);

    /// <summary>水泥碎屑粒子（不规则碎片）。</summary>
    public static Image CementChip => _chipP ??= CementChip_();

    private static Image CementChip_()
    {
        const int S = 8;
        var img = NewImage(S, S);
        img.SetPixel(3, 3, new Color(0x8a, 0x86, 0x7c, 1));
        img.SetPixel(4, 3, new Color(0x8a, 0x86, 0x7c, 1));
        img.SetPixel(3, 4, new Color(0x6e, 0x6e, 0x6e, 1));
        img.SetPixel(4, 4, new Color(0x6e, 0x6e, 0x6e, 1));
        img.SetPixel(2, 4, new Color(0xa8, 0xa8, 0xa8, 1));
        return img;
    }

    private static Image Dot(uint rgb)
    {
        var img = NewImage(1, 1);
        img.SetPixel(0, 0, new Color(((rgb >> 16) & 0xFF) / 255f,
                                    ((rgb >> 8) & 0xFF) / 255f,
                                    (rgb & 0xFF) / 255f, 1));
        return img;
    }

    // ── 简单背景（美术没来时的兜底）─────────────────────────────────────

    /// <summary>阴天墓地：灰白天空 + 枯草地平线 + 远处碑的剪影。三阶明暗。</summary>
    public static Image BgGraveyard(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int horizon = h * 2 / 3;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color col;
            if (y < horizon)
            {
                // 天空：三阶硬边横带
                float t = (float)y / horizon;
                col = t < 0.4f ? new Color(0xd8, 0xd8, 0xd0)
                   : t < 0.72f ? new Color(0xc0, 0xc0, 0xb8)
                   : new Color(0xa8, 0xa8, 0xa0);
            }
            else
            {
                // 枯草
                float t = (float)(y - horizon) / (h - horizon);
                float n = Fractal(x, y, Math.Max(2, w / 5), 3, 2);
                col = (t * 0.35f + n * 0.3f) > 0.42f ? new Color(0x8a, 0x82, 0x62)
                   : (t * 0.35f + n * 0.3f) > 0.24f ? new Color(0x6e, 0x68, 0x4e)
                   : new Color(0x55, 0x50, 0x3c);
            }
            img.SetPixel(x, y, col);
        }

        // 远处一排碑的剪影（1 阶色，粗糙人形）
        var rng = new Random(20210411);
        for (int i = 0; i < 22; i++)
        {
            int tx = rng.Next(0, w);
            int tw = rng.Next(8, 18);
            int th = rng.Next(24, 52);
            for (int y = horizon - th; y < horizon; y++)
            for (int x = tx; x < tx + tw && x < w; x++)
                img.SetPixel(x, y, new Color(0x6a, 0x6a, 0x68));
        }
        return img;
    }

    /// <summary>纯黑。</summary>
    public static Image Black(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 1));
        return img;
    }
}