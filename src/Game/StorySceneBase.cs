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
        // 取自己的场景路径（代码实例化时为空 → 用类名兜底，测试里也能断言到章名）
        var sf = SceneFilePath;
        MenuHud.Ensure(GetTree(),
            MenuHud.LabelFor(string.IsNullOrEmpty(sf) ? GetType().Name + ".tscn" : sf), true);
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
        if (_subs.Count == 0)
        {
            _subBg.Visible = false;
            var d = _onSubDone; _onSubDone = null;
            d?.Invoke();
            return;
        }
        _sub.Text = _subs.Dequeue();
        _subBg.Visible = true;
        _subTimer = 0;
    }

    /// <summary>系统静默：字幕只在停留够久或点击后推进，绝不自动跳红字。</summary>
    public override void _Process(double dt)
    {
        DocHover();
        DocKeys();
        if (_shotF >= 0)
        {
            _shotF++;
            if (_shotF % 24 == 0)   // 截图模式：直接走推进函数（ParseInputEvent 在
            {                       // _Process 里时序不可靠——真机验证过不生效）
                if (_subBg.Visible) NextSub(); else _advanceHook?.Invoke();
            }
            if (_shotF == 420)
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
        ClearDocs();
        var panel = new Panel { Visible = false, Position = new Vector2(0, VH - 116), Size = new Vector2(VW, 116), MouseFilter = Control.MouseFilterEnum.Ignore };
        TestPanel = panel;
        // ★ ExpandMode 必须 Ignore：默认 KeepSize 会让控件涨到纹理原始尺寸
        //   （交付人像 512×768），这就是"头像大大超出界面、文字被盖没"的根因。
        // Citrate#10：头像放大，头的上端要高过文本框上沿——所以让头像向上
        // "探出"面板（Panel 不裁子节点），并整体左置，正文从它右侧开始，互不遮挡。
        var face = new TextureRect { Position = new Vector2(8, -80), Size = new Vector2(132, 178),
                                     ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                                     StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                                     MouseFilter = Control.MouseFilterEnum.Ignore };
        TestFace = face;
        var txt = new RichTextLabel { Position = new Vector2(150, 12), Size = new Vector2(VW - 168, 92),
                                      BbcodeEnabled = true, Text = "", MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddChild(face); panel.AddChild(txt);
        Ui.AddChild(panel);

        // Citrate#11 四种文本框的归属：
        //   旁白    = 无花边、白底   → ui_dialog_npc（中性白条）
        //   主角老吴 = 有花边、黄底   → ui_frame_yellow_lace
        //   其他人物 = 有花边、白底   → ui_frame_white_lace（由 ui_frame_lace 蓝改白派生）
        var texNarrate = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_dialog_npc.png");
        var texWu = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_frame_yellow_lace.png");
        var texOther = ResourceLoader.Load<Texture2D>("res://assets/textures/ui_frame_white_lace.png");
        var faceWu = ResourceLoader.Load<Texture2D>("res://assets/textures/char_wuwu_face.png");
        var faceSu = ResourceLoader.Load<Texture2D>("res://assets/textures/char_suhang.png");
        int i = 0;
        void Show()
        {
            if (i >= lines.Length) { panel.Visible = false; done?.Invoke(); return; }
            var (who, text) = lines[i];
            panel.Visible = true;
            // Scale 拉伸：StyleBoxTexture 默认按纹理原始尺寸画会只占一小截。
            var st = new StyleBoxTexture { Texture = who == "wu" ? texWu
                                                       : who == "" ? texNarrate : texOther };
            st.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch;
            st.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
            panel.AddThemeStyleboxOverride("panel", st);
            // 人物才有头像；旁白不占位（正文左移，不留空框）
            face.Texture = who == "wu" ? faceWu : who == "su" ? faceSu : null;
            face.Visible = face.Texture != null;
            txt.Position = new Vector2(face.Visible ? 150 : 24, 12);
            txt.Size = new Vector2(VW - (face.Visible ? 168 : 48), 92);
            // #15：人物对话框是浅底（黄/白），文字必须用墨色；旁白是灰底用亮字。
            bool lightBox = who != "";
            txt.AddThemeColorOverride("default_color",
                lightBox ? new Color(0.11f, 0.10f, 0.09f) : new Color(0.92f, 0.92f, 0.90f));
            txt.Text = "[b]" + (who == "wu" ? "老吴" : who == "su" ? "苏航" : "") + "[/b]  " + text;
        }
        void Advance() { i++; Show(); }
        _advanceHook = Advance;
        Show();
        void OnInput(InputEvent e)
        {
            if (!panel.Visible) return;
            if (e.IsActionPressed("ui_accept") || (e is InputEventMouseButton mb && mb.Pressed))
                Advance();
        }
        _inputHandler = OnInput;
    }

    private Action<InputEvent> _inputHandler;
    private System.Action _advanceHook;   // Dialogue 装的"下一句"钩子，shot 模式用
    internal Control TestPanel, TestFace; // 回归断言用（对话布局几何）
    internal void TestAdvance() { if (_subBg.Visible && _subTimer > 0.35f) NextSub(); else _advanceHook?.Invoke(); }

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

    // ── 资料卡选择（#3#4#5：纯文字罗列+隐形热区 = 玩家不知道该点哪）────
    private sealed class Doc { public Panel Root; public StyleBoxFlat Sb; public Rect2 Box; public Action Pick; }
    private readonly System.Collections.Generic.List<Doc> _docs = new();
    private Label _choiceHint;

    /// <summary>把分支画成桌上的资料：每份一张纸卡（标题+一句"做什么"），
    /// 悬停微微提亮，点击即选。选择前桌上就摆着这些——点哪里一目了然。</summary>
    protected void DocChoices(string hint, params (string title, string sub, Action pick)[] docs)
    {
        ClearDocs();
        if (_choiceHint == null)
        {
            _choiceHint = new Label { Position = new Vector2(16, 6), Size = new Vector2(560, 18),
                Modulate = new Color(1, 1, 1, 0.75f) };
            _choiceHint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
            Ui.AddChild(_choiceHint);
        }
        _choiceHint.Text = hint;

        int n = docs.Length; float gap = 12f, w = 640f - 48 - gap * (n - 1);
        // 上移到 166..290：不压底部字幕条（306 起），也不挡中景
        float cw = w / n, y = 166, ch = 124;
        _docSel = 0;
        for (int i = 0; i < n; i++)
        {
            var sb = new StyleBoxFlat { BgColor = new Color(0.145f, 0.14f, 0.135f, 0.97f),
                BorderColor = new Color(0.62f, 0.58f, 0.5f, 0.5f) };
            foreach (var side in new[] { Side.Left, Side.Right, Side.Top, Side.Bottom }) sb.SetBorderWidth(side, 1);
            var card = new Panel { Position = new Vector2(24 + i * (cw + gap), y), Size = new Vector2(cw, ch),
                MouseFilter = Control.MouseFilterEnum.Ignore };
            card.AddThemeStyleboxOverride("panel", sb);
            // #14：标题长于卡宽会溢出——自动换行 + 裁切兜底
            var tl = new Label { Position = new Vector2(10, 8), Size = new Vector2(cw - 20, 34),
                Text = docs[i].title, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                ClipText = true };
            card.AddChild(tl);
            card.AddChild(new Label { Position = new Vector2(10, 44), Size = new Vector2(cw - 20, ch - 54),
                Text = docs[i].sub, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(1, 1, 1, 0.55f) });
            // 纸屑感：左上角一道浅色"纸边"
            card.AddChild(new ColorRect { Color = new Color(0.72f, 0.68f, 0.55f, 0.16f),
                Position = new Vector2(0, 0), Size = new Vector2(cw, 4) });
            Ui.AddChild(card);
            _docs.Add(new Doc { Root = card, Sb = sb, Box = new Rect2(24 + i * (cw + gap), y, cw, ch), Pick = docs[i].pick });
        }
        _inputHandler = OnDocInput;
        if (_choiceHint != null) _choiceHint.Text = hint + "　（←→ 选 · Enter 确认 · 或直接点卡片）";
    }

    private int _docSel;
    private void PickDoc(int i)
    {
        if (i < 0 || i >= _docs.Count) return;
        var act = _docs[i].Pick;
        ClearDocs();
        act.Invoke();
    }

    private void OnDocInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb) return;
        for (int i = 0; i < _docs.Count; i++)
            if (_docs[i].Box.HasPoint(mb.Position)) { PickDoc(i); return; }
    }

    protected void ClearDocs()
    {
        foreach (var d in _docs) { d.Root.Visible = false; d.Root.QueueFree(); }   // #13：立刻消失，别压着字幕
        _docs.Clear();
        _inputHandler = null;
        if (_choiceHint != null) _choiceHint.Text = "";
    }

    /// <summary>悬停提亮（每帧一次鼠标位，成本可忽略）。</summary>
    private void DocHover()
    {
        if (_docs.Count == 0) return;
        var m = GetGlobalMousePosition();
        for (int i = 0; i < _docs.Count; i++)
        {
            var d = _docs[i];
            bool on = d.Box.HasPoint(m) || i == _docSel;
            d.Sb.BgColor = on ? new Color(0.2f, 0.195f, 0.185f, 0.98f)
                              : new Color(0.145f, 0.14f, 0.135f, 0.97f);
        }
    }

    /// <summary>键盘操作资料卡（#16：点击若被系统吞，←→+Enter 兜底）。</summary>
    private void DocKeys()
    {
        if (_docs.Count == 0) return;
        if (Input.IsActionJustPressed("ui_left")) { _docSel = (_docSel + _docs.Count - 1) % _docs.Count; }
        else if (Input.IsActionJustPressed("ui_right")) { _docSel = (_docSel + 1) % _docs.Count; }
        else if (Input.IsActionJustPressed("ui_accept")) PickDoc(_docSel);
    }

    public int TestDocCount => _docs.Count;
    public void TestClickDoc(int i) => PickDoc(i);

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
