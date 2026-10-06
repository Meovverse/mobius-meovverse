using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// M2 · 第一章垂直切片：「碑上的名字」前半段。
///
/// 链路（对分镜稿第一章）：走近碑（RPGExplo 按 Enter）→ 凑近碑面（本场景）→
/// 按住右键擦尘 → 日期区的"新石面"露出 → 按住左键沿磨痕描（sixteen_under_seventeen）→
/// 描满 55% → **线索 01 + stone_altered**（走 A）。也可以不描：
/// 点右下角登记本（走 B → 线索 02），或点最右"离开"（走 C → 结局一）。
///
/// ★ 全部判定读的都是 SteleBuilder 程序生成的那份 SurfaceModel——
///   擦过头的不可逆、描的进度、磨痕的位置，一个都不从画面反推。
///   这里唯一的"画面"就是那份数据的可视化，所以所见即所判。
/// </summary>
public partial class ChapterOne : Node2D
{
    public enum Phase { Wiping, Choice, Ended }

    const int W = SteleBuilder.W, H = SteleBuilder.H;
    const int BrushR = 16;

    SurfaceModel _sm;
    SteleBuilder.Layout _lay;
    CarveMark _sixteen;
    byte[] _base;                       // 石面底图 RGB（判定的可视化基底）
    byte[] _rgb = new byte[W * H * 3];
    Image _img; ImageTexture _tex;
    Sprite2D _stone;
    Label _hint, _tip;
    RunState _save;

    Phase _phase = Phase.Wiping;
    bool _breakFiredThisPress;
    float _traceBreakCooldown;
    int _breakPlays;
    bool _endingOne, _shot, _handOn;
    Sprite2D _hand;
    bool _ready, _wipeHold, _traceHold;
    float _renderAcc;
    int _dateSeen, _shotF;

    public override async void _Ready()
    {
        _save = RunState.Load();

        var fontPath = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        var font = ResourceLoader.Exists(fontPath) ? ResourceLoader.Load<Font>(fontPath) : ThemeDB.FallbackFont;

        _stone = new Sprite2D { Centered = false };
        AddChild(_stone);

        _hint = new Label { Position = new Vector2(16, 330), Size = new Vector2(560, 24) };
        _tip  = new Label { Position = new Vector2(16, 8),   Size = new Vector2(608, 40) };
        AddChild(_hint); AddChild(_tip);
        // 真人反馈"进去根本不知道怎么玩"——演示阶段先教再谈美学：
        // 首次进来，一只手在日期格上自动画圈示范擦。玩家一动手它就消失，永不再见。
        var handTex = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_cursor_hand.png");
        if (handTex != null)
        {
            _hand = new Sprite2D { Texture = handTex, Scale = new Vector2(0.6f, 0.6f), ZIndex = 50,
                                   Modulate = new Color(1, 1, 1, 0.85f) };
            AddChild(_hand);
            _handOn = !_save.Has("saw_wipe_hint");
        }
        // 上一版为了"手和光标不重叠"把系统光标整个藏了——真人反馈反过来变成
        // "找不到鼠标、以为游戏没反应"。光标永远可见是底线；示范手挪到日期区
        // 左下方画圈，和玩家手位不抢，任何按键一响它就退场。
        if (_handOn) _tip.Text = "灰挺厚。按住鼠标右键，画圈擦。（跟着那只手做就行）";

        _sm = await SteleBuilder.BuildAsync(font, this);
        _lay = SteleBuilder.LastLayout;
        foreach (var m in _sm.Marks) if (m.Id == "sixteen_under_seventeen") _sixteen = m;

        // 磨痕那一格本来就是"新凿的石面"——比周围亮一格的底图
        _base = SteleBuilder.RenderBase(_sm);
        _img = Image.CreateEmpty(W, H, false, Image.Format.Rgb8);
        _tex = ImageTexture.CreateFromImage(_img);
        _stone.Texture = _tex;

        _ready = true;
        _hint.Text = "碑蒙着这些年的灰。按住**鼠标右键**，在它上面慢慢画圈。";
        Rebuild();
        _shot = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot") >= 0;
    }

