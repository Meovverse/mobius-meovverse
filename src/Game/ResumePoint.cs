using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// 封面之后的分支屏：「桌前的账本」——继续 or 从头。
///
/// 设计（全部是画面内的东西，没有对话框/弹窗）：
///   封面压暗当背景（刚看完的画，视线不断）；一句旁白把选择说成动作——
///   账本在桌上，"翻开接着写"还是"另起一本"。
///   从头开始 = **原地二段确认**：按下去那行字自己变口吻，4 秒内再按才成立。
///   选项行整行可点（全宽命中区），空白处什么都不发生——上一版"点空白=确认"
///   被真人投诉误触与无反馈，废除。
/// </summary>
public partial class ResumePoint : Node2D
{
    private const int VW = 640, VH = 360;
    private Label _resumeRow, _resumeSub, _freshRow, _freshSub, _backHint, _narr;
    private int _sel;                       // 0=继续 1=从头 （无档时只有 1 可用）
    private bool _hasSave;
    private double _armLeft;                // 二段确认剩余秒数
    private ColorRect _white;
    private bool _leaving;

    public override void _Ready()
    {
        _hasSave = RunState.HasSave();

        AddChild(new ColorRect { Color = new Color(0, 0, 0), Size = new Vector2(VW, VH),
                                 MouseFilter = Control.MouseFilterEnum.Ignore });
        // ★ Citrate657 #1：这一屏有 UI 文字，必须用**无字封面**（美术重制版），
        //   带字的那张只属于标题画面本体。
        var art = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title_notext.png");
        if (art == null) art = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title.png");
        if (art != null)
            AddChild(new Sprite2D { Texture = art, Centered = false, Scale = new Vector2(0.5f, 0.5f),
                                    Modulate = new Color(1, 1, 1, 0.35f) });
        _white = new ColorRect { Color = new Color(1, 1, 1, 0), Size = new Vector2(VW, VH),
                                 MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_white);
        MoShi.Core.AudioIndex.StopTitle(1.0f);   // 保险：标题曲止于封面

        _narr = Row(64, "桌上是那本旧账。写过的都还在。", 0.55f);
        if (_hasSave)
        {
            _resumeRow = Row(120, "", 1f);
            _resumeSub = Row(142, "", 0.5f);
            var st = RunState.Load();
            _resumeSub.Text = RunState.Describe() + " · 翻到《" + ChapterFlow.Label(ChapterFlow.Next()) + "》";
        }
        _freshRow = Row(_hasSave ? 186 : 120, "", 1f);
        _freshSub = Row(_hasSave ? 208 : 142, "回到 2006 年那个下午，从空白页写起", 0.5f);
        _backHint = Row(320, "↑↓ 选择 · Enter 确认 · Esc 回封面", 0.35f);

        _sel = _hasSave ? 0 : 1;
        Render();
    }

    private Label Row(float y, string text, float alpha)
    {
        var l = new Label
        {
            Position = new Vector2(0, y), Size = new Vector2(VW, 26),
            HorizontalAlignment = HorizontalAlignment.Center, Text = text,
            Modulate = new Color(1, 1, 1, alpha),
        };
        AddChild(l);
        return l;
    }

    private string FreshText => _armLeft > 0 ? "　另起一本 —— 这一按，旧账就撕了。再按一次算数"
                                             : "　另起一本";

    private void Render()
    {
        if (_resumeRow != null)
        {
            bool on = _sel == 0;
            _resumeRow.Text = (on ? "▶ " : "　") + "翻开它 —— 接着上次写";
            _resumeRow.Modulate = new Color(1, 1, 1, on ? 1f : 0.55f);
            _resumeSub.Position = new Vector2(0, 142);
        }
        bool freshOn = _sel == 1;
        _freshRow.Text = (freshOn ? "▶ " : "　") + FreshText.TrimStart('　');
        _freshRow.Modulate = new Color(1, 1, 1, freshOn ? 1f : 0.55f);
        // 注行选中才亮；撕账确认态时高亮警告
        _freshSub.Modulate = new Color(1, 1, 1, _armLeft > 0 ? 0.9f : freshOn ? 0.5f : 0.25f);
        _freshSub.Text = _armLeft > 0 ? $"{System.Math.Ceiling(_armLeft):0} 秒内再按一次成立，否则这行字忘记刚才的事"
                                      : "回到 2006 年那个下午，从空白页写起";
    }

    private int _shotF = -1;

    public override void _Process(double delta)
    {
        if (_shotF == -1 && System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "shot") >= 0) _shotF = 0;
        if (_shotF >= 0)
        {
            _shotF++;
            bool armed = System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "shot2") >= 0;
            if (_shotF == 30 && armed) { _sel = 1; _armLeft = 4.0; Render(); }
            if (_shotF == (armed ? 40 : 30))
            {
                GetViewport().GetTexture().GetImage().SavePng(armed ? "res://data/gen/resume_armed.png" : "res://data/gen/resume.png");
                GD.Print("[resume] shot"); GetTree().Quit(); return;
            }
        }
        if (_armLeft > 0)
        {
            _armLeft -= delta;
            Render();
            if (_armLeft <= 0) Render();   // 过期文案复位
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_leaving) return;
        if (e.IsActionPressed("ui_cancel"))
        {
            GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
            return;
        }

        bool clickResume = false, clickFresh = false;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            float y = mb.Position.Y;
            // 整行命中：主行+注行都算
            clickResume = _hasSave && y >= 116 && y < 168;
            float fy = _hasSave ? 186 : 120;
            clickFresh = y >= fy && y < fy + 52;
            if (!clickResume && !clickFresh) return;   // 空白：什么都不发生
        }
        if (e.IsActionPressed("ui_up") || e is InputEventKey { Pressed: true, Keycode: Key.W }) { _sel = 0; _armLeft = 0; Render(); return; }
        if (e.IsActionPressed("ui_down") || e is InputEventKey { Pressed: true, Keycode: Key.S }) { _sel = 1; _armLeft = 0; Render(); return; }
        if (clickResume) _sel = 0;
        if (clickFresh) _sel = 1;

        bool commit = e.IsActionPressed("ui_accept") || clickResume || clickFresh ||
                      e is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.Space };
        if (!commit) return;

        if (_sel == 0 && _hasSave) { Go(ChapterFlow.Next(), "sfx_page_turn"); return; }
        if (_sel == 1)
        {
            if (_armLeft <= 0) { _armLeft = 4.0; Render(); return; }   // 第一段：只是把话挑明
            RunState.DeleteSave();
            Go("res://scenes/ChPrologue.tscn", "sfx_pen_write");
        }
    }

    private void Go(string scenePath, string sfx)
    {
        _leaving = true;
        AudioIndex.Sfx(sfx);
        var tw = CreateTween();
        tw.TweenProperty(_white, "color:a", 1f, 0.35f);
        tw.TweenInterval(0.25);
        tw.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scenePath)));
    }
}
