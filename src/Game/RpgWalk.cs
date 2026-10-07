using System;
using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// 实验分支：**可复用的俯视走位段**（把 RPGExplo 抽象出来）。
///
/// 用途：后面几章原来"一进来就是底图 + 文本框 + 资料卡"，现在可以改成
/// **在地图上操控老吴走动、跟着指引走到某个点、按 Enter 触发**那一场内容。
/// 每张地图一份子类配置（地图 / 出生点 / 目标点 / 指引），走位与分支机制共用。
///
/// 可走面：和墓园同一套思路——**按颜色分类**。内景里"暖棕/红砖地板"可走，
/// 灰石、白墙、紫陈列、纯黑（档案室透明区）都不可走。不是像素级碰撞，够用即可。
/// </summary>
public partial class RpgWalk : Node2D
{
    // ── 子类配置 ────────────────────────────────────────────────────────
    protected virtual string ChapterName => "走访";
    protected virtual string MapPath => "";
    protected virtual Vector2 SpawnWorld => new(480, 420);
    protected virtual string[] Guidance => new[] { "方向键：走动　·　走到标「▼」的点，按 Enter" };
    protected virtual RpgTarget[] Targets => Array.Empty<RpgTarget>();

    /// <summary>可走像素判定（默认=暖棕地板；子类可覆盖）。</summary>
    protected virtual bool WalkablePixel(int r, int g, int b)
    {
        bool warm = r > 110 && r - b > 28 && r >= g - 6;
        bool gray = Math.Abs(r - g) < 20 && Math.Abs(g - b) < 20;
        bool dark = r < 45 && g < 45 && b < 45;
        return warm && !gray && !dark;
    }

    public readonly record struct RpgTarget(string Label, Vector2 Pos, string Scene);

    // ── 走位状态 ────────────────────────────────────────────────────────
    private Image _map; private bool[] _safe; private int _mw, _mh;
    private Sprite2D _player; private Node2D _camOwner; private Vector2 _pos;
    private int _face = 1; private float _anim;
    private Label _prompt, _banner;
    private readonly List<(RpgTarget t, RpgBeacon b, Label tag)> _marks = new();
    private static readonly Vector2 View = new(960, 540);
    private const float Speed = 78f;
    private static readonly string[] DirKeys = { "B", "F", "L", "R" };
    private readonly Texture2D[,] _frames = new Texture2D[4, 2];