    // ── 渲染：SurfaceModel → 像素（脏了才重画，节流到 ~20fps）──────────

    void Rebuild()
    {
        var dust = _sm.Dust; var pile = _sm.Pile; var h = _sm.Height;
        var bm = _sm.BatchMap;
        for (int i = 0, p = 0; i < dust.Length; i++, p += 3)
        {
            float r = _base[p], g = _base[p + 1], b = _base[p + 2];
            int hv = h[i];
            if (hv > 0) { float k = 1f - hv * 0.5f / 255f; r *= k; g *= k; b *= k; }   // 刻痕压暗
            if (bm[i] == (byte)Batch.Repair) { r += 20; g += 20; b += 16; }            // 补刻面偏新偏亮
            int d = dust[i];
            if (d > 0)
            {
                // 数据层的 Dust 是 32px 颗粒（判定只看区域均值，粗没问题）；
                // 但直接画出来是棋盘。叠一层逐像素哈希当砂粒，观感就对了——
                // 只影响显示，判定永远读原始 Dust[]。
                int x = i % W, y = i / W;
                uint hh = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                hh ^= hh >> 13; hh *= 0x5bd1e995u; hh ^= hh >> 15;
                float grain = (hh & 0xFF) / 255f;
                float k = d / 255f * (0.60f + 0.40f * grain);
                r = r * (1 - k) + 199 * k; g = g * (1 - k) + 196 * k; b = b * (1 - k) + 186 * k;
            }
            int q = pile[i];
            if (q > 0) { float k = q / 255f * 0.38f; r = r * (1 - k) + 156 * k; g = g * (1 - k) + 148 * k; b = b * (1 - k) + 126 * k; }
            if (_sm.Crushed.Contains(i)) { r *= 0.74f; g *= 0.74f; b *= 0.76f; }       // 压进刻痕的暗脏
            _rgb[p] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
            _rgb[p + 1] = (byte)(g < 0 ? 0 : g > 255 ? 255 : g);
            _rgb[p + 2] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
        }
        _img.SetData(W, H, false, Image.Format.Rgb8, _rgb);
        _tex.Update(_img);
    }

    // ── 主循环 ─────────────────────────────────────────────────────────

