using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>刻痕的"物理档案"。这一笔是哪一年刻的，只记录，不解释。</summary>
public enum Batch
{
    /// <summary>素石，没有刻痕。</summary>
    Blank = 0,

    /// <summary>2021-05-17 老吴手刻。</summary>
    Luyun = 1,

    /// <summary>补刻。用的是新錾子，手劲没控制好。</summary>
    Repair = 2,

    /// <summary>被磨除的旧痕（原来的「湘」）。</summary>
    Ground = 3,

    /// <summary>激光刻（新碑）。</summary>
    Laser = 4,
}

/// <summary>一个需要用「描」去完成的判定笔画。</summary>
public sealed class CarveMark
{
    public required string Id { get; init; }

    /// <summary>判定用的包围盒（像素）。</summary>
    public required Rect2 Bounds { get; init; }

    /// <summary>容差基准（像素）。描的时候偏离超过它就断。</summary>
    public float Tolerance { get; init; } = 4f;

    /// <summary>起点。描必须从这里开始。</summary>
    public Vector2 Start { get; init; }

    /// <summary>进度 0..1。</summary>
    public float Progress { get; private set; }

    public bool Complete => Progress >= SurfaceModel.TraceDoneAt;

    /// <summary>已经被"描"过的像素。逐像素记，这样进度是真实覆盖率。</summary>
    public HashSet<int> Covered { get; } = new();

    internal int TotalPixels { get; set; }

    internal void MarkCovered(int idx)
    {
        if (Covered.Add(idx))
            Progress = (float)Covered.Count / Math.Max(1, TotalPixels);
    }

    public void Reset()
    {
        Covered.Clear();
        Progress = 0f;
    }

    public override string ToString() => $"{Id} {Progress:P0}";
}

/// <summary>
/// 一张"表面"——碑面、纸面、底座，全都用这一个结构。
///
/// ★ 六个动词（看/擦/描/拓/对/量）全部读写同一份数据，所以它们的手感天然一致。
/// 三个动词之间的因果关系是这个数据模型自己长出来的，不是脚本写死的：
///
///   擦   → Dust 减少，Pile 增加
///   擦过头 → Pile 被压进 Height，该处对比度永久下降
///   描   → 只在 CarveMark 上记进度，不改画面
///   拓   → 把 Height 整层转印到纸，**因此改动和改动之前的痕迹会一起被带走**
///
/// ★★ **判定数据（CarveMark / Height 的形状）永远程序生成，绝不从美术图里读。**
/// 因为美术可能不按规格交图，而"玩家描得对不对"这件事不能依赖美术的构图。
/// 美术的图只负责好看，判定只认程序自己生成的那一份。
/// </summary>
public sealed class SurfaceModel
{
    /// <summary>一笔描到这个覆盖率就算完成。</summary>
    public const float TraceDoneAt = 0.55f;

    /// <summary>擦过头：Dust 低于这个值后继续擦，就开始压碎屑。</summary>
    public const byte CrushedThreshold = 12;

    public readonly int W;
    public readonly int H;

    /// <summary>刻痕深度。0 = 石面原高，255 = 最深。</summary>
    public readonly byte[] Height;

    /// <summary>覆盖物厚度：石粉 / 锈 / 灰 / 涂改液。</summary>
    public readonly byte[] Dust;

    /// <summary>被擦下来的碎屑堆积。</summary>
    public readonly byte[] Pile;

    /// <summary>每像素所属的批次（Batch 枚举）。</summary>
    public readonly byte[] BatchMap;

    public readonly List<CarveMark> Marks = new();

    /// <summary>任何改动都触发这个。视图据此上传纹理。</summary>
    public event Action? Changed;

    /// <summary>这一块是否已经被"擦过头"压过。用于结局和提示。</summary>
    public readonly HashSet<int> Crushed = new();

