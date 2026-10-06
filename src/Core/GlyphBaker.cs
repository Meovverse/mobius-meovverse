using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 字形烘焙器：把文字画到一张离屏纹理上，读回像素。
///
/// ★ 为什么不用 TextServer 的字形缓存 API：
///   `FontFile.GetGlyphOffset()` 给的是**相对基线的偏移**，不是字形在缓存图集里的左上角。
///   照字面用会从图集的随机位置取像素，拿回来的是一条杂散色带而不是字。
///   （这个问题在开发时真的卡了很久，是靠 ASCII dump 才看出来的。）
///
///   所以这里走**唯一没有歧义的路径**：SubViewport + `Font.DrawString`，
///   也就是引擎平时画文字的那条路，然后 `GetTexture().GetImage()` 把像素读回来。
///   代价是要等一两帧，但整块碑面只烘一次。
///
/// ★ 它是纯程序生成的，不依赖任何美术资产——所以判定数据永远和"我们以为的碑面"一致。
/// </summary>
public partial class GlyphBaker : Node
{
    /// <summary>一个要烘的字：字形、落位框、批次、刻痕深度。</summary>
    public readonly record struct Slot(char Ch, Rect2 Box, Batch Batch, int Depth);

    /// <summary>
    /// ★ 融合像素是 **12px 位图字体**——任何"直接画 96px"都是在放大 8 倍糊图，
    ///   这就是"中文字体过于粗糙"的全部原因。正确做法：整张 mask 用 1/8 画布
    ///   （96px 字槽 → 12px 原生的位图字号）绘制，再 **最近邻 ×8 放大**。
    ///   笔画全部落在 8px 网格上，判定几何不变，像素反而更干净。
    /// </summary>
    /// <summary>
    /// 位图字体时代=8（见上面的历史注释）。现在换 Noto Sans SC（矢量），
    /// 96px 直接渲染天然锐利，缩放管道留而不发（改回 1）。
    /// 万一又换回位图字体，把它调回 8 即可。
    /// </summary>
    public const int PixelScale = 1;

    private SubViewport? _vp;
    private Control? _canvas;

    /// <summary>
    /// 把一组字槽烘成一张 Height/Batch 场。
    ///
    /// ★ 必须 async：SubViewport 的内容是在**下一帧**才渲染出来的。
    ///   创建完立刻 GetTexture().GetImage() 拿到的是一张空图。
    ///   这个坑的表现是"刻痕像素 0"，很容易误判成字体没加载。
    /// </summary>
    public static async Task<SurfaceModel?> BakeSteleAsync(
        Node host, int w, int h, Font font, IReadOnlyList<Slot> slots)
    {
        var baker = new GlyphBaker();
        host.AddChild(baker);
        var surface = await baker.RunAsync(w, h, font, slots);
        baker.QueueFree();
        return surface;
    }

    private async Task<SurfaceModel?> RunAsync(int w, int h, Font font, IReadOnlyList<Slot> slots)
    {
        // 离屏视口
        _vp = new SubViewport
        {
            // ★ 保持全尺寸 viewport（小尺寸在 headless 下读不回纹理）。
            //   1/8 的含义体现在"只画左上角那块 + 取回后放大"。
            Size = new Vector2I(w, h),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
        };
        AddChild(_vp);

        _canvas = new Control { Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        _vp.AddChild(_canvas);

        var surface = new SurfaceModel(w, h);
        _canvas.Draw += () => Paint(font, slots);
        _canvas.QueueRedraw();

        // ★ 等 SubViewport 真的渲染出来。它比 canvas item 晚一帧，
        //   这里连等三帧再读，少一帧就是全空。
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var img = _vp.GetTexture()?.GetImage();
        if (img == null)
        {
            GD.PushError("GlyphBaker：离屏纹理读不回来");
            Cleanup();
            return null;
        }
        img.Convert(Image.Format.Rgba8);
        // 取左上 w/8 × h/8 的"原生像素区"，最近邻 ×8 放大回全尺寸
        var small = img.GetRegion(new Rect2I(0, 0, w / PixelScale, h / PixelScale));
        small.Resize(w, h, Image.Interpolation.Nearest);
        img = small;

        Threshold(img, surface, slots);
        Cleanup();
        return surface;
    }

    private void Paint(Font font, IReadOnlyList<Slot> slots)
    {
        foreach (var s in slots)
        {
            if (s.Ch == ' ' || s.Ch == '　') continue;
            int size = Mathf.Max(6, Mathf.RoundToInt(s.Box.Size.Y / (float)PixelScale));

            // CJK 字是全角的：advance == fontSize。
            // 所以把**基线**放在 box 底部往上 = fontSize - descent，
            // 字形的 em 框就正好落在 box 里，横向从 box 左边缘开始。
            float descent = font.GetDescent(size);
            var baseline = new Vector2(s.Box.Position.X / PixelScale,
                                       s.Box.End.Y / (float)PixelScale - descent);

            _canvas!.DrawString(font, baseline, s.Ch.ToString(),
                                HorizontalAlignment.Left, -1, size,
                                Colors.White);
        }
    }

    private static void Threshold(Image img, SurfaceModel sm, IReadOnlyList<Slot> slots)
    {
        // 先做一次"哪些像素有笔画"的判定
        bool[,] raw = new bool[sm.W, sm.H];
        bool[,] on = new bool[sm.W, sm.H];
        for (int y = 0; y < sm.H && y < img.GetHeight(); y++)
        for (int x = 0; x < sm.W && x < img.GetWidth(); x++)
        {
            var px = img.GetPixel(x, y);
            raw[x, y] = px.A > 0.45f;     // 矢量字体的 AA 边缘：0.45 收边
        }

        // Noto 96px 的笔画对"刻痕"偏瘦：阈值后做一次 3×3 膨胀加粗 1px
        //（比 DrawString 的 outline 参数好传，也和像素时代的 8px 块判定兼容）
        for (int y = 0; y < sm.H; y++)
        for (int x = 0; x < sm.W; x++)
        {
            if (raw[x, y]) { on[x, y] = true; continue; }
            for (int dy = -1; dy <= 1 && !on[x, y]; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < sm.W && ny < sm.H && raw[nx, ny]) { on[x, y] = true; break; }
            }
        }

        // 再按每个字槽写入 Height / Batch
        foreach (var s in slots)
        {
            if (s.Ch == ' ' || s.Ch == '　') continue;
            int x0 = Mathf.FloorToInt(s.Box.Position.X);
            int y0 = Mathf.FloorToInt(s.Box.Position.Y);
            int x1 = Mathf.CeilToInt(s.Box.End.X);
            int y1 = Mathf.CeilToInt(s.Box.End.Y);

            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (!sm.InBounds(x, y) || !on[x, y]) continue;
                int i = sm.Index(x, y);
                if (sm.Height[i] < s.Depth)
                    sm.Height[i] = (byte)s.Depth;
                sm.BatchMap[i] = (byte)(int)s.Batch;
            }
        }
    }

    private void Cleanup()
    {
        if (_vp != null && IsInstanceValid(_vp))
            _vp.QueueFree();
        _vp = null;
        _canvas = null;
    }
}