    public override void _Notification(int what)
    {
        // 离开场景务必把系统光标还回来
        if (what == NotificationExitTree) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        if (_traceBreakCooldown > 0) _traceBreakCooldown -= (float)delta;   // 闸门独立于早退守卫
        if (!_ready || _sixteen == null && _phase != Phase.Wiping) return;   // 构建未完成时别跑分支逻辑
        var dt = (float)delta;
        var m = GetGlobalMousePosition();

        if (_shot && _shotF++ == 4 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot2") >= 0)
            ForceCleanDate();
        if (_shot && _shotF == 10)
        {
            var vp = GetViewport().GetTexture().GetImage();
            vp.SavePng("res://data/gen/m2_chapter1" + (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot2") >= 0 ? "_clean" : "") + ".png");
            GD.Print("[m2] shot vpC=" + vp.GetPixel(320, 180));
            GetTree().Quit();
            return;
        }

        // F9：把日期区瞬间擦净——给节奏调试和验收截图用，正式版会摘掉
        if (Input.IsActionJustPressed("F9_debug_reveal")) ForceCleanDate();

        if (_phase == Phase.Wiping)
        {
            bool rightNow = Input.IsMouseButtonPressed(MouseButton.Right);
            // 手形示范：绕日期格画圈。真人反馈"擦碑时查看图标闪烁"——
            // 根因是示范手和系统光标在日期区反复重叠。修：按住右键**同一帧**收手，
            // 并且示范期间藏起系统光标（只剩一只手，不打架）。
            if (_handOn)
            {
                bool anyDown = rightNow || Input.IsMouseButtonPressed(MouseButton.Left);
                if (anyDown)
                {
                    _handOn = false; _hand.Visible = false; _tip.Text = "";
                    _save.Set("saw_wipe_hint"); _save.Save();
                }
                else
                {
                    var ctr = DateRect().GetCenter();
                    float a = (float)Godot.Time.GetTicksMsec() / 700f;
                    _hand.Position = ctr + new Vector2(-58 + Mathf.Cos(a) * 22, 6 + Mathf.Sin(a) * 12);
                }
            }
            bool right = Input.IsMouseButtonPressed(MouseButton.Right);
            if (right)
            {
                int removed = _sm.Erase((int)m.X, (int)m.Y, BrushR, 240f * dt);
                if (!_wipeHold) { _wipeHold = true; AudioIndex.StartHold("loop_brush_stone"); }
                if (removed > 0 && (removed % 900) < 300) AudioIndex.Sfx("sfx_brush_pile");
                if (removed > 0) _renderAcc = 9;      // 每帧都脏，交给节流
            }
            else if (_wipeHold) { _wipeHold = false; AudioIndex.StopHold(); }

            // 日期区擦净到能看见"新旧石面的接缝" → 进入选择
            if (++_dateSeen > 20)
            {
                _dateSeen = 0;
                if (DateDustMean() < 0.08f)
                {
                    _phase = Phase.Choice; _hint.Text = "";
                    ShowGlow(true);
                    _tip.Text = "「16」这一格的石头比周围浅——那是被磨掉重刻过的地方。\n按住**鼠标左键**，在那块浅斑里慢慢描过去。";
                }
            }
        }
        else if (_phase == Phase.Choice)
        {
            // 描：左键按住沿磨痕走
            bool left = Input.IsMouseButtonPressed(MouseButton.Left);
            if (left)
            {
                var mark = _sm.FindMarkNear(m, 14f);   // 判定圈 10→14px：普通玩家手抖的余地
                if (mark != null)
                {
                    if (!_traceHold) { _traceHold = true; _breakFiredThisPress = false; AudioIndex.StartHold("loop_chisel_run"); }
                    // 笔刷半径 3：单程扫过就能覆盖，旧版 1px 要描十几遍
                    for (int oy = -3; oy <= 3; oy++)
                    for (int ox = -3; ox <= 3; ox++)
                        if (ox * ox + oy * oy <= 9) _sm.MarkTrace(mark, (int)m.X + ox, (int)m.Y + oy);
                    _renderAcc = 9;
                    if (mark == _sixteen && mark.Complete) SixteenFound();
                }
                else if (_traceHold)
                {
                    _traceHold = false; AudioIndex.StopHold();
                    // Natsume 反馈两条一起修：①断音不再连环重播（0.9s 冷却 +
                    // 每次按住只响一次）②**进度不再清零**——移出只是暂停，
                    // 回来接着描。旧版"离开=全部归零"让玩家原地卡死。
                    if (_sixteen != null && !_sixteen.Complete && _traceProgress() > 0
                        && !_breakFiredThisPress && _traceBreakCooldown <= 0f)   // Citrate#4：
                    {                                                            // 快速反复按住会绕过
                        _breakFiredThisPress = true;
                        _traceBreakCooldown = 1.2f;                                  // 必须再加时间闸门
                        _breakPlays++;
                        AudioIndex.Sfx("sfx_trace_break");
                        _tip.Text = "手移出去了——描过的都在。回到浅斑里接着描。";
                    }
                }
            }
            else if (_traceHold) { _traceHold = false; AudioIndex.StopHold(); }

            // Enter 兜底：看登记本
            if (Input.IsActionJustPressed("ui_accept")) ChooseB();
        }
        else // Ended
        {
            if (Input.IsActionJustPressed("ui_accept"))
                GetTree().ChangeSceneToFile(_endingOne ? "res://scenes/Boot.tscn" : ChapterFlow.Next());
        }

        _renderAcc += dt;
        if (_renderAcc >= 0.05f) { _renderAcc = 0; if (_wipeHold || _traceHold) Rebuild(); }
    }

    // 热区 B（随身登记本）/ C（离开）用"按下沿"点，避免和描的按住拖动打架
    public override void _UnhandledInput(InputEvent e)
    {
        if (_phase != Phase.Choice || e is not InputEventMouseButton mb || !mb.Pressed
            || mb.ButtonIndex != MouseButton.Left) return;
        var m = mb.Position;
        if (DateRect().Grow(8).HasPoint(m)) return;            // 点在日期上=要描，不切
        if (m.X >= W - 46) ChooseC();
        else if (new Rect2(16, 286, 96, 60).HasPoint(m)) ChooseB();
    }

    public bool TestReady => _ready && _sm != null;
    public Phase TestPhase => _phase;

    /// <summary>测试钩子：以真实同款参数横扫日期区——和手擦走同一条 Erase 路径。</summary>
    public void Test_WipeDate()
    {
        var r = DateRect();
        for (float yy = r.Position.Y - BrushR; yy < r.End.Y + BrushR; yy += 5)
        for (float xx = r.Position.X - BrushR; xx < r.End.X + BrushR; xx += 5)
            _sm.Erase((int)xx, (int)yy, BrushR, 40f);
        if (DateDustMean() < 0.08f && _phase == Phase.Wiping)
            _phase = Phase.Choice;
    }

    /// <summary>测试钩子：沿磨痕笔画整圈描满。</summary>
    public void Test_TraceSixteen()
    {
        if (_sixteen == null) return;
        var b = _sixteen.Bounds;
        for (int loop = 0; loop < 3 && !_sixteen.Complete; loop++)
        for (int yy = (int)b.Position.Y; yy < b.End.Y; yy++)
        for (int xx = (int)b.Position.X; xx < b.End.X; xx++)
            _sm.MarkTrace(_sixteen, xx, yy);
        if (_sixteen.Complete) SixteenFound();
    }

    /// <summary>测试钩子：在同一处空擦到把碎屑压进刻痕（走真实 Erase 物理）。</summary>
    public void Test_Overwipe()
    {
        var c0 = DateRect().GetCenter();
        for (int i = 0; i < 60; i++) _sm.Erase((int)c0.X, (int)c0.Y, 6, 60f);
    }
    public bool Test_CrushedGrew() => _sm.Crushed.Count > 0;

    public void Test_ChooseB() => ChooseB();
    public void Test_ChooseC() => ChooseC();

    void ForceCleanDate()
    {
        var r = DateRect();
        for (int y = (int)r.Position.Y; y < r.End.Y; y++)
        for (int x = (int)r.Position.X; x < r.End.X; x++)
        { int i = _sm.Index(x, y); if (y >= 0 && x >= 0 && i < _sm.Dust.Length) _sm.Dust[i] = 0; }
        _phase = Phase.Choice; _tip.Text = "「16」那一格的石头，比周围的白。"; Rebuild();
    }

    // 日期区 = 磨痕笔画的外扩。**用判定数据定热区**，不去猜字形坐标——
    //   字换了/挪了，热区自动跟着走。
    Rect2 DateRect() => _sixteen != null ? _sixteen.Bounds.Grow(14) : new Rect2(300, 240, 130, 110);

    float DateDustMean()
    {
        var r = DateRect();
        int n = 0; long sum = 0;
        for (int y = (int)r.Position.Y; y < r.End.Y; y++)
        for (int x = (int)r.Position.X; x < r.End.X; x++)
        { int i = _sm.Index(x, y); if (_sm.Height[i] > 0 || _sm.BatchMap[i] == (byte)Batch.Repair) { sum += _sm.Dust[i]; n++; } }
        return n == 0 ? 1f : sum / (float)n / 255f;
    }

    // ── 分支（全部落 RunState，对分镜稿"调查选择一"）──────────────────

    private GlowRect _glow;
    private void ShowGlow(bool on)
    {
        if (_sixteen == null) return;
        _glow ??= new GlowRect { Bounds = _sixteen.Bounds.Grow(6) };
        if (on && !_glow.IsInsideTree()) AddChild(_glow);
        if (_glow.IsInsideTree()) _glow.Visible = on;
    }
    private float _traceProgress() => _sixteen == null ? 0f : _sixteen.Progress;

    /// <summary>测试钩子：走与 _Process 完全相同的描迹分支。</summary>
    public void Test_TraceStep(Vector2 p, bool leftDown)
    {
        if (_sixteen == null) return;
        if (leftDown)
        {
            var mark = _sm.FindMarkNear(p, 14f);
            if (mark != null)
            {
                if (!_traceHold) { _traceHold = true; _breakFiredThisPress = false; }
                for (int oy = -3; oy <= 3; oy++)
                for (int ox = -3; ox <= 3; ox++)
                    if (ox * ox + oy * oy <= 9) _sm.MarkTrace(mark, (int)p.X + ox, (int)p.Y + oy);
                if (mark == _sixteen && mark.Complete) SixteenFound();
            }
        }
        else if (_traceHold)
        {
            _traceHold = false;
            if (_sixteen != null && !_sixteen.Complete && _traceProgress() > 0 && _traceBreakCooldown <= 0f)
            { _traceBreakCooldown = 1.2f; _breakPlays++; }   // 与 _Process 同一时间闸门
        }
    }
    public float Test_SixteenProgress() => _sixteen?.Progress ?? -1f;
    public int CountBreakSfxForTest() => _breakPlays;

    void SixteenFound()
    {
        AudioIndex.Sfx("sfx_trace_done");
        ShowGlow(false);
        _save.Set(RunState.Flag.Clue01); _save.Set(RunState.Flag.StoneAltered);
        _save.SetChoice(1, 'A');
        _save.Ledger.Add(new LedgerLine { Text = "「16」底下磨掉过什么。刻痕还是我的，字不是了。" });
        _save.Save();
        _hint.Text = ""; _tip.Text = "线索 01 · 碑面「16」存在磨痕。\n按 Enter。";
        _phase = Phase.Ended;
    }

    void ChooseB()
    {
        _save.Set(RunState.Flag.Clue02); _save.SetChoice(1, 'B');
        _save.Ledger.Add(new LedgerLine { Text = "系统里那两行都是五月十七。碑上刻的是十六。" });
        _save.Save();
        _tip.Text = "线索 02 · 墓园系统记录 5·17。（第二章从这里接）\n按 Enter 回墓道。";
        _phase = Phase.Ended;
    }

    void ChooseC()
    {
        _save.SetChoice(1, 'C');
        _save.Save();
        _endingOne = _save.EndingOneFired;
        _tip.Text = _endingOne
            ? "结局一 · 没发现碑的问题\n\n真相就在眼前，但你没有看见。\n\n按 Enter 回标题"
            : "你移开了视线。\n按 Enter 回碑前";
        _phase = Phase.Ended;
    }
}

/// <summary>磨痕区的呼吸描边：只画 1px 亮框 + 4% 填充，提示"是这里"，不解释（系统不出声）。</summary>
public partial class GlowRect : Node2D
{
    public Rect2 Bounds;
    private float _t;
    public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }
    public override void _Draw()
    {
        float a = 0.35f + 0.25f * Mathf.Sin(_t * 3f);
        DrawRect(Bounds, new Color(1f, 0.98f, 0.9f, a * 0.10f), true);
        DrawRect(Bounds, new Color(1f, 0.98f, 0.9f, a), false, 1f);
    }
}
