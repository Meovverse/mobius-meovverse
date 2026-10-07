#if DEBUG
using System;
using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Dev;

/// <summary>
/// 墓园 RPG 走动式探索 —— 地图/墓地.png 直接当关卡，土路就是给脚走的。
///
/// 这推翻了"墓园必须平视固定取景"的旧口径：视角冲突不是地图的问题，
/// 是取景设定的问题。改成俯视 RPG 探索后，这张 960×720 的地图本体就是关卡：
/// 中央十字路是出生点，A 区 7 号（左上围栏里的碑 3）是目的地。
/// 近景调查（擦/描/拓/量的那些镜头）不在这张图上做，走近碑后切特写 ——
/// 探索用地图、调查用特写，两套镜头各干各的。
///
/// 可走判定不用碰撞层、不用 Tiled 瓦片：土路颜色太好认了，
/// 直接对像素分类（暖沙色：R>180, G>150, B<195, R-B>70），960×720 一次算完。
/// </summary>
public partial class RPGExplo : Node2D
{
    // 视野 960×540 世界像素 = 640×360 缓冲区 ÷ (2/3) 相机 zoom。窗口永远是
    // 项目的 1280×720——运行期不改窗口大小（失焦 bug 的根因，见 Boot 同款注释）。
    static readonly Vector2 View = new(960, 540);
    const float Speed = 70f;               // 地图像素/秒
    static readonly Vector2 Spawn = new(480, 300);      // 中央十字路
    static readonly Vector2 Goal = new(295, 62);        // A 区 7 号：左上围栏里那排碑的高碑（当初抠 stele_bg 的同源）
    static readonly Vector2 GoalStand = new(240, 101);  // 碑前最近的路点（围栏外的横向小径）

    Image _map; bool[] _walk, _closed, _safe; int _mw, _mh;
    Node2D _ysort;    // 树 + 人物同在一个 Y-Sort 容器，按脚底互相排序
    Vector2 _start, _goal;
    int _lastLeg = -1;
    Sprite2D _player; Node2D _camOwner; Label _prompt; Beacon _marker; Label _beaconTag; CanvasLayer _beaconLayer;
    static readonly string[] dirKeys = ["B", "F", "L", "R"];   // 走路文件名轴
    readonly Texture2D[,] _frames = new Texture2D[4, 2];
    int _face = 2; float _anim;
    static Texture2D ResLoader<T>(string path) where T : class => ResourceLoader.Load<Texture2D>(path);
    Vector2 _pos;
    List<Vector2> _route; int _leg;
    bool _manual, _arrived, _free, _qingming;
    int _frame, _stuck;