    public SurfaceModel(int w, int h)
    {
        W = w;
        H = h;
        Height = new byte[w * h];
        Dust = new byte[w * h];
        Pile = new byte[w * h];
        BatchMap = new byte[w * h];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    public int Index(int x, int y) => y * W + x;

    public void Touch() => Changed?.Invoke();

    // ── 擦 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 擦一下。返回被擦掉的量（用来触发"碎屑堆积"的音效/视觉）。
    ///
    /// 覆盖物厚度降到 0 之后**继续擦**，就会把碎屑压进刻痕里——
    /// 那一处 DetailLayer 的对比度永久下降，而且玩家不会得到任何提示。
    /// 这就是"擦过头"的全部实现：不是一个计数器，是一个物理后果。
    /// </summary>
    public int Erase(int cx, int cy, int radius, float amount)
    {
        int removed = 0;
        int r2 = radius * radius;
        for (int y = Math.Max(0, cy - radius); y <= Math.Min(H - 1, cy + radius); y++)
        for (int x = Math.Max(0, cx - radius); x <= Math.Min(W - 1, cx + radius); x++)
        {
            int dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > r2) continue;

            int i = Index(x, y);
            float falloff = 1f - (dx * dx + dy * dy) / (float)r2;

            if (Dust[i] > 0)
            {
                int d = Math.Min(Dust[i], (int)(amount * falloff * 255f));
                if (d <= 0) continue;
                Dust[i] = (byte)(Dust[i] - d);
                removed += d;

                // 擦下来的东西堆在笔画两侧（往低处偏）
                Pile[i] = (byte)Math.Min(255, Pile[i] + (int)(d * 0.55f));
            }
            else if (Pile[i] > 0)
            {
                // ★ 擦过头：把碎屑压进刻痕里。
                // 这一处的对比度永久下降，且不可逆。
                int press = Math.Min(Pile[i], (int)(amount * falloff * 255f));
                if (press <= 0) continue;
                Pile[i] = (byte)(Pile[i] - press);
                Height[i] = (byte)Math.Max(0, Height[i] - press / 2);
                Crushed.Add(i);
                removed += press / 2;
            }
        }

        if (removed > 0) Touch();
        return removed;
    }

    // ── 描 ──────────────────────────────────────────────────────────────

    /// <summary>找 cx,cy 附近、当前正在进行的笔画。</summary>
    public CarveMark? FindMarkNear(Vector2 p, float slack = 8f)
    {
        CarveMark? best = null;
        float bestD = float.MaxValue;
        foreach (var m in Marks)
        {
            if (m.Complete) continue;
            var grown = m.Bounds.Grow(slack);
            if (!grown.HasPoint(p)) continue;
            float d = grown.Grow(slack * 2).GetCenter().DistanceTo(p);
            if (d < bestD) { bestD = d; best = m; }
        }
        return best;
    }

    /// <summary>把 (x,y) 记进笔画进度。</summary>
    public void MarkTrace(CarveMark mark, int x, int y)
    {
        if (!InBounds(x, y)) return;
        int i = Index(x, y);
        if (!mark.Bounds.Grow(mark.Tolerance).HasPoint(new Vector2(x, y))) return;
        mark.MarkCovered(i);
    }

    // ── 拓 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 拓：把 Height 场按阈值转印到纸上。
    ///
    /// ★ 关键：转印的是**整层** Height，而磨改在 Height 里就是"某一段被重新凿过"，
    /// 所以拓片天然同时包含"改动"和"改动之前的痕迹"。第一章的题眼是数据模型的必然结果，
    /// 不是我们写出来的规则。
    ///
    /// 返回这次敲出来的墨量。
    /// </summary>
    public int Rub(int paperIdx, int srcIdx, float pressure)
    {
        int h = Height[srcIdx];
        if (h <= 0) return 0;

        int ink = (int)(pressure * (h / 255f) * 200f);
        if (ink <= 0) return 0;

        if (Dust[srcIdx] > 0)
        {
            // 没擦开的地方拓不到东西——这就是"必须先擦"的理由
            return 0;
        }

        int prev = PaperInk[paperIdx];
        int next = Math.Min(255, prev + ink);
        PaperInk[paperIdx] = (byte)next;
        return next - prev;
    }

    /// <summary>拓片用的墨量场（章节自己分配大小）。</summary>
    public byte[]? PaperInk { get; set; }

    // ── 查询 ────────────────────────────────────────────────────────────

    /// <summary>某像素的"新旧程度"：Batch 越靠后，越可疑。供 UI 和判定用。</summary>
    public Batch GetBatch(int x, int y) => (Batch)BatchMap[Index(x, y)];

    /// <summary>把 Height/Batch 场写进 RGBA 图，供 SurfaceView 上传。</summary>
    public Image MakeChannelImage(Image.Format format)
    {
        var img = Image.CreateEmpty(W, H, false, format);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            int i = Index(x, y);
            img.SetPixel(x, y, new Color(Height[i] / 255f, Dust[i] / 255f,
                                         Pile[i] / 255f, BatchMap[i] / 255f));
        }
        return img;
    }
}