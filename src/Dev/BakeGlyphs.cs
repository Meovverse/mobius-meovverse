using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Dev;

/// <summary>
/// 字形烘焙工具（一次性，跑一次生成 PNG）。
///
/// ★ 为什么需要它：Godot 的 <c>--headless</c> **没有渲染管线**，
/// SubViewport + DrawString 在 headless 下画不出任何东西。
/// 而这个项目的字形遮罩必须在游戏启动时就绪（判定全靠它），
/// 不能等窗口起来再烘——那会让第一章的头两秒卡住。
///
/// 所以：跑一次这个工具（**需要窗口，不能 headless**），
/// 把结果写成 <c>res://data/gen/stele_glyphs.png</c>，提交进仓库。
/// 游戏启动时直接读这张图。
///
/// ★ 它仍然是**程序生成**的，不是美术资产：
///   美术来不来、规格对不对，都不影响这张图的存在和正确性。
///   它只是"字体的轮廓"，任何人拿同样的字体跑同样的命令都会得到同样的结果。
///
/// 用法：
///   Godot --path &lt;proj&gt; res://scenes/BakeGlyphs.tscn
///   （不要加 --headless）
/// </summary>
public partial class BakeGlyphs : Control
{
    private const string OutDir = "res://data/gen/";
    private const string OutPath = OutDir + "stele_glyphs.png";

    public override async void _Ready()
    {
        GD.Print("════ 字形烘焙 ════");

        var font = LoadFont();
        if (font == null)
        {
            GD.PushError("字体没加载，烘不了");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"字体：{font.GetType().Name}");

        var layout = SteleBuilder.LastLayout ?? SteleBuilder.BuildLayoutOnly();

        // 收集要烘的字（含「七」和「湘」这两个不在正常行里的字）
        var slots = new System.Collections.Generic.List<GlyphBaker.Slot>();
        foreach (var s in SteleBuilder.BakeSlots(layout))
            slots.Add(s);

        var sm = await GlyphBaker.BakeSteleAsync(
            this, SteleBuilder.W, SteleBuilder.H, font, slots);
        if (sm == null)
        {
            GD.PushError("烘焙失败");
            GetTree().Quit(1);
            return;
        }

        int carved = 0;
        var byBatch = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < sm.Height.Length; i++)
        {
            if (sm.Height[i] <= 0) continue;
            carved++;
            int b = sm.BatchMap[i];
            byBatch[b] = byBatch.TryGetValue(b, out int v) ? v + 1 : 1;
        }

        GD.Print($"有刻痕像素：{carved}");
        foreach (var (b, n) in byBatch)
            GD.Print($"  Batch {b} ({(Batch)b})：{n} px");

        if (carved < 1500)
        {
            GD.PushError($"★刻痕像素只有 {carved}，明显没烘出来");
            GetTree().Quit(1);
            return;
        }

        // 存图：R=Height  G=Batch  B=备用
        var img = Image.CreateEmpty(sm.W, sm.H, false, Image.Format.Rgba8);
        for (int y = 0; y < sm.H; y++)
        for (int x = 0; x < sm.W; x++)
        {
            int i = sm.Index(x, y);
            img.SetPixel(x, y, new Color(sm.Height[i] / 255f, sm.BatchMap[i] / 255f, 0, 1));
        }
        img.SavePng(OutPath);
        GD.Print($"已写入 {OutPath}（{sm.W}×{sm.H}）");
        GD.Print("════ 烘焙结束 ════");

        GetTree().Quit(0);
    }

    private Font? LoadFont()
    {
        var p = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        if (!string.IsNullOrEmpty(p) && ResourceLoader.Exists(p))
            return ResourceLoader.Load<Font>(p);
        return ThemeDB.FallbackFont;
    }
}