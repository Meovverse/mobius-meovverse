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
    const int Zoom = 2;                    // 640×360 窗口 → 看见 320×180 的地图
    const float Speed = 70f;               // 地图像素/秒
    static readonly Vector2 Spawn = new(480, 300);      // 中央十字路
    static readonly Vector2 Goal = new(295, 62);        // A 区 7 号：左上围栏里那排碑的高碑（当初抠 stele_bg 的同源）
    static readonly Vector2 GoalStand = new(240, 101);  // 碑前最近的路点（围栏外的横向小径）

    Image _map; bool[] _walk, _closed, _safe; int _mw, _mh;
    Vector2 _start, _goal;
    int _lastLeg = -1;
    Sprite2D _player, _marker; Node2D _camOwner;
    Vector2 _pos;
    List<Vector2> _route; int _leg;
    bool _manual, _arrived;
    int _frame, _stuck;

    public override void _Ready()
    {
        var tex = ResourceLoader.Load<Texture2D>("res://assets/textures/map_graveyard.png");
        _map = tex.GetImage();
        _map.Convert(Image.Format.Rgb8);
        _mw = _map.GetWidth(); _mh = _map.GetHeight();

        var bg = new Sprite2D { Texture = tex, Centered = false };
        AddChild(bg);

        BuildWalkMask();
        CloseGaps();               // 先愈合
        ErodeFootprint();          // 再侵蚀
        _start = NearestSafe(Spawn);
        _goal = NearestSafe(GoalStand);
        _route = BFS(_start, _goal);
        GD.Print($"[rpg] 路面 {FindWalkableCount()} px，安全面 {CountSafe()} px，start={_start} goal={_goal} 路线 {_route?.Count ?? -1}");

        var body = ImageTexture.CreateFromImage(Art.CharLuYunBack(160, 240));
        _player = new Sprite2D
        {
            Texture = body,
            Scale = new Vector2(40f / 240f, 40f / 240f),   // 26×40：碑 34px 高，人比碑略矮
            Offset = new Vector2(0, -20),                   // 脚底对齐世界坐标
            ZIndex = 10,
        };
        AddChild(_player);

        _camOwner = new Node2D { Position = _start };
        var cam = new Camera2D { Zoom = new Vector2(Zoom, Zoom) };
        _camOwner.AddChild(cam);
        AddChild(_camOwner);
        cam.MakeCurrent();

        // 目的地标只能是子节点：父节点自己的 _Draw 永远画在地图（同为子节点）
        // 之前，实测菱形被整张地图盖掉。8×8 冷白菱形，一张小 Image 解决。
        var dia = Image.CreateEmpty(9, 9, false, Image.Format.Rgba8);
        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 9; x++)
            if (Mathf.Abs(x - 4) + Mathf.Abs(y - 4) <= 4)
                dia.SetPixel(x, y, new Color(0.95f, 0.97f, 1f, 0.85f));
        _marker = new Sprite2D { Texture = ImageTexture.CreateFromImage(dia), ZIndex = 30 };
        AddChild(_marker);

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

    bool Walkable(Vector2 p) =>
        p.X >= 0 && p.Y >= 0 && p.X < _mw && p.Y < _mh && _walk[(int)p.Y * _mw + (int)p.X];

    /// <summary>
    /// 二值闭运算（膨胀2再腐蚀2）。小径边缘是抖动色，逐像素分类会在路沿
    /// 咬出 1~3px 的凹口；不愈合的话侵蚀后的安全面在这些凹口处直接断链
    /// ——实测 A 区的路网整段孤立、BFS 无路可走。
    /// </summary>
    void CloseGaps()
    {
        _closed = Dilate(_walk, 2);
        _closed = Erode(_closed, 2);
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
        p.X >= 0 && p.Y >= 0 && p.X < _mw && p.Y < _mh && _safe[(int)p.Y * _mw + (int)p.X];

    bool CanStand(Vector2 p) => Safe(p);

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
        _frame++;
        var v = Vector2.Zero;
        if (Input.IsActionPressed("ui_left")) v.X -= 1;
        if (Input.IsActionPressed("ui_right")) v.X += 1;
        if (Input.IsActionPressed("ui_up")) v.Y -= 1;
        if (Input.IsActionPressed("ui_down")) v.Y += 1;
        if (v != Vector2.Zero) _manual = true;

        if (!_manual && !_arrived && _route != null)
        {
            while (_leg < _route.Count && (_route[_leg] - _pos).Length() < 2.5f) _leg++;
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
            var nx = _pos + new Vector2(v.X, 0) * Speed * dt;
            if (CanStand(nx)) _pos = nx;
            var ny = _pos + new Vector2(0, v.Y) * Speed * dt;
            if (CanStand(ny)) _pos = ny;
        }

        _player.Position = _pos;
        _player.FlipH = v.X < 0 ? true : v.X > 0 ? false : _player.FlipH;
        _camOwner.Position = new Vector2(
            Mathf.Clamp(_pos.X, 160, _mw - 160),
            Mathf.Clamp(_pos.Y, 90, _mh - 90));

        var mt = (float)Godot.Time.GetTicksMsec() / 1000f;
        _marker.Position = Goal + new Vector2(0, Mathf.Sin(mt * 3f) * 2f - 34);
        if (_frame == 8) Shot("rpg_start");
        if (_arrived && _frame % 30 == 0 && _frame < 3000) Shot("rpg_goal", once: true);
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

#endif
