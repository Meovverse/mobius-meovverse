using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 碑面「A 区 7 号」的表面构建器。
///
/// ★★ 这个类**不读任何美术图**。字形遮罩由字体在运行时渲染，
/// 所以判定区域、坐标、批次划分永远和"我们以为的碑面"一致。
///
/// 美术的 <c>mask_stone_glyphs.png</c> 只是好看用的；即使美术交来一张
/// 构图不一样的图，游戏照样能玩、判定照样正确。这是为了对付
/// "美术资产可能不按规格交"这件事。
/// </summary>
public static class SteleBuilder
{
    public const int W = 640;
    public const int H = 360;

    // ★ 碑面内容以 doc/剧本.md（S6/S7/S9）为准，不是 doc/剧情.md。
    //   2026 版剧本里**名字没被改过**——碑上刻的就是真名苏兰。
    //   苏航只改了日期：把「17」磨掉，改刻成「16」。

    /// <summary>姓名（第一行，字最大）。</summary>
    public const string FaceName = "苏兰";

    /// <summary>生年（第二行）。</summary>
    public const string FaceBorn = "一九五八年生";

    /// <summary>卒日期（第三行）。★ 这里是「16」——被磨改之后的那个。</summary>
    public const string FaceDate = "2006.05.16";

    /// <summary>底座垫石上刻着的那两行（终章扒出来的那一面，真话）。</summary>
    public const string UnderDate = "材料日期 2006-05-17 / 刻字日期 2006-05-17";

    /// <summary>登记本 / 墓园系统的内容。真话。</summary>
    public const string RegistryName = "苏兰";
    public const string RegistryDate = "2006-05-17";

    /// <summary>名字所占的横向范围（比例）。</summary>
    private const float NameStartFrac = 0.06f;
    private const float NameEndFrac = 0.40f;

    /// <summary>「六」在正文行里的横向位置（由布局算出，见 <see cref="Layout"/>）。</summary>
    public sealed class Layout
    {
        /// <summary>字高（像素）。</summary>
        public int GlyphSize { get; init; } = 96;

        public int NameTop { get; set; }
        public int BodyTop { get; set; }
        public int DateTop { get; set; }

        /// <summary>正文字距（像素）。剧情要求"故意松，像碑"。</summary>
        public const int BodyGap = 14;

        /// <summary>★ 剧情：改碑的人为了把「十」塞进去，把它往左挪了两毫米 → 2px。</summary>
        public const int ShouNudge = 2;

        /// <summary>一个字槽。★ 必须按槽位存而不是按字存：</summary>
        public sealed record GlyphSlot(char Ch, Rect2 Box, int Line, int IndexInLine);

        /// <summary>碑上所有字槽，按行顺序。</summary>
        public List<GlyphSlot> Slots { get; } = new();

        /// <summary>取某个字在指定行的位置；行号 -1 = 最后一次出现的位置。</summary>
        public Rect2 Find(char ch, int line = -1)
        {
            for (int i = Slots.Count - 1; i >= 0; i--)
                if (Slots[i].Ch == ch && (line < 0 || Slots[i].Line == line))
                    return Slots[i].Box;
            return default;
        }

        public bool Has(char ch, int line)
        {
            foreach (var s in Slots)
                if (s.Ch == ch && s.Line == line) return true;
            return false;
        }
    }

    public static Layout? LastLayout { get; private set; }

    /// <summary>
    /// 构建碑面。返回的 SurfaceModel 上带着 4 个 CarveMark。
    ///
    /// 全程不读任何美术图：字形由 <see cref="GlyphBaker"/> 现场烘焙，
    /// 批次划分由下面的 slot 表决定。
    /// </summary>
    public static async Task<SurfaceModel> BuildAsync(Font font, Node host)
    {
        var layout = ComputeLayout(font);
        LastLayout = layout;

        // ★ 先试已烘好的遮罩（data/gen/stele_glyphs.png，程序生成，不是美术资产）。
        //   没有才在运行时烘。两条路都失败才退化成纯石面。
        //   这样 headless 诊断和正式游戏走的是同一份数据。
        var sm = LoadBaked();
        if (sm == null)
        {
            sm = await GlyphBaker.BakeSteleAsync(host, W, H, font, BakeSlots(layout));
        }
        if (sm == null)
        {
            GD.PushWarning("字形遮罩不可用，退回纯石面（判定会过不去，但游戏不崩）");
            sm = new SurfaceModel(W, H);
        }

        // ★ 批次划分在 BuildMarks 里做（它同时要知道判定区），这里不再重复处理
        FillDust(sm, layout);

        BuildMarks(sm, layout);
        sm.Touch();
        return sm;
    }

