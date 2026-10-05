using System.Linq;
using Godot;
using MoShi.Core;

namespace MoShi.Dev;

/// <summary>
/// M1 验收用的诊断场景。
///
/// 它回答一个问题：**"靠质地分辨新旧"这件事在数据上成立吗？**
/// 具体是：同一字里的三笔旧刻 + 一笔补刻，它们的 Batch 分布是不是真的分得开、
/// 判定笔画是不是真的落在刻痕上。
///
/// 用法（headless）：
///   Godot --headless --path &lt;proj&gt; res://scenes/GlyphTest.tscn
///
/// 所有结论都打在 stdout 上，不依赖渲染。
/// </summary>
public partial class GlyphTest : Control
{
    /// <summary>命令行加 --dump-glyph 就把字形打成 ASCII，用来肉眼看。</summary>
    public static readonly bool dumpGlyph =
        System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--dump-glyph") >= 0;

    private static void DumpAscii(Image img, char c)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        GD.Print($"  --- '{c}' 的实际像素（每 2px 取 1 点）---");
        const string ramp = " .:-=+*#%@";
        int rows = 0;
        for (int y = 0; y < h && rows < 34; y += 2, rows++)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = 0; x < w; x++)
            {
                var px = img.GetPixel(System.Math.Min(x, w - 1), System.Math.Min(y, h - 1));
                float v = px.A < 0.5f ? px.R : px.A;
                int k = System.Math.Clamp((int)(v * (ramp.Length - 1)), 0, ramp.Length - 1);
                sb.Append(ramp[k]);
            }
            GD.Print("  |" + sb + "|");
        }
    }

    public override async void _Ready()
    {
        GD.Print("════════ 墓石 · M1 字形管线诊断 ════════");

        var font = LoadFont();
        if (font == null)
        {
            GD.PushError("字体没加载，后面的数字都没有意义");
            return;
        }
        GD.Print($"字体：{font.GetType().Name}");

        // ① 探测字号缓存下标
        ProbeCache(font);

        // ① 单字渲染能不能出像素
        ProbeGlyph(font, '梅', 108);
        ProbeGlyph(font, '每', 108);
        ProbeGlyph(font, '七', 44);
        ProbeGlyph(font, '十', 44);

        // ①c 烘好的遮罩读得到吗
        ReportBakedPng();

        // ② 整块碑面
        var sm = await SteleBuilder.BuildAsync(font, this);
        ReportSurface(sm);

        // ②b 把「梅」那块 Height 场打成 ASCII，肉眼看刻痕
        if (dumpGlyph)
        {
            var mei = SteleBuilder.LastLayout!.Find('梅', SteleBuilder.LineName);
            DumpHeight(sm, mei, "「梅」Height 场");
        }

        // ③ 判定笔画是否真的落在刻痕上 —— 这是最关键的一条
        ReportMarks(sm);

        // ④ 「擦过头」的物理后果
        await ReportOverWipeAsync(sm);

        GD.Print("════════ 诊断结束 ════════");
        GetTree().Quit(0);
    }

    private Font? LoadFont()
    {
        var p = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        if (!string.IsNullOrEmpty(p) && ResourceLoader.Exists(p))
            return ResourceLoader.Load<Font>(p);
        return ThemeDB.FallbackFont;
    }

    /// <summary>
    /// FontFile 的 cacheIndex 是"字号缓存数组下标"，不是字号。
    /// 先用 FontRenderRange 把目标字号烘进缓存，再逐个下标试，看哪个有效。
    /// </summary>
    private static void DumpHeight(SurfaceModel sm, Rect2 box, string title)
    {
        GD.Print($"  --- {title} {box} ---");
        const string ramp = " .:-=+*#%@";
        int x0 = Mathf.RoundToInt(box.Position.X), x1 = Mathf.RoundToInt(box.End.X);
        int y0 = Mathf.RoundToInt(box.Position.Y), y1 = Mathf.RoundToInt(box.End.Y);
        for (int y = y0; y < y1 && y < sm.H; y += 2)
        {
            var sb = new System.Text.StringBuilder();
            for (int x = x0; x < x1 && x < sm.W; x += 1)
            {
                int v = sm.Height[sm.Index(x, y)];
                sb.Append(ramp[System.Math.Clamp(v * (ramp.Length - 1) / 255, 0, ramp.Length - 1)]);
            }
            GD.Print($"  {y,3}|" + sb);
        }
    }

    private static void ProbeCache(Font font)
    {
        if (font is not FontFile ff) return;
        var ts = TextServerManager.GetPrimaryInterface();
        var vs = new Vector2I(108, 0);

        try { ts.FontRenderRange(ff.GetRids()[0], vs, 0, 0x2FFF); }
        catch (System.Exception e) { GD.Print($"  FontRenderRange 抛异常：{e.Message}"); }

        for (int i = 0; i < 6; i++)
        {
            int gi = -1, ti = -1;
            Vector2 gs = Vector2.Zero;
            try { gi = ff.GetGlyphIndex(i, '梅', 0); } catch { }
            try { ti = ff.GetGlyphTextureIdx(i, vs, gi); } catch { }
            try { gs = ff.GetGlyphSize(i, vs, gi); } catch { }
            GD.Print($"  cacheIndex {i}: glyphIndex={gi} texIdx={ti} size={gs}");
        }
    }

    private static void ProbeGlyph(Font font, char c, int size)
    {
        var img = SteleBuilder.RenderGlyph(font, c, size);
        if (img == null)
        {
            GD.Print($"  字形 '{c}' @ {size}px —— ★渲染失败，走方块兜底");
            return;
        }

        int on = 0;
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var px = img.GetPixel(x, y);
            float a = px.A < 0.5f ? px.R : px.A;
            if (a >= 0.4f) on++;
        }
        int total = img.GetWidth() * img.GetHeight();
        float cover = total > 0 ? on / (float)total : 0f;

        string verdict = cover is > 0.06f and < 0.60f ? "OK" : cover >= 0.60f ? "★疑似实心方块" : "★太稀/几乎空";
        GD.Print($"  字形 '{c}' @ {size}px —— {img.GetWidth()}×{img.GetHeight()}，覆盖 {cover:P0}  {verdict}");

        if (dumpGlyph)
        {
            float aMin = 9, aMax = -9, rMin = 9, rMax = -9;
            var hist = new int[8];
            for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var px = img.GetPixel(x, y);
                aMin = System.Math.Min(aMin, px.A); aMax = System.Math.Max(aMax, px.A);
                rMin = System.Math.Min(rMin, px.R); rMax = System.Math.Max(rMax, px.R);
                hist[System.Math.Clamp((int)(px.A * 7), 0, 7)]++;
            }
            GD.Print($"    通道统计 fmt={img.GetFormat()} A[{aMin:F2},{aMax:F2}] R[{rMin:F2},{rMax:F2}]");
            foreach (float th in new[] { 0.1f, 0.4f, 0.9f })
            {
                int n = 0, x0 = 9999, x1 = -1, y0 = 9999, y1 = -1;
                for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                {
                    var px = img.GetPixel(x, y);
                    float v = px.A < 0.5f ? px.R : px.A;
                    if (v < th) continue;
                    n++;
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
                GD.Print($"    阈值 {th:F1}: {n} px, 包围盒 ({x0},{y0})-({x1},{y1})");
            }
            GD.Print($"    A 分布：" + string.Join(" ", hist.Select(n => n.ToString().PadLeft(5))));
            DumpAscii(img, c);
        }
    }

    private static void ReportSurface(SurfaceModel sm)
    {
        int carved = 0;
        var byBatch = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < sm.Height.Length; i++)
        {
            if (sm.Height[i] <= 0) continue;
            carved++;
            int b = sm.BatchMap[i];
            byBatch[b] = byBatch.TryGetValue(b, out int v) ? v + 1 : 1;
        }

        GD.Print($"── 碑面 {sm.W}×{sm.H} ──");
        GD.Print($"  有刻痕的像素：{carved}（{carved / (float)(sm.W * sm.H):P1} 画面占比）");
        foreach (var (b, n) in byBatch)
            GD.Print($"    Batch {b} ({(Batch)b})：{n} px");
        if (carved == 0)
            GD.PushError("★一块刻痕都没有 —— 字形管线没通");
    }

    private static float MeanHeight(SurfaceModel sm, int cx, int cy, int r, int fallback)
    {
        long sum = 0; int n = 0;
        for (int y = cy - r; y <= cy + r; y++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            if (!sm.InBounds(x, y)) continue;
            sum += sm.Height[sm.Index(x, y)]; n++;
        }
        return n > 0 ? sum / (float)n : fallback;
    }

    private static void ReportBakedPng()
    {
        const string path = SteleBuilder.BakedPath;
        GD.Print($"── 烘好的遮罩 {path} ──");
        GD.Print($"  ResourceLoader.Exists = {ResourceLoader.Exists(path)}");
        var tex = ResourceLoader.Load<Texture2D>(path);
        GD.Print($"  Load<Texture2D> = {(tex == null ? "null" : tex.GetType().Name)}");
        var img = tex?.GetImage();
        if (img == null) { GD.Print("  ★GetImage() 返回 null"); return; }
        GD.Print($"  {img.GetWidth()}×{img.GetHeight()} fmt={img.GetFormat()}");

        int nonzero = 0;
        float maxR = 0;
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var px = img.GetPixel(x, y);
            if (px.R > 0.01f) nonzero++;
            maxR = System.Math.Max(maxR, px.R);
        }
        GD.Print($"  R>0 的像素：{nonzero}，R 最大值 {maxR:F3}");
        if (nonzero == 0) GD.Print("  ★图是空的 —— 保存时通道写错了");
    }

    private static void ReportMarks(SurfaceModel sm)
    {
        GD.Print("── 判定笔画 ──");
        foreach (var m in sm.Marks)
        {
            // 判定区里有多少像素真的有刻痕？没有的话这一关根本不可能过
            int inBounds = 0, carved = 0;
            for (int y = Mathf.RoundToInt(m.Bounds.Position.Y); y < Mathf.RoundToInt(m.Bounds.End.Y); y++)
            for (int x = Mathf.RoundToInt(m.Bounds.Position.X); x < Mathf.RoundToInt(m.Bounds.End.X); x++)
            {
                if (!sm.InBounds(x, y)) continue;
                inBounds++;
                if (sm.Height[sm.Index(x, y)] > 40) carved++;
            }
            float ratio = inBounds > 0 ? carved / (float)inBounds : 0f;
            string verdict = ratio > 0.06f ? "OK" : "★判定区里几乎没有刻痕，这一关过不去";
            GD.Print($"  {m.Id,-20} {m.Bounds}  容差 {m.Tolerance}  " +
                     $"刻痕占比 {ratio:P0}  {verdict}");
        }
    }

    private async System.Threading.Tasks.Task ReportOverWipeAsync(SurfaceModel sm)
    {
        GD.Print("── 擦过头的物理后果 ──");
        var copy = await SteleBuilder.BuildAsync(LoadFont()!, this);

        int cx = Mathf.RoundToInt(copy.Marks[0].Bounds.GetCenter().X);
        int cy = Mathf.RoundToInt(copy.Marks[0].Bounds.GetCenter().Y);

        int beforeCarved = 0, beforeCrushed = 0;
        for (int i = 0; i < copy.Height.Length; i++)
        {
            if (copy.Height[i] > 40) beforeCarved++;
            if (copy.Crushed.Contains(i)) beforeCrushed++;
        }
        float meanBefore = MeanHeight(copy, cx, cy, 8, 0);

        // 先把那一块的石粉全擦掉，再继续擦 200 下 —— 这就是"擦过头"
        for (int k = 0; k < 400; k++)
            copy.Erase(cx, cy, 6, 0.35f);

        float meanAfter = MeanHeight(copy, cx, cy, 8, 0);
        int afterCarved = 0, afterCrushed = 0;
        for (int i = 0; i < copy.Height.Length; i++)
        {
            if (copy.Height[i] > 40) afterCarved++;
            if (copy.Crushed.Contains(i)) afterCrushed++;
        }

        GD.Print($"  该处平均刻痕深度 {meanBefore:F1} → {meanAfter:F1}（对比度损失 {(1 - meanAfter / System.Math.Max(1f, meanBefore)):P0}）");
        GD.Print($"  刻痕像素 {beforeCarved} → {afterCarved}（丢了 {beforeCarved - afterCarved}）");
        GD.Print($"  被压坏的像素 {beforeCrushed} → {afterCrushed}");
        bool crushOk = afterCrushed > 30 && meanAfter < meanBefore * 0.95f;
        GD.Print(crushOk
            ? "  OK 碎屑确实压进了刻痕里，对比度永久下降且不可逆"
            : "  ★擦过头的代价没实现");
    }
}