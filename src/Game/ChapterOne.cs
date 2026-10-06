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
    enum Phase { Wiping, Choice, Ended }

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
    bool _endingOne;
    bool _ready, _wipeHold, _traceHold;
    float _renderAcc;
    int _dateSeen;

    public override async void _Ready()
    {
        _save = RunState.Load();

        var fontPath = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        var font = ResourceLoader.Exists(fontPath) ? ResourceLoader.Load<Font>(fontPath) : ThemeDB.FallbackFont;

        _stone = new Sprite2D { Centered = false };
        AddChild(_stone);

        _hint = new Label { Position = new Vector2(16, 330), Size = new Vector2(560, 24) };
        _tip  = new Label { Position = new Vector2(16, 8),   Size = new Vector2(608, 20) };
        AddChild(_hint); AddChild(_tip);

        _sm = await SteleBuilder.BuildAsync(font, this);
        _lay = SteleBuilder.LastLayout;
        foreach (var m in _sm.Marks) if (m.Id == "sixteen_under_seventeen") _sixteen = m;

        // 磨痕那一格本来就是"新凿的石面"——比周围亮一格的底图
        _base = SteleBuilder.RenderBase(_sm);
        _img = Image.CreateEmpty(W, H, false, Image.Format.Rgb8);
        _tex = ImageTexture.CreateFromImage(_img);
        _stone.Texture = _tex;

        _ready = true;
        _hint.Text = "按住右键擦。";
        Rebuild();
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
            if (d > 0) { float k = d / 255f * 0.82f; r = r * (1 - k) + 199 * k; g = g * (1 - k) + 196 * k; b = b * (1 - k) + 186 * k; }
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

    public override void _Process(double delta)
    {
        if (!_ready) return;
        var dt = (float)delta;
        var m = GetGlobalMousePosition();

        // F9：把日期区瞬间擦净——给节奏调试和验收截图用，正式版会摘掉
        if (Input.IsActionJustPressed("F9_debug_reveal")) ForceCleanDate();

        if (_phase == Phase.Wiping)
        {
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
                if (DateDustMean() < 0.08f) { _phase = Phase.Choice; _hint.Text = ""; _tip.Text = "「16」那一格的石头，比周围的白。"; }
            }
        }
        else if (_phase == Phase.Choice)
        {
            // 描：左键按住沿磨痕走
            bool left = Input.IsMouseButtonPressed(MouseButton.Left);
            if (left)
            {
                var mark = _sm.FindMarkNear(m, 10f);
                if (mark != null)
                {
                    if (!_traceHold) { _traceHold = true; AudioIndex.StartHold("loop_chisel_run"); }
                    _sm.MarkTrace(mark, (int)m.X, (int)m.Y);
                    _renderAcc = 9;
                    if (mark == _sixteen && mark.Complete) SixteenFound();
                }
                else if (_traceHold)
                {
                    _traceHold = false; AudioIndex.StopHold();
                    if (_sixteen != null && !_sixteen.Complete) { _sixteen.Reset(); AudioIndex.Sfx("sfx_trace_break"); _tip.Text = "断了。从断的地方接着描。"; }
                }
            }
            else if (_traceHold) { _traceHold = false; AudioIndex.StopHold(); }

            // Enter 兜底：看登记本
            if (Input.IsActionJustPressed("ui_accept")) ChooseB();
        }
        else // Ended
        {
            if (Input.IsActionJustPressed("ui_accept"))
            {
                if (_endingOne) GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
                else GetTree().ChangeSceneToFile("res://scenes/RPGExplo.tscn");
            }
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

    void SixteenFound()
    {
        AudioIndex.Sfx("sfx_trace_done");
        _save.Set(RunState.Flag.Clue01); _save.Set(RunState.Flag.StoneAltered);
        _save.SetChoice(1, 'A');
        _save.Ledger.Add(new LedgerLine { Text = "「16」底下磨掉过什么。刻痕还是我的，字不是了。" });
        _save.Save();
        _hint.Text = ""; _tip.Text = "线索 01 · 碑面「16」存在磨痕。\n按 Enter 回墓道。";
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