    public const int LineName = 0, LineBorn = 1, LineDate = 2;

    /// <summary>
    /// 需要烘进遮罩的全部字槽。
    /// ★ 和美术需求无关：判定数据全部在这里算出来，美术的图只负责好看。
    /// </summary>
    public static List<GlyphBaker.Slot> BakeSlots(Layout l)
    {
        var slots = new List<GlyphBaker.Slot>();
        foreach (var s in l.Slots)
        {
            int depth = s.Line == LineName ? 210 : 170;
            slots.Add(new GlyphBaker.Slot(s.Ch, s.Box, Batch.Luyun, depth));
        }

        // ★ 「6」底下那个「7」——被磨掉又重刻的第一处痕迹。
        //   完全重合是不行的：那样玩家什么都看不见，这一关就变成猜谜了。
        //   所以「7」正位偏下 6px，从「6」底下露出下半截。
        var liu = l.Find('6', LineDate);
        if (liu.Size.X > 0)
        {
            var sevenBox = new Rect2(
                liu.Position.X + liu.Size.X * 0.10f,
                liu.Position.Y + liu.Size.Y * 0.30f,
                liu.Size.X * 0.78f,
                liu.Size.Y * 0.86f);
            slots.Add(new GlyphBaker.Slot('7', sevenBox, Batch.Ground, 120));
            LastSevenMark = new Rect2(
                sevenBox.Position.X,
                sevenBox.Position.Y + sevenBox.Size.Y * 0.42f,
                sevenBox.Size.X,
                sevenBox.Size.Y * 0.58f);
        }

        return slots;
    }

    /// <summary>「7」露出在外面的那一截（判定区）。</summary>
    public static Rect2 LastSevenMark { get; private set; }

    /// <summary>已烘好的遮罩路径。存在就直接读，不做运行时烘焙。</summary>
    public const string BakedPath = "res://data/gen/stele_glyphs.png";


    private static Font? LoadThemeFont()
    {
        var p = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        if (!string.IsNullOrEmpty(p) && ResourceLoader.Exists(p))
            return ResourceLoader.Load<Font>(p);
        return ThemeDB.FallbackFont;
    }

    /// <summary>
    /// 用 TextServer 把一个字渲染成位图。
    /// Godot 4 的 C# API 里没有 Font.GetCharTexture，正解是 TextServer.FontRenderGlyph。
    /// 渲染不出来（字库缺字）就返回 null，调用方走方块兜底。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<int, int> _cacheIndexForSize = new();

    /// <summary>
    /// 找到（或建立）某个字号在 FontFile 字号缓存里的下标。
    ///
    /// ★ 不查文档一定会踩的坑：
    ///   FontFile 的 GetGlyphIndex / GetGlyphSize / GetGlyphTextureIdx / GetTextureImage
    ///   的第一个参数是**字号缓存数组的下标**，不是字号。
    ///   下标 0 对动态字体（TFF）是空的，直接用会报 `p_size.x &lt;= 0`。
    ///   必须先调 TextServer.FontRenderRange 把目标字号烘进缓存，之后下标 1..N 才可用。
    ///   这里把找到的下标缓存起来。
    /// </summary>
    /// <summary>批次 ID 图（R 通道）。程序推导，美术不用做。</summary>
    /// <summary>
    /// 把 SurfaceModel 画成一帧 RGB 底图（石面本体 + 补刻区的"新石"微差）。
    /// 尘埃/堆屑/压痕由场景层的渲染器叠加——底图是静态的，只算一次。
    /// </summary>
    public static byte[] RenderBase(SurfaceModel sm)
    {
        var stone = ProcGen.SteleFace(W, H);
        var buf = new byte[W * H * 3];
        for (int y = 0, i = 0; y < H; y++)
        for (int x = 0; x < W; x++, i++)
        {
            var c = stone.GetPixel(x, y);
            // 补刻的那一格，石头是新的：底色本身就偏亮一档（这是 clue_01 的视觉真相）
            float k = sm.BatchMap[i] == (byte)Batch.Repair ? 1.10f : 1f;
            buf[i * 3] = (byte)(c.R * 255 * k);
            buf[i * 3 + 1] = (byte)(c.G * 255 * k);
            buf[i * 3 + 2] = (byte)(c.B * 255 * k);
        }
        return buf;
    }

