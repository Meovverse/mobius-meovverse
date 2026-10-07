using System;
using Godot;

namespace GraveCanTell.Core;

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
        // ★ 陈年潜伏 bug（M0 就在）：局部坐标没除以 period——Smooth 收到
        //   [0,48) 的格内坐标，t²(3−2t) 直接爆到千万级，双线性 lerp 把值甩出
        //   色域 → SetPixel 钳位成纯黑/纯白棋盘。铺子内景杂乱的病根。
        float fx = Smooth((x - x0 * p) / (float)p), fy = Smooth((y - y0 * p) / (float)p);
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
    /// <summary>
    /// 按 0–255 造颜色。
    ///
    /// ★★ 别直接写 <c>new Color(0xd8, 0xd8, 0xd0)</c>——
    ///   Godot 的三参数 Color 构造按 **0–1 浮点**解释，216 会被钳成 1.0，
    ///   结果整张图全白。这个坑很安静：不报错，只是图白了。
    ///   要么用字符串构造 <c>new Color("d8d8d0")</c>，要么用这个。
    /// </summary>
    public static Color Rgb(int r, int g, int b, float a = 1f) =>
        new(r / 255f, g / 255f, b / 255f, a);

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
    /// <summary>
    /// P3 登记本 · 双页：左页 2006（两行）／右页 2026（18 行线索），中间一道装订缝。
    /// 新剧本（墓时）把线索表画在这本子上，所以它不再是单页纸。
    /// </summary>
    public static Image PaperSpread(int pageW, int h)
    {
        int gap = 8;
        var img = Image.CreateEmpty(pageW * 2 + gap, h, false, Image.Format.Rgba8);
        img.Fill(Rgb(96, 84, 68));                       // 书脊：露出的封面布纹
        var l = PaperSheet(pageW, h);
        var r = PaperSheet(pageW, h);
        img.BlitRect(l, new Rect2I(0, 0, pageW, h), new Vector2I(0, 0));
        img.BlitRect(r, new Rect2I(0, 0, pageW, h), new Vector2I(pageW + gap, 0));
        // 装订缝两侧压一点影，双页才有"摊开"的体积
        for (int y = 0; y < h; y++)
        {
            for (int k = 0; k < gap; k++)
            {
                float sh = k < gap / 2 ? 0.72f + 0.28f * (k / (float)(gap / 2))
                                       : 0.72f + 0.28f * ((gap - 1 - k) / (float)(gap / 2));
                img.SetPixel(pageW + k, y, new Color(96 * sh / 255f, 84 * sh / 255f, 68 * sh / 255f, 1f));
            }
            var le = img.GetPixel(pageW - 1, y);
            var re = img.GetPixel(pageW + gap, y);
            img.SetPixel(pageW - 1, y, new Color(le.R * 0.9f, le.G * 0.9f, le.B * 0.9f, 1f));
            img.SetPixel(pageW + gap, y, new Color(re.R * 0.9f, re.G * 0.9f, re.B * 0.9f, 1f));
        }
        return img;
    }

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
        img.SetPixel(3, 3, Rgb(138, 134, 124, 1));
        img.SetPixel(4, 3, Rgb(138, 134, 124, 1));
        img.SetPixel(3, 4, Rgb(110, 110, 110, 1));
        img.SetPixel(4, 4, Rgb(110, 110, 110, 1));
        img.SetPixel(2, 4, Rgb(168, 168, 168, 1));
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
                col = t < 0.4f ? Rgb(216, 216, 208)
                   : t < 0.72f ? Rgb(192, 192, 184)
                   : Rgb(168, 168, 160);
            }
            else
            {
                // 枯草
                float t = (float)(y - horizon) / (h - horizon);
                // 低频起伏用正弦而不是 Fractal：Fractal 的实际取值范围没保证，
                // 试过 128/58/16/32 四种周期，出来要么是一片平板、要么是方块棋盘。
                // 正弦的值域是确定的 [0,1]，低频斑驳交给它。
                float n = 0.5f + 0.5f * Mathf.Sin(x * 0.031f + y * 0.017f)
                                     * Mathf.Cos(x * 0.011f - y * 0.023f);
                // 再叠一层逐像素抖动，把 16px 晶格之间的平滑过渡打碎
                uint hh = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                hh ^= hh >> 13; hh *= 0x5bd1e995u; hh ^= hh >> 15;
                float dith = (hh & 0xFFFF) / 65535f;
                // 枯草：留一点暖色免得画面死掉，但不能暖到跟灰阶场景脱节。
                // 用连续插值而不是硬阈值分三档——硬阈值在低频噪声上会出来一块块方斑。
                // v 要钳位：Fractal 叠三个倍频会超过 1，Lerp 权重越界后颜色会冲成白块
                float v = Mathf.Clamp(0.40f + t * 0.24f + (n - 0.5f) * 0.24f + (dith - 0.5f) * 0.10f, 0f, 1f);
                col = v < 0.30f
                    ? Rgb(76, 76, 65).Lerp(Rgb(100, 99, 84), Mathf.Clamp(v / 0.30f, 0f, 1f))
                    : Rgb(100, 99, 84).Lerp(Rgb(126, 124, 106), Mathf.Clamp((v - 0.30f) / 0.35f, 0f, 1f));
            }
            img.SetPixel(x, y, col);
        }

        DrawFarLayer(img, w, horizon);
        DrawSteleRow(img, w, horizon);
        return img;
    }

    /// <summary>
    /// 中景那一排墓碑。
    ///
    /// ★ 优先用美术素材 <c>stele_bg_01..04.png</c>——它们是从队友给的俯视地图里
    ///   抠出来的 3/4 视角墓碑，灰阶 + 12 级量化之后已经能看。
    ///   没有素材时退回程序生成的粗糙剪影（几个灰矩形）。
    /// </summary>
    /// <summary>
    /// 地平线以上的远景层。
    ///
    /// 用俯视素材 <c>地图/墓地.png</c> 顶部横带（y 0..336，避开教堂/水井/邮筒/雏菊）
    /// 缩到 640 宽，去色、压对比、竖向渐变、轻微模糊之后当大气远景。
    /// 原图是俯视地图，直接当背景会和"平视读碑面"的玩法打架；
    /// 这么处理之后它不再读作俯视，只是一层雾里的远景——所以能留。
    /// </summary>
    private static void DrawFarLayer(Image img, int w, int horizon)
    {
        var path = "res://assets/textures/bg_graveyard_far.png";
        if (!ResourceLoader.Exists(path)) return;
        var far = ResourceLoader.Load<Texture2D>(path)?.GetImage();
        if (far == null) return;
        far.Convert(Image.Format.Rgba8);

        int fh = Math.Min(far.GetHeight(), horizon);
        // BlitRect 是 C 层整块拷贝；逐像素 SetPixel 要跑 14 万次，没必要。
        img.BlitRect(far, new Rect2I(0, 0, far.GetWidth(), fh), new Vector2I(0, horizon - fh));

        // 远景比天空矮时，用它的顶行往上补满，否则地平线以上会留一条硬边
        int top = horizon - fh;
        for (int y = 0; y < top; y++)
        {
            var row = far.GetPixel(0, 0);
            for (int x = 0; x < w; x++) img.SetPixel(x, y, row);
        }

        // 远景底部和地平线之间压一道渐变，避免出现一条硬边
        for (int i = 0; i < 10; i++)
        {
            float t = i / 9f;
            var c = img.GetPixel(w / 2, horizon - 10 + i);
            float k = 0.86f + 0.14f * t;
            var row = new Color(c.R * k, c.G * k, c.B * k, 1f);
            for (int x = 0; x < w; x++) img.SetPixel(x, horizon - 10 + i, row);
        }
    }

    /// <summary>
    /// 墓园背景里地平线上的那一排碑。
    ///
    /// 墓碑本体是从俯视素材 <c>地图/墓地.png</c> 里抠出来的真实像素
    /// （<c>assets/textures/stele_bg_01..04.png</c>，已转灰阶+抠成透明背景）。
    /// 整张地图不作为背景——视角/色彩/气氛/比例都对不上，只有碑能用。
    ///
    /// ★ 这里有两个静默到不报错的坑，都踩过：
    ///   1) <c>new Color(0xd8, 0xd8, 0xd0)</c> 的参数是 0–1 浮点，216 会钳成 1.0 → 全白。要用 <see cref="Rgb"/>。
    ///   2) <c>Color.A</c> 是 0–1 浮点。写 <c>c.A &lt; 128</c> 判断透明会<b>恒为真</b>，
    ///      结果每个像素都被跳过，一个碑都画不出来。判断透明一律用 <c>0.5f</c>。
    /// </summary>
    private static void DrawSteleRow(Image img, int w, int horizon)
    {
        var stones = new System.Collections.Generic.List<Image>();
        for (int i = 1; i <= 4; i++)
        {
            var path = $"res://assets/textures/stele_bg_{i:00}.png";
            if (!ResourceLoader.Exists(path)) continue;
            var im = ResourceLoader.Load<Texture2D>(path)?.GetImage();
            if (im == null) continue;
            im.Convert(Image.Format.Rgba8);
            stones.Add(im);
        }

        if (stones.Count == 0)
        {
            DrawProceduralSteles(img, w, horizon);
            return;
        }

        // 原生尺寸铺，不缩放——缩放要重采样，这 28×52 的小图不值得引入那点误差。
        // 每三块留一块当"远处"，压暗并拉开间距，做出纵深。
        int cursor = -12, idx = 0;
        while (cursor < w)
        {
            var st = stones[idx % stones.Count];
            bool far = idx % 3 == 0;
            int sw = st.GetWidth(), sh = st.GetHeight();
            int baseY = horizon - sh - (far ? 5 : 0);
            float k = far ? 0.72f : 1f;

            // 落地阴影：没有它碑会浮在地平线上，看起来像贴图而不是站在土里
            for (int px = 0; px < sw; px++)
            {
                int tx = cursor + px;
                if (tx < 0 || tx >= w) continue;
                var below = st.GetPixel(px, sh - 1);
                if (below.A < 0.5f) continue;      // 只在碑正下方才有影
                float spread = far ? 0.82f : 0.92f;
                img.SetPixel(tx, baseY + sh, new Color(0.24f, 0.23f, 0.18f, 1f));
                if ((px * 100 / sw) % 100 < spread * 100)
                    img.SetPixel(tx, baseY + sh + (far ? 0 : 1), new Color(0.30f, 0.29f, 0.23f, 1f));
            }

            for (int py = 0; py < sh; py++)
            {
                int ty = baseY + py;
                if (ty < 0 || ty >= img.GetHeight()) continue;
                for (int px = 0; px < sw; px++)
                {
                    int tx = cursor + px;
                    if (tx < 0 || tx >= w) continue;
                    var c = st.GetPixel(px, py);
                    if (c.A < 0.5f) continue;
                    img.SetPixel(tx, ty, new Color(c.R * k, c.G * k, c.B * k, 1f));
                }
            }

            cursor += far ? sw + 22 : sw + 6;
            idx++;
        }
    }

    /// <summary>抠像素材缺失时的兜底：两排程序剪影，后排矮而淡、前排高而实。</summary>
    private static void DrawProceduralSteles(Image img, int w, int horizon)
    {
        var rng = new Random(20060517);
        var stone = Rgb(106, 106, 104);
        for (int pass = 0; pass < 2; pass++)
        {
            int y = horizon - (pass == 0 ? 4 : 0);
            int thMin = pass == 0 ? 20 : 30;
            int thMax = pass == 0 ? 34 : 54;
            float k = pass == 0 ? 0.62f : 1f;
            for (int i = 0; i < (pass == 0 ? 26 : 18); i++)
            {
                int tx = rng.Next(-8, w);
                int tw = rng.Next(8, 18);
                int th = rng.Next(thMin, thMax);
                for (int py = y - th; py < y; py++)
                for (int px = tx; px < tx + tw && px < w; px++)
                {
                    if (px < 0 || py < 0 || py >= img.GetHeight()) continue;
                    // 顶端收窄，让它像个碑而不是块砖
                    float t = (float)(y - py) / th;
                    float half = tw * 0.5f * (1f - 0.35f * t * t);
                    if (Math.Abs(px - (tx + tw * 0.5f)) > half) continue;
                    img.SetPixel(px, py, new Color(stone.R * k, stone.G * k, stone.B * k, 1f));
                }
            }
        }
    }

    /// <summary>纯黑。</summary>
    public static Image Black(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 1));
        return img;
    }
}