    public override void _Ready()
    {
        MoShi.Game.MenuHud.Ensure(GetTree(), "墓园 · 走访途中", true);
        AudioIndex.Ambience("amb_graveyard");   // 2026 墓园环境（第五批交付）
        // 第二批美术交付了清明落成版（终章 P10）：和平时版只差 5.2% 像素。
        // ★ 但"加灰蓝雾"会让路色漂移，按颜色分路面直接失灵（实测路网少 18k px、
        //   寻路失败）。所以：**可走面永远从剥离过的平时版算，清明版只换显示层**。
        //   两版布局逐像素同构，这是合法的——雾不该改地形。
        var clean = ResourceLoader.Load<Texture2D>("res://assets/textures/map_graveyard_clean.png");
        _map = clean.GetImage();
        _map.Convert(Image.Format.Rgb8);
        _mw = _map.GetWidth(); _mh = _map.GetHeight();

        var args = OS.GetCmdlineUserArgs();
        _qingming = System.Array.IndexOf(args, "qingming") >= 0;
        // -- free：自由走（不带自动寻路、不截图、不退出）——给人试玩用的模式
        // 2026-10-06 真人反馈"进墓园人物自己狂奔然后闪退"：自动寻路+到点 Quit
        // 是回归测试台的行为，不该是默认。现在——默认就是自由走，
        // 要跑回归显式 -- auto（自动路径+截图+到点退出，原测试链不变）。
        bool autoDemo = System.Array.IndexOf(args, "auto") >= 0;
        _free = !autoDemo;
        if (_free) _manual = true;
        var bg = new Sprite2D
        {
            Texture = _qingming
                ? ResourceLoader.Load<Texture2D>("res://assets/textures/map_graveyard_qingming_clean.png")
                : clean,
            Centered = false,
        };
        AddChild(bg);

        BuildWalkMask();
        CloseGaps();               // 先愈合抖动凹口
        FillHoles(12000);          // #43：再填内部小孔（路面上的装饰/杂点不再咬出卡点）
        SpawnTrees();              // 树精灵按美术遮罩清单进 Y-Sort（须在 _qingming 赋值之后）
        ErodeFootprint();          // 侵蚀必须放在所有改路面之后
        _start = NearestSafe(Spawn);
        _goal = NearestSafe(GoalStand);
        _route = _free ? null : Simplify(BFS(_start, _goal));  // 自由模式不寻路
        GD.Print($"[rpg] 路面 {FindWalkableCount()} px，安全面 {CountSafe()} px，start={_start} goal={_goal} 路线 {_route?.Count ?? -1}");

        // 四批：老吴 RPG 小人走路帧（四面×两帧）。站立用对应方向第 0 帧，
        // 移动时 8fps 换帧——从此人物有方向、有步态，不再是"永远的后脑勺"。
        for (int d = 0; d < 4; d++)
            for (int f = 0; f < 2; f++)
                _frames[d, f] = ResLoader<Texture2D>($"res://assets/textures/ww_" + dirKeys[d] + f + ".png");
        _player = new Sprite2D
        {
            Texture = _frames[2, 0],
            // Citrate#12：人物贴图里人物本体只有 ~25px 宽、~125px 高（0.2 的
            // 肩高比），等比缩放后是根"竹竿"。横向单独放大到 0.58，得到
            // 15×46 的肩高比（≈0.33，正常人形），像素风下不违和。
            Scale = new Vector2(0.58f, 0.37f),
            Offset = new Vector2(0, 22),                    // 脚底对齐世界坐标
        };
        _ysort.AddChild(_player);   // ★ 和树同容器才比得出前后

        _camOwner = new Node2D { Position = _start };
        var cam = new Camera2D { Zoom = new Vector2(2f / 3f, 2f / 3f) };
        _camOwner.AddChild(cam);
        AddChild(_camOwner);
        cam.MakeCurrent();

        // 目的地标只能是子节点：父节点自己的 _Draw 永远画在地图（同为子节点）
        // 之前，实测菱形被整张地图盖掉。8×8 冷白菱形，一张小 Image 解决。
        // Citrate657#3：一颗小白菱形不够——玩家在路上根本注意不到。
        // 信标柱（半透亮柱+地面脉冲环）+ 章名浮标，全程可见；越肩提示保留。
        // ★ Citrate#39：信标原来立在碑上(Goal)，但玩家能站的触发点在 GoalStand，
        //   两者差 ~60px——玩家走进光圈按 Enter 却没反应。信标改立到**触发点**上。
        _marker = new Beacon { Position = GoalStand + new Vector2(0, -4), ZIndex = 29 };
        AddChild(_marker);
        _beaconTag = new Label
        {
            Text = "A 区 7 号 · 苏兰墓",
            Modulate = new Color(1f, 0.97f, 0.85f, 0.95f),
        };
        _beaconTag.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        _beaconTag.AddThemeConstantOverride("shadow_offset_x", 1);
        _beaconTag.AddThemeConstantOverride("shadow_offset_y", 1);
        var bl = new CanvasLayer(); bl.AddChild(_beaconTag); AddChild(bl); _beaconLayer = bl;

        // 开场目标卡：我是谁、来干嘛、怎么动。12 秒后自己淡出。
        var open = new Label
        {
            Position = new Vector2(0, 298), Size = new Vector2(640, 58),
            Text = "第一章 · 碑上的名字\n安和园 A 区 7 号，售后回访。（方向键走 · Enter 互动）",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        open.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        open.AddThemeConstantOverride("shadow_offset_x", 1);
        open.AddThemeConstantOverride("shadow_offset_y", 1);
        var ol = new CanvasLayer(); ol.AddChild(open); AddChild(ol);
        var tw = open.CreateTween();
        tw.TweenInterval(12);
        tw.TweenProperty(open, "modulate:a", 0f, 1.5);

        _pos = _start;
    }

    void BuildWalkMask()
    {
        _walk = new bool[_mw * _mh];
        for (int y = 0; y < _mh; y++)
        for (int x = 0; x < _mw; x++)
        {
            var c = _map.GetPixel(x, y);
            int r = (int)(c.R * 255), g = (int)(c.G * 255), b = (int)(c.B * 255);
            _walk[y * _mw + x] = r > 180 && g > 150 && b < 195 && r - b > 70;
        }
    }

    int CountSafe()
    {
        int n = 0;
        for (int i = 0; i < _safe.Length; i++) if (_safe[i]) n++;
        return n;
    }

    int FindWalkableCount()
    {
        int n = 0;
        for (int i = 0; i < _walk.Length; i++) if (_walk[i]) n++;
        return n;
    }

    /// <summary>
    /// 视线法简化。顿挫感的主因是 500+ 个单像素航点，每个航点方向都在抖，
    /// 速度全耗在"对准下一个像素"上。贪心找"从当前点能直走到的最远点"作
    /// 航点——留下的每一段都是可直行的线段。比"方向改变点"更狠：
    /// 8 邻域 BFS 在直廊里交替 (1,0)/(0,-1)，方向改点仍有 131 个假拐点，
    /// 视线法直接压成十几段直线。
    /// </summary>
    List<Vector2> Simplify(List<Vector2> path)
    {
        if (path == null || path.Count < 3) return path;
        var keep = new List<Vector2>();
        int i = 0;
        while (i < path.Count - 1)
        {
            int j = path.Count - 1;
            while (j > i + 1 && !LineOk(path[i], path[j])) j--;
            keep.Add(path[j]);
            i = j;
        }
        return keep;
    }

    bool LineOk(Vector2 a, Vector2 b)
    {
        float d = a.DistanceTo(b);
        int n = Mathf.Max(1, (int)(d * 2));
        for (int k = 0; k <= n; k++)
            if (!Safe(a.Lerp(b, k / (float)n))) return false;
        return true;
    }

    bool Walkable(Vector2 p) =>
        p.X >= 0 && p.Y >= 0 && p.X < _mw && p.Y < _mh && _walk[Mathf.RoundToInt(p.Y) * _mw + Mathf.RoundToInt(p.X)];

    /// <summary>
    /// 二值闭运算（膨胀2再腐蚀2）。小径边缘是抖动色，逐像素分类会在路沿
    /// 咬出 1~3px 的凹口；不愈合的话侵蚀后的安全面在这些凹口处直接断链
    /// ——实测 A 区的路网整段孤立、BFS 无路可走。
    /// </summary>
    void CloseGaps()
    {
        // 半径 5 不是 2：碑前那片路的**卵石堆**在可走面上咬出 8~12px 的孔，
        // r=2 缝不住，左上碑排的路整段断链（真人反馈"被石堆挡住"）。
        // 闭运算只填小孔——大片草地被膨胀 5 后仍离路面太远，腐蚀后原样回去，不漏。
        _closed = Dilate(_walk, 5);
        _closed = Erode(_closed, 5);
    }

    bool[] Dilate(bool[] src, int r)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < _mh; y++)
        for (int x = 0; x < _mw; x++)
        {
            if (!src[y * _mw + x]) continue;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < _mw && ny < _mh) dst[ny * _mw + nx] = true;
            }
        }
        return dst;
    }

    /// <summary>#43：填被路面包住的内部小孔（边缘洪泛 + 只填小孔），去掉路上的卡点。</summary>
    void FillHoles(int maxHole)
    {
        int w = _mw, h = _mh;
        var reach = new bool[_walk.Length];
        var q = new Queue<int>();
        void Seed(int i) { if (!_walk[i] && !reach[i]) { reach[i] = true; q.Enqueue(i); } }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int cx = c % w, cy = c / w;
            if (cx > 0) Seed(c - 1); if (cx < w - 1) Seed(c + 1);
            if (cy > 0) Seed(c - w); if (cy < h - 1) Seed(c + w);
        }
        var vis = new bool[_walk.Length];
        for (int i = 0; i < _walk.Length; i++)
        {
            if (_walk[i] || reach[i] || vis[i]) continue;
            var comp = new List<int>(); var dq = new Queue<int>(); dq.Enqueue(i); vis[i] = true;
            while (dq.Count > 0)
            {
                int c = dq.Dequeue(); comp.Add(c); int cx = c % w, cy = c / w;
                if (cx > 0 && !vis[c - 1] && !_walk[c - 1] && !reach[c - 1]) { vis[c - 1] = true; dq.Enqueue(c - 1); }
                if (cx < w - 1 && !vis[c + 1] && !_walk[c + 1] && !reach[c + 1]) { vis[c + 1] = true; dq.Enqueue(c + 1); }
                if (cy > 0 && !vis[c - w] && !_walk[c - w] && !reach[c - w]) { vis[c - w] = true; dq.Enqueue(c - w); }
                if (cy < h - 1 && !vis[c + w] && !_walk[c + w] && !reach[c + w]) { vis[c + w] = true; dq.Enqueue(c + w); }
            }
            if (comp.Count <= maxHole) foreach (var c in comp) _walk[c] = true;
        }
    }

    bool[] Erode(bool[] src, int r)
    {
        var dst = new bool[src.Length];
        for (int y = r; y < _mh - r; y++)
        for (int x = r; x < _mw - r; x++)
        {
            bool ok = true;
            for (int dy = -r; dy <= r && ok; dy++)
            for (int dx = -r; dx <= r && ok; dx++)
                if (!src[(y + dy) * _mw + x + dx]) ok = false;
            dst[y * _mw + x] = ok;
        }
        return dst;
    }

    /// <summary>
    /// 树木层。方案演进：
    ///  ✗ 颜色聚类找树：草地和树冠同色，4-邻域整片连通（实测一个 959×579
    ///    的巨块），删路网和遮挡层全错——这就是"人物从树顶走过"的根因。
    ///  ✗ 固定遮挡层：树永远盖在人上面，人走到树"前面"时也盖，深度反了。
    ///  ✓ 离线模板匹配（cv2，prop_pine 当模板）产出 8 棵树的位置清单
    ///    data/gen/trees.json；同时把树从底图剥离（data/… 生成脚本补草/补路），
    ///    每棵树存成独立 sprite。运行时树和人物放同一个 Y-Sort 容器，
    ///    按脚底 Y 互相排序：人在树后→树盖人，人在树前→人盖树，天然正确。
    ///    树干那一格踢出路网（人绕树走），树冠悬在路面上方可以穿行。
    /// </summary>
    void SpawnTrees()
    {
        _ysort = new Node2D { YSortEnabled = true };
        AddChild(_ysort);

        // 树清单由美术交付的"抠出来的树"遮罩自动拆分生成（data/gen/trees(_qingming).json，
        // 工具见实现进度）。脚点=包围盒底边中点，Y-Sort 零偏移。
        string treeMan = _qingming ? "res://data/gen/trees_qingming.json" : "res://data/gen/trees.json";
        using var f = Godot.FileAccess.Open(treeMan, Godot.FileAccess.ModeFlags.Read);
        using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            int x = t.GetProperty("x").GetInt32(), y = t.GetProperty("y").GetInt32();
            int w = t.GetProperty("w").GetInt32(), h = t.GetProperty("h").GetInt32();
            var name = t.GetProperty("sprite").GetString();
            var tex = ResourceLoader.Load<Texture2D>($"res://assets/textures/{name}");

            // 锚点在树干底（精灵底部中间），Y-Sort 拿位置 Y 比较，就是脚底
            var spr = new Sprite2D { Texture = tex, Offset = new Vector2(0, -h + 2) };
            spr.Position = new Vector2(x + w / 2f, y + h - 1);
            _ysort.AddChild(spr);

            // 树干占格：底边中部 宽40%×高14 —— 只踢树干；树冠投影照常可走
            int tw = (int)(w * 0.4f);
            for (int ty = y + h - 14; ty < y + h - 2; ty++)
            for (int tx = x + w / 2 - tw / 2; tx < x + w / 2 + tw / 2; tx++)
                if (tx >= 0 && ty >= 0 && tx < _mw && ty < _mh) _closed[ty * _mw + tx] = false;
        }
    }

    /// <summary>
    /// 把"脚的占位"预烙进可走面：_safe = 中心+四脚探针全在土路上。
    ///
    /// ★ 不做这一步必然卡死：BFS 在原始路面上能找到中心可走、但脚占位
    ///   压到草缘的格子，角色（中心碰撞）走到那里就再也迈不开——实测停在
    ///   环岛边上 leg=7。寻路和移动必须用同一张面。
    /// </summary>
    void ErodeFootprint()
    {
        _safe = new bool[_mw * _mh];
        for (int y = 0; y < _mh; y++)
        for (int x = 0; x < _mw; x++)
        {
            if (!_closed[y * _mw + x]) continue;
            bool ok = true;
            foreach (var (dx, dy) in new[] { (-4, 0), (4, 0), (0, 2), (0, -2) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _mw || ny >= _mh || !_closed[ny * _mw + nx]) { ok = false; break; }
            }
            _safe[y * _mw + x] = ok;
        }
    }

    Vector2 NearestSafe(Vector2 p)
    {
        if (Safe(p)) return p;
        for (int r = 1; r <= 60; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var q = p + new Vector2(dx, dy);
                if (Safe(q)) return q;
            }
        return p;
    }

    bool Safe(Vector2 p) =>
        p.X >= 0 && p.Y >= 0 && p.X < _mw && p.Y < _mh && _safe[Mathf.RoundToInt(p.Y) * _mw + Mathf.RoundToInt(p.X)];

    bool CanStand(Vector2 p) => Safe(p);

    /// <summary>轴分离移动 + **角补偿**：主方向走不动时，横向/纵向错开一两像素再试。
    /// 路沿那点抖动凹口不再把角色"卡住"（Citrate#43/#52）。</summary>
    Vector2 Slide(Vector2 from, Vector2 delta, bool alongX)
    {
        if (delta == Vector2.Zero) return from;
        var to = from + delta;
        if (CanStand(to)) return to;
        foreach (int o in new[] { 2, -2, 4, -4, 6, -6 })
        {
            var c = alongX ? new Vector2(to.X, to.Y + o) : new Vector2(to.X + o, to.Y);
            if (CanStand(c)) return c;
        }
        return from;
    }

    List<Vector2> BFS(Vector2 a, Vector2 b)
    {
        var parent = new int[_mw * _mh]; Array.Fill(parent, -1);
        var q = new Queue<int>();
        int s = (int)a.Y * _mw + (int)a.X, t = (int)b.Y * _mw + (int)b.X;
        parent[s] = s; q.Enqueue(s);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            if (cur == t) break;
            int cx = cur % _mw, cy = cur / _mw;
            // 8 邻域。4 邻域的 BFS 会出对角阶梯，而角色是"先滑 X 再滑 Y"的
            // 轴分离移动——阶梯斜步会被轴分离卡死在草皮上（实测停在 leg=16）。
            // 斜走要求两条直角边都可走，杜绝穿栅栏角。
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= _mw || ny >= _mh) continue;
                int ni = ny * _mw + nx;
                if (!_safe[ni] || parent[ni] != -1) continue;
                if (dx != 0 && dy != 0 && (!_safe[cy * _mw + nx] || !_safe[ny * _mw + cx])) continue;
                parent[ni] = cur; q.Enqueue(ni);
            }
        }
        if (parent[t] == -1) return null;
        var path = new List<Vector2>();
        for (int cur = t; cur != s; cur = parent[cur])
            path.Add(new Vector2(cur % _mw, cur / _mw));
        path.Reverse();
        return path;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_cancel")) GetTree().Quit();
        _frame++;
        var v = Vector2.Zero;
        if (Input.IsActionPressed("ui_left")) v.X -= 1;
        if (Input.IsActionPressed("ui_right")) v.X += 1;
        if (Input.IsActionPressed("ui_up")) v.Y -= 1;
        if (Input.IsActionPressed("ui_down")) v.Y += 1;
        if (v != Vector2.Zero) _manual = true;

        if (!_manual && !_arrived && _route != null)
        {
            while (_leg < _route.Count && (_route[_leg] - _pos).Length() < 5f) _leg++;
            if (_leg >= _route.Count) { Arrive(); }
            else
            {
                v = (_route[_leg] - _pos).Normalized();
                // 卡住检测：路缘的抖动凹口会让 ±4 脚探针在最后一两像素拒绝
                // 浮点落脚点，而整数路点本身就在安全面上（BFS 就是在它上面
                // 选的）——卡 1/3 秒就吸附过去，肉眼看不出（≤3px，508px 路程）。
                _stuck = (_leg == _lastLeg) ? _stuck + 1 : 0;
                if (_stuck > 20 && CanStand(_route[_leg])) { _pos = _route[_leg]; _stuck = 0; }
                _lastLeg = _leg;
            }
        }

        if (v != Vector2.Zero && _arrived != true)
        {
            float dt = (float)delta;
            float step = Speed * dt;
            _pos = Slide(_pos, new Vector2(v.X, 0) * step, true);
            _pos = Slide(_pos, new Vector2(0, v.Y) * step, false);
        }

        if (v != Vector2.Zero)
        {
            _face = Mathf.Abs(v.X) > Mathf.Abs(v.Y) ? (v.X < 0 ? 2 : 3) : (v.Y < 0 ? 0 : 1);
            _anim += (float)delta * 8f;
            _player.Texture = _frames[_face, ((int)_anim) % 2];
        }
        else _player.Texture = _frames[_face, 0];
        _player.Position = _pos;
        var target = new Vector2(
            Mathf.Clamp(_pos.X, View.X / 2f, _mw - View.X / 2f),
            Mathf.Clamp(_pos.Y, View.Y / 2f, _mh - View.Y / 2f));
        _camOwner.Position = _camOwner.Position.Lerp(target, 1f - Mathf.Exp(-9f * (float)delta));

        var screen = GetCanvasTransform() * (GoalStand + new Vector2(0, 12));   // 触发点上方，不压开场卡
        if (_beaconTag != null)
        {
            _beaconTag.Position = screen - new Vector2(_beaconTag.GetSize().X / 2f, 0);
            _beaconTag.Visible = screen.Y > -30 && screen.Y < 700;
        }
        // 走到碑前（或自动抵达）→ Enter 切特写。M2 的第一条接缝。
        bool near = (_pos - GoalStand).Length() < 34f;   // #39：放宽到 34，站在光圈里必触发
        if (near || _arrived)
        {
            if (_prompt == null)
            {
                _prompt = new Label { Position = new Vector2(0, 250), Size = new Vector2(640, 26),
                    HorizontalAlignment = HorizontalAlignment.Center, Text = "Enter：凑近看碑面" };
                var cl = new CanvasLayer(); cl.AddChild(_prompt); AddChild(cl);
            }
            if (Input.IsActionJustPressed("ui_accept"))
                GetTree().ChangeSceneToFile(MoShi.Game.ChapterFlow.Next());
        }
        if (_frame == 8 && !_free) Shot("rpg_start");
        if (_frame == 150 && !_free) Shot("rpg_mid");
        if (_arrived && !_manual && _frame % 30 == 0 && _frame < 3000) Shot("rpg_goal", once: true);
    }

    void Arrive()
    {
        if (_arrived) return;
        _arrived = true;
        GD.Print("[rpg] 走到了：A 区 7 号 · 苏兰墓前。接下来该切特写镜头（擦/描/拓/量）。");
    }

    void Shot(string name, bool once = false)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng($"res://data/gen/{name}.png");
        GD.Print($"[rpg] {name} 已存");
        if (once) GetTree().Quit();
    }
}