    public static Image BuildBatchMap()
    {
        var sm = LoadBaked();
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        if (sm != null)
            for (int i = 0; i < sm.BatchMap.Length; i++)
                img.SetPixel(i % W, i / W, new Color(sm.BatchMap[i] / 255f, 0, 0, 1));
        else
            img.Fill(new Color(0, 0, 0, 1));
        return img;
    }

    private static int EnsureCacheIndex(FontFile ff, int pxSize)
    {
        if (_cacheIndexForSize.TryGetValue(pxSize, out int cached))
            return cached;

        var ts = TextServerManager.GetPrimaryInterface();
        var vs = new Vector2I(pxSize, 0);

        try { ts.FontRenderRange(ff.GetRids()[0], vs, 0, 0x2FFF); }
        catch { return -1; }

        for (int i = 1; i < 8; i++)
        {
            // ★ 必须用一个**真实的字**去探测。glyph 0 是 .notdef，尺寸恒为 0，
            // 用它探测会误判成"这个下标不可用"，导致永远烘不出字形。
            try
            {
                int g = ff.GetGlyphIndex(i, '梅', 0);
                if (g == 0) continue;
                var sz = ff.GetGlyphSize(i, vs, g);
                if (Mathf.RoundToInt(sz.X) > 0)
                {
                    _cacheIndexForSize[pxSize] = i;
                    return i;
                }
            }
            catch { /* 这个下标不可用，试下一个 */ }
        }
        return -1;
    }

    /// <summary>
    /// 用 TextServer 把一个字渲染成位图。
    /// C# 绑定里 FontRenderGlyph 返回 **void**——它只把字形烘进缓存纹理，
    /// 像素要自己从缓存纹理里读回来。渲染不出来就返回 null，调用方走方块兜底。
    /// </summary>
    public static Image? RenderGlyph(Font font, char c, int pxSize)
    {
        if (font is not FontFile ff) return null;

        int ci = EnsureCacheIndex(ff, pxSize);
        if (ci < 0) return null;

        int glyph;
        try { glyph = ff.GetGlyphIndex(ci, c, 0); }
        catch { return null; }
        if (glyph == 0) return null;

        var vs = new Vector2I(pxSize, 0);

        int texIdx;
        Vector2 gsize, goff;
        try
        {
            texIdx = ff.GetGlyphTextureIdx(ci, vs, glyph);
            gsize = ff.GetGlyphSize(ci, vs, glyph);
            goff = ff.GetGlyphOffset(ci, vs, glyph);
        }
        catch { return null; }

        int gw = Mathf.RoundToInt(gsize.X), gh = Mathf.RoundToInt(gsize.Y);
        if (texIdx < 0 || gw <= 0 || gh <= 0) return null;

        Image cache;
        try { cache = ff.GetTextureImage(ci, vs, texIdx); }
        catch { return null; }
        if (cache == null) return null;
        cache.Convert(Image.Format.Rgba8);

        int rx = Mathf.RoundToInt(goff.X), ry = Mathf.RoundToInt(goff.Y);
        var outImg = Image.CreateEmpty(gw, gh, false, Image.Format.Rgba8);
        for (int y = 0; y < gh; y++)
        for (int x = 0; x < gw; x++)
        {
            int cx = rx + x, cy = ry + y;
            if (cx < 0 || cy < 0 || cx >= cache.GetWidth() || cy >= cache.GetHeight())
                continue;
            outImg.SetPixel(x, y, cache.GetPixel(cx, cy));
        }
        return outImg;
    }

    /// <summary>只要布局不要表面（烘焙工具用）。</summary>
    public static Layout BuildLayoutOnly()
    {
        LastLayout = ComputeLayout(LoadThemeFont());
        return LastLayout;
    }

    private static Layout ComputeLayout(Font font)
    {
        // 三行：姓名（大）· 生年 · 卒日期
        // 姓名那一行最大，因为第一章要找的就是「兰」的最后一笔。
        var l = new Layout();

        const int NAME = 104, SMALL = 30;
        const int X = 52;

        l.NameTop = 40;
        foreach (var (ch, i) in FaceName.Select((c, i) => (c, i)))
            l.Slots.Add(new Layout.GlyphSlot(ch,
                new Rect2(X + i * (NAME + 10), l.NameTop, NAME, NAME), LineName, i));

        l.BodyTop = 200;
        int x = X;
        foreach (char c in FaceBorn)
        {
            l.Slots.Add(new Layout.GlyphSlot(c, new Rect2(x, l.BodyTop, SMALL, SMALL), LineBorn, 0));
            x += SMALL + 8;
        }

        // 日期行：数字和点各占固定格，这样「6」的位置是确定的，判定区不会飘
        l.DateTop = 262;
        x = X;
        int idx = 0;
        foreach (char c in FaceDate)
        {
            int w = c is '.' ? 12 : SMALL;
            l.Slots.Add(new Layout.GlyphSlot(c, new Rect2(x, l.DateTop, w, SMALL), LineDate, idx++));
            x += w + 8;
        }

        return l;
    }