    public override void _Ready()
    {
        MenuHud.Ensure(GetTree(), ChapterName, true);

        var tex = ResourceLoader.Load<Texture2D>(MapPath);
        if (tex == null) { GD.PushError($"[rpg] 地图缺失：{MapPath}"); GetTree().Quit(1); return; }
        _map = tex.GetImage();
        _map.Convert(Image.Format.Rgb8);
        _mw = _map.GetWidth(); _mh = _map.GetHeight();
        AddChild(new Sprite2D { Texture = tex, Centered = false, ZIndex = -1 });

        BuildMask();

        for (int d = 0; d < 4; d++)
            for (int f = 0; f < 2; f++)
                _frames[d, f] = ResourceLoader.Load<Texture2D>($"res://assets/textures/ww_{DirKeys[d]}{f}.png");

        _player = new Sprite2D { Texture = _frames[1, 0], Scale = new Vector2(0.58f, 0.37f), Offset = new Vector2(0, 22) };
        AddChild(_player);

        _pos = NearestSafe(SpawnWorld);
        _camOwner = new Node2D { Position = _pos };
        _camOwner.AddChild(new Camera2D { Zoom = new Vector2(2f / 3f, 2f / 3f) });
        AddChild(_camOwner);
        _camOwner.GetChild<Camera2D>(0).MakeCurrent();

        // 目标点：地面信标柱 + 世界内标签（挂在 CanvasLayer 上保持屏幕空间）
        foreach (var t in Targets)
        {
            var beacon = new RpgBeacon { Position = t.Pos + new Vector2(0, -6) };
            AddChild(beacon);
            var tag = new Label { Text = t.Label, Modulate = new Color(1, 0.97f, 0.85f) };
            tag.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            tag.AddThemeConstantOverride("shadow_offset_x", 1);
            tag.AddThemeConstantOverride("shadow_offset_y", 1);
            var layer = new CanvasLayer(); layer.AddChild(tag); AddChild(layer);
            _marks.Add((t, beacon, tag));
        }

        // 顶部指引条（屏幕空间）
        _banner = new Label
        {
            Position = new Vector2(0, 6), Size = new Vector2(640, 44),
            HorizontalAlignment = HorizontalAlignment.Center, Text = string.Join("\n", Guidance),
        };
        _banner.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.95f));
        _banner.AddThemeConstantOverride("shadow_offset_x", 1);
        _banner.AddThemeConstantOverride("shadow_offset_y", 1);
        var bl = new CanvasLayer(); bl.AddChild(_banner); AddChild(bl);
    }

    // ── 可走面：优先读离线碰撞掩码（data/gen/walk_<map>.png），没有才按颜色分类 ──
    void BuildMask()
    {
        string baseName = System.IO.Path.GetFileNameWithoutExtension(MapPath);
        string maskPath = "res://data/gen/walk_" + baseName + ".png";
        var walk = new bool[_mw * _mh];
        var maskTex = ResourceLoader.Exists(maskPath) ? ResourceLoader.Load<Texture2D>(maskPath) : null;
        if (maskTex != null)
        {
            var mi = maskTex.GetImage();
            if (mi.GetWidth() == _mw && mi.GetHeight() == _mh)
            {
                mi.Convert(Image.Format.R8);        // 批量读像素，别逐格 GetPixel（几十万次会卡）
                var px = mi.GetData();
                for (int i = 0; i < px.Length && i < walk.Length; i++) walk[i] = px[i] > 128;
            }
            else maskTex = null;
        }
        if (maskTex == null)   // 回退：颜色分类
            for (int y = 0; y < _mh; y++)
            for (int x = 0; x < _mw; x++)
            {
                var c = _map.GetPixel(x, y);
                walk[y * _mw + x] = WalkablePixel((int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
            }

        _safe = new bool[_mw * _mh];
        for (int y = 3; y < _mh - 3; y++)
        for (int x = 4; x < _mw - 4; x++)
        {
            bool ok = walk[y * _mw + x];
            foreach (var (dx, dy) in new[] { (-4, 0), (4, 0), (0, 2), (0, -2) })
                if (!walk[(y + dy) * _mw + (x + dx)]) { ok = false; break; }
            _safe[y * _mw + x] = ok;
        }
    }

    bool Safe(Vector2 p) =>
        p.X >= 0 && p.Y >= 0 && p.X < _mw && p.Y < _mh && _safe[Mathf.RoundToInt(p.Y) * _mw + Mathf.RoundToInt(p.X)];

    Vector2 NearestSafe(Vector2 p)
    {
        if (Safe(p)) return p;
        for (int r = 1; r <= 120; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var q = p + new Vector2(dx, dy);
            if (Safe(q)) return q;
        }
        return p;
    }

    public override void _Process(double delta)
    {
        if (Input.IsActionJustPressed("ui_cancel")) { GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"); return; }

        var v = Vector2.Zero;
        if (Input.IsActionPressed("ui_left")) v.X -= 1;
        if (Input.IsActionPressed("ui_right")) v.X += 1;
        if (Input.IsActionPressed("ui_up")) v.Y -= 1;
        if (Input.IsActionPressed("ui_down")) v.Y += 1;

        if (v != Vector2.Zero)
        {
            var dt = (float)delta;
            var nx = _pos + new Vector2(v.X, 0) * Speed * dt;
            if (Safe(nx)) _pos = nx;
            var ny = _pos + new Vector2(0, v.Y) * Speed * dt;
            if (Safe(ny)) _pos = ny;
            _face = Math.Abs(v.X) > Math.Abs(v.Y) ? (v.X < 0 ? 2 : 3) : (v.Y < 0 ? 0 : 1);
            _anim += (float)delta * 8f;
            _player.Texture = _frames[_face, ((int)_anim) % 2];
        }
        else _player.Texture = _frames[_face, 0];
        _player.Position = _pos;

        // 相机：地图比视野大就跟随夹边，比视野小就居中（档案室裁剪版就比视野小）
        var target = new Vector2(
            _mw <= View.X ? _mw / 2f : Mathf.Clamp(_pos.X, View.X / 2f, _mw - View.X / 2f),
            _mh <= View.Y ? _mh / 2f : Mathf.Clamp(_pos.Y, View.Y / 2f, _mh - View.Y / 2f));
        _camOwner.Position = _camOwner.Position.Lerp(target, 1f - Mathf.Exp(-9f * (float)delta));

        // 目标标签跟屏幕
        var xf = GetCanvasTransform();
        foreach (var (t, _, tag) in _marks)
        {
            var screen = xf * (t.Pos + new Vector2(0, 12));
            tag.Position = screen - new Vector2(tag.GetSize().X / 2f, 0);
            tag.Visible = screen.Y > -30 && screen.Y < 700 && screen.X > -80 && screen.X < 720;
        }

        // 就近目标
        RpgTarget? near = null; float best = 26f;
        foreach (var (t, _, _) in _marks)
        {
            float d = _pos.DistanceTo(t.Pos);
            if (d < best) { best = d; near = t; }
        }
        if (near != null)
        {
            if (_prompt == null)
            {
                _prompt = new Label { Position = new Vector2(0, 300), Size = new Vector2(640, 26),
                    HorizontalAlignment = HorizontalAlignment.Center };
                _prompt.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.95f));
                _prompt.AddThemeConstantOverride("shadow_offset_x", 1);
                _prompt.AddThemeConstantOverride("shadow_offset_y", 1);
                var pl = new CanvasLayer(); pl.AddChild(_prompt); AddChild(pl);
            }
            _prompt.Text = "Enter：" + near.Value.Label;
            if (Input.IsActionJustPressed("ui_accept"))
                GetTree().ChangeSceneToFile(near.Value.Scene);
        }
        else if (_prompt != null) _prompt.Text = "";
    }
}

/// <summary>目标点：地面脉冲环 + 半透亮柱 + 顶端菱形（照搬墓园 Beacon 的观感）。</summary>
public partial class RpgBeacon : Node2D
{
    private float _t;
    public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }
    public override void _Draw()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 2.4f);
        DrawCircle(Vector2.Zero, 15f, new Color(0.05f, 0.05f, 0.06f, 0.30f));
        float ring = 6f + 10f * (_t % 1.2f / 1.2f);
        DrawArc(Vector2.Zero, ring, 0, Mathf.Tau, 28, new Color(1f, 0.93f, 0.72f, 0.95f - 0.55f * (_t % 1.2f / 1.2f)), 2f);
        for (int i = 0; i < 40; i++)
        {
            float a = (0.6f + 0.2f * pulse) * (1f - i / 46f);
            DrawLine(new Vector2(0, -i), new Vector2(0, -i - 1), new Color(1f, 0.93f, 0.66f, a), 6f);
        }
        var top = new Vector2(0, -46 - 3f * pulse);
        DrawPolygon(new[] { top + Vector2.Up * 6, top + Vector2.Right * 4.5f, top + Vector2.Down * 6, top + Vector2.Left * 4.5f },
                    new[] { new Color(1f, 0.98f, 0.9f, 1f) });
    }
}