/// <summary>
/// 目的地信标：地面脉冲环 + 半透亮柱 + 顶端菱形。Citrate657#3——
/// 一颗会飘的小菱形在草丛里等于没有；亮柱从地面立到天，路过谁都能看到。
/// </summary>
public partial class Beacon : Node2D
{
    private float _t;
    public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }
    public override void _Draw()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.4f);
        // 地面暗托 + 脉冲环：先垫底再画环，草地上也看得见
        DrawCircle(Vector2.Zero, 17f, new Color(0.05f, 0.05f, 0.06f, 0.30f));
        float ring = 7f + 11f * (_t % 1.2f / 1.2f);
        DrawArc(Vector2.Zero, ring, 0, Mathf.Tau, 32, new Color(1f, 0.93f, 0.72f, 0.95f - 0.55f * (_t % 1.2f / 1.2f)), 2f);
        // 亮柱：44px，底部近实
        for (int i = 0; i < 44; i++)
        {
            float a = (0.62f + 0.22f * pulse) * (1f - i / 50f);
            DrawLine(new Vector2(0, -i), new Vector2(0, -i - 1), new Color(1f, 0.93f, 0.66f, a), 6f);
        }
        // 顶菱（加大）
        var top = new Vector2(0, -50 - 3f * pulse);
        DrawPolygon(new[] { top + Vector2.Up * 6, top + Vector2.Right * 4.5f, top + Vector2.Down * 6, top + Vector2.Left * 4.5f },
                    new[] { new Color(1f, 0.98f, 0.9f, 1f) });
    }
}
#endif