    /// <summary>
    /// 读已烘好的遮罩（<c>data/gen/stele_glyphs.png</c>，R=Height，G=Batch）。
    /// ★ 这张图是**程序生成的**，不是美术资产，所以它不受"美术交不交图"的影响。
    ///   万一文件丢了，BuildAsync 会退回运行时烘焙，游戏照样能跑。
    /// </summary>
    private static SurfaceModel? LoadBaked()
    {
        if (!ResourceLoader.Exists(BakedPath)) return null;
        var tex = ResourceLoader.Load<Texture2D>(BakedPath);
        var img = tex?.GetImage();
        if (img == null) return null;
        img.Convert(Image.Format.Rgba8);

        if (img.GetWidth() != W || img.GetHeight() != H)
        {
            GD.PushWarning($"烘好的遮罩尺寸不对（{img.GetWidth()}×{img.GetHeight()}），忽略");
            return null;
        }

        var sm = new SurfaceModel(W, H);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var px = img.GetPixel(x, y);
            int i = sm.Index(x, y);
            sm.Height[i] = (byte)Math.Clamp(px.R * 255f, 0, 255);
            sm.BatchMap[i] = (byte)Math.Clamp(px.G * 255f, 0, 255);
        }
        return sm;
    }

    // ── 后处理：批次划分 ────────────────────────────────────────────────
    //
    // 上面 GlyphBaker 只负责"哪些像素有笔画"。这里负责"这一笔是哪一年的"——
    // 也就是玩法真正的判据。全部从几何推出来，不看美术图。

    /// <summary>「每」的最下面那一长横 = 补刻（Batch.Repair）。全篇要找的那一笔。</summary>
    /// <summary>把某个字最下面那一长横标成"补刻"（Batch.Repair）。</summary>
    private static void MarkRepairBar(SurfaceModel sm, Rect2 box)
    {
        if (box.Size.X <= 0) return;
        var bar = FindBottomHorizontal(sm, box, 0.68f, 1.0f);
        SetBatchIn(sm, bar, Batch.Repair, 240);
    }

    private static void SetBatchIn(SurfaceModel sm, Rect2 box, Batch batch, int depth)
    {
        ForEachIn(sm, box, (x, y, i) =>
        {
            if (sm.Height[i] <= 0) return;
            sm.BatchMap[i] = (byte)batch;
            sm.Height[i] = (byte)Math.Max((int)sm.Height[i], depth);
        });
    }

    private static void ForEachIn(SurfaceModel sm, Rect2 box, Action<int, int, int> fn)
    {
        int x0 = Mathf.RoundToInt(box.Position.X), x1 = Mathf.RoundToInt(box.End.X);
        int y0 = Mathf.RoundToInt(box.Position.Y), y1 = Mathf.RoundToInt(box.End.Y);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            if (!sm.InBounds(x, y)) continue;
            fn(x, y, sm.Index(x, y));
        }
    }

    /// <summary>石粉覆盖。刻痕里更厚；名字那一块额外加厚（"有人抹过粉想盖住"）。</summary>
    private static void FillDust(SurfaceModel sm, Layout l)
    {
        var nameBox = new Rect2(l.Find('梅', LineName).Position,
                                new Vector2(l.Find('梅', LineName).Size.X * 2.2f,
                                            l.Find('梅', LineName).Size.Y));

        for (int y = 0; y < sm.H; y++)
        for (int x = 0; x < sm.W; x++)
        {
            int i = sm.Index(x, y);
            float n = ProcGen.FractalAccessor(x, y, 32, 3, 2);
            int d = Math.Clamp((int)(150 + n * 80), 0, 255);
            if (sm.Height[i] > 0) d = Math.Min(255, d + 40);
            if (nameBox.HasPoint(new Vector2(x, y))) d = Math.Min(255, d + 50);
            sm.Dust[i] = (byte)d;
        }
    }

    /// <summary>
    /// 在一个字的框里找"最长的水平笔画"。用来定位「每」下面那一横——
    /// 不用手工标坐标，字形换了也照样找得到。
    /// </summary>
    private static Rect2 FindBottomHorizontal(SurfaceModel sm, Rect2 box, float yFromFrac, float yToFrac)
    {
        int x0 = Mathf.RoundToInt(box.Position.X), x1 = Mathf.RoundToInt(box.End.X);
        int y0 = Mathf.RoundToInt(box.Position.Y + box.Size.Y * yFromFrac);
        int y1 = Mathf.RoundToInt(box.Position.Y + box.Size.Y * yToFrac);

        int bestY = -1, bestLen = 0;
        for (int y = y0; y < y1 && y < sm.H; y++)
        {
            int run = 0, maxRun = 0, runStart = -1, bestStart = -1;
            for (int x = x0; x < x1 && x < sm.W; x++)
            {
                if (sm.Height[sm.Index(x, y)] > 40)
                {
                    if (run == 0) runStart = x;
                    run++;
                    if (run > maxRun) { maxRun = run; bestStart = runStart; }
                }
                else run = 0;
            }
            if (maxRun > bestLen) { bestLen = maxRun; bestY = y; }
        }

        if (bestY < 0 || bestLen < 8)
            return new Rect2(box.Position.X, box.End.Y - 12, box.Size.X * 0.7f, 8);

        return new Rect2(x0, bestY - 3, x1 - x0, 9);
    }

    // ── 判定笔画 ────────────────────────────────────────────────────────

    /// <summary>
    /// 判定笔画。2026 版剧本里**名字没有被改过**——碑上刻的就是真名苏兰，
    /// 苏航只磨改了日期。所以只剩两处痕迹，不是四处。
    /// </summary>
    private static void BuildMarks(SurfaceModel sm, Layout l)
    {
        // ① 「兰」的最下面那一长横 —— 被磨掉又重刻，Batch = Repair
        var lan = l.Find('兰', LineName);
        if (lan.Size.X > 0)
        {
            MarkRepairBar(sm, lan);                      // 先标批次
            var bar = FindBottomHorizontal(sm, lan, 0.68f, 1.0f);
            sm.Marks.Add(new CarveMark
            {
                Id = "lan_last_stroke",
                Bounds = bar,
                Tolerance = 6f,
                Start = new Vector2(bar.Position.X + 2, bar.GetCenter().Y),
                TotalPixels = AreaOf(bar),
            });
        }

        // ② 「6」底下那个「7」——玩家擦开石粉后描出来
        var liu = l.Find('6', LineDate);
        if (liu.Size.X > 0)
        {
            var seven = LastSevenMark.Size.X > 0 ? LastSevenMark : liu;
            sm.Marks.Add(new CarveMark
            {
                Id = "sixteen_under_seventeen",
                Bounds = seven,
                Tolerance = 5f,
                Start = new Vector2(seven.GetCenter().X, seven.Position.Y + 2),
                TotalPixels = AreaOf(seven),
            });
        }
    }

    private static int AreaOf(Rect2 r) =>
        Math.Max(1, Mathf.RoundToInt(r.Size.X) * Mathf.RoundToInt(r.Size.Y));

    // ── 清碑（BE5 的入口，不可逆）──────────────────────────────────────

    /// <summary>
    /// 把碑磨掉。30 秒不可逆演出，每次调用磨掉一层。
    /// 磨完 Height 归零、Batch 归零 —— 证据没了，而且没有任何提示告诉玩家他失去了什么。
    /// </summary>
    public static int Grind(SurfaceModel sm, float dt)
    {
        int touched = 0;
        int band = Mathf.RoundToInt(26 * dt);      // 每秒推进约 26 行
        if (band <= 0) band = 1;

        int maxY = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
            if (sm.Height[sm.Index(x, y)] > 0) { maxY = y; break; }
        if (maxY == 0) return 0;

        int from = Mathf.Max(0, maxY - band);
        for (int y = from; y <= maxY && y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = sm.Index(x, y);
            if (sm.Height[i] == 0 && sm.Dust[i] == 0) continue;
            sm.Height[i] = 0;
            sm.BatchMap[i] = 0;
            sm.Dust[i] = (byte)Math.Min(255, sm.Dust[i] + 12);   // 石粉飞扬
            touched++;
        }
        sm.Touch();
        return touched;
    }

    /// <summary>碑是否已经磨平。</summary>
    public static bool IsGroundAway(SurfaceModel sm)
    {
        for (int i = 0; i < sm.Height.Length; i++)
            if (sm.Height[i] > 0) return false;
        return true;
    }
}