using System;
using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// 章节公共外壳：字幕卡 / 对话（对话框+头像）/ 调查选择热区 / 图版装载。
/// 规则（剧本 §7）：系统不出声、不弹"恭喜发现"；认知全部经手——
/// 所以这里的 UI 只有三种：画面、字、可点的东西。
/// </summary>
public partial class StorySceneBase : Node2D
{
    public const int VW = 640, VH = 360;

    protected RunState Save;
    protected Control Ui;
    private Label _sub;
    private ColorRect _subBg;
    private Action _onSubDone;
    private Queue<string> _subs = new();
    private float _subTimer;
    private const float SubHold = 3.2f;

    public override void _Ready()
    {
        // Install 幂等（RegisterAll 有守卫）：章节场景可能不经 Boot 直达
        // （编辑器里 F6 跑当前场景 / 集成测试 / 未来热跳章）——不装规则表
        // 则 AssetIntake.Get 抛"没登记的槽位"，_Ready 中断=黑屏半初始化。
        SlotRegistry.Install();

        Save = RunState.Load();
        Ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 10 };
        Ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Ui);
        _subBg = new ColorRect { Color = new Color(0, 0, 0, 0.65f), Visible = false,
                                 Position = new Vector2(0, VH - 54), Size = new Vector2(VW, 54) };
        _sub = new Label { Position = new Vector2(16, 10), Size = new Vector2(VW - 32, 36),
                           AutowrapMode = TextServer.AutowrapMode.WordSmart,
                           HorizontalAlignment = HorizontalAlignment.Center };
        _subBg.AddChild(_sub);
        Ui.AddChild(_subBg);
        ArmShot();
        SceneReady();
    }

    protected virtual void SceneReady() { }

    // ── 图版 ────────────────────────────────────────────────────────────

    /// <summary>取槽位贴图（美术交付→接管层修正；没有→程序版）。挂满屏。</summary>
    protected Sprite2D Plate(string slotKey, bool fitWidth = false)
    {
        var tex = AssetIntake.Get(slotKey);
        var s = new Sprite2D { Texture = tex, Centered = false };
        if (fitWidth)
        {
            float k = (float)VW / tex.GetWidth();
            s.Scale = new Vector2(k, k);
        }
        AddChild(s);
        return s;
    }

    /// <summary>
    /// 直接按文件路径贴图（不走槽位）。**必须给 targetH**：交付人像都是
    /// 512×768 起步，怼进 640×360 视口不缩放=真人反馈的"头像大幅超出"。
    /// 传 0 = 按原尺寸（仅用于本就 ≤ 视口的贴图）。at 是精灵中心。
    /// </summary>
    protected Sprite2D Raw(string resPath, Vector2 at, float targetH = 0f)
    {
        var t = ResourceLoader.Load<Texture2D>(resPath);
        if (t == null) return null;
        var s = new Sprite2D { Texture = t, Position = at };
        if (targetH > 0)
        {
            float k = targetH / t.GetHeight();
            s.Scale = new Vector2(k, k);
        }
        AddChild(s);
        return s;
    }

    /// <summary>纯色遮幅/黑场。</summary>
    protected ColorRect Black(float alpha = 1f)
    {
        // 1：盖住所有图版（z=0），Ui 在 z=10 不受影响
        var c = new ColorRect { Color = new Color(0, 0, 0, alpha), Size = new Vector2(VW, VH), ZIndex = 1 };
        AddChild(c);
        return c;
    }

    // ── 字幕流 ──────────────────────────────────────────────────────────

    /// <summary>排队播字幕；全部放完回调 done。点击/任意键跳下一条。</summary>
    protected void Subs(Action done, params string[] lines)
    {
        _subs = new Queue<string>(lines);
        _onSubDone = done;
        NextSub();
    }

    private void NextSub()
    {
        if (_subs.Count == 0) { _subBg.Visible = false; var d = _onSubDone; _onSubDone = null; d?.Invoke(); return; }
        _sub.Text = _subs.Dequeue();
        _subBg.Visible = true;
        _subTimer = 0;
    }

    /// <summary>系统静默：字幕只在停留够久或点击后推进，绝不自动跳红字。</summary>
    public override void _Process(double dt)
    {
        if (_shotF >= 0)
        {
            _shotF++;
            if (_shotF % 14 == 0)   // 截图模式：合成点击推进（字幕与对话都吃）
            {
                var pd = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(320, 100) };
                var pu = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(320, 100) };
                Input.ParseInputEvent(pd); Input.ParseInputEvent(pu);
            }
            if (_shotF == 300)
            {
                GetViewport().GetTexture().GetImage().SavePng("res://data/gen/scene_shot.png");
                GD.Print("[shot] scene_shot 已存");
                GetTree().Quit();
                return;
            }
        }
        if (!_subBg.Visible) return;
        _subTimer += (float)dt;
        // Enter 推进（0.35s 防重键：同一帧的 Enter 既"打开"又"跳过"）
        if (Input.IsActionJustPressed("ui_accept") && _subTimer > 0.35f) NextSub();
    }

    // ── 对话（看 §7：老吴两句、苏航短平）──────────────────────────────

    /// <summary>一行一条 (谁, 话)。who: "wu"/"su"/""=旁白。</summary>
    protected void Dialogue((string who, string line)[] lines, Action done)
    {
        // 通铺三边：贴左、贴右、贴底（真人反馈：对话框要占满左侧右侧与下侧）
        var panel = new Panel { Visible = false, Position = new Vector2(0, VH - 116), Size = new Vector2(VW, 116), MouseFilter = Control.MouseFilterEnum.Ignore };
        var face = new TextureRect { Position = new Vector2(14, 10), Size = new Vector2(88, 96),
                                     StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        var txt = new RichTextLabel { Position = new Vector2(116, 12), Size = new Vector2(VW - 132, 92),
                                      BbcodeEnabled = true, Text = "", MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(face); panel.AddChild(txt);
        Ui.AddChild(panel);

        var texWu = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_dialog_player.png");
        var texSu = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_dialog_npc.png");
        var faceWu = ResourceLoader.Load<Texture2D>("res://assets/textures/char_wuwu_face.png");
        int i = 0;
        void Show()
        {
            if (i >= lines.Length) { panel.Visible = false; done?.Invoke(); return; }
            var (who, text) = lines[i];
            panel.Visible = true;
            StyleBox sb;
            if (who != "")
            {
                sb = new StyleBoxTexture { Texture = who == "wu" ? texWu : texSu };
                face.Texture = who == "wu" ? faceWu : null;
                face.Visible = face.Texture != null;
            }
            else
            {
                sb = new StyleBoxFlat { BgColor = new Color(0.09f, 0.09f, 0.095f, 0.92f) };
                face.Visible = false;
            }
            panel.AddThemeStyleboxOverride("panel", sb);
            txt.Text = "[b]" + (who == "wu" ? "老吴" : who == "su" ? "苏航" : "") + "[/b]  " + text;
        }
        Show();
        void OnInput(InputEvent e)
        {
            if (!panel.Visible) return;
            if (e.IsActionPressed("ui_accept") || (e is InputEventMouseButton mb && mb.Pressed))
            { i++; Show(); }
        }
        _inputHandler = OnInput;
    }

    private Action<InputEvent> _inputHandler;

    // 真人反馈第二次踩同一坑：字幕只认键盘。点击必须同样能推进——
    // 字幕停留 >0.4s 后的点击先给字幕，不吃进选择热区。
    public override void _UnhandledInput(InputEvent e)
    {
        if (_subBg.Visible && e is InputEventMouseButton { Pressed: true } && _subTimer > 0.35f)
        {
            NextSub();
            return;
        }
        _inputHandler?.Invoke(e);
    }

    /// <summary>烟测/节奏调试用：-- shot 在第 100 帧存视口后退出。</summary>
    private int _shotF = -1;
    public bool TestSubVisible => _subBg.Visible;

    private void ArmShot()
    {
        if (Godot.OS.GetCmdlineUserArgs().Length > 0 &&
            System.Array.IndexOf(Godot.OS.GetCmdlineUserArgs(), "shot") >= 0) _shotF = 0;
    }

    // ── 调查选择（§5.2：光标放到东西上，不弹按钮）──────────────────────

    /// <summary>hotspot: 区域+一行提示+选中后果。全部展示后等待点击。</summary>
    protected void Choices(List<(Rect2 box, string tip, Action pick)> hs, Action onCancelled)
    {
        var marks = new List<Node2D>();
        foreach (var h in hs)
        {
            var o = new Polygon2D { Polygon = new[] { h.box.Position, new Vector2(h.box.End.X, h.box.Position.Y), h.box.End, new Vector2(h.box.Position.X, h.box.End.Y) },
                                    Color = new Color(1, 1, 1, 0f) };
            AddChild(o); marks.Add(o);
        }
        void OnInput(InputEvent e)
        {
            if (e is not InputEventMouseButton mb || !mb.Pressed) return;
            for (int i = 0; i < hs.Count; i++)
                if (hs[i].box.HasPoint(mb.Position))
                {
                    _inputHandler = null;
                    foreach (var m in marks) m.QueueFree();
                    hs[i].pick();
                    return;
                }
        }
        _inputHandler = OnInput;
    }

    protected void Sfx(string id) => AudioIndex.Sfx(id);
    protected void HoldStart(string id) => AudioIndex.StartHold(id);
    protected void HoldStop() => AudioIndex.StopHold();

    /// <summary>记一条线索（flag+账本行，剧本 §2：线索=登记本上老吴自己的一笔）。</summary>
    protected void Clue(string flagId, string ledgerText)
    {
        Save.Set(flagId);
        Save.Ledger.Add(new LedgerLine { Text = ledgerText, Source = Name });
        Save.Save();
    }
}
