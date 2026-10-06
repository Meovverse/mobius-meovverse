using System.Collections.Generic;
using Godot;

namespace MoShi;

/// <summary>
/// 启动场景 / 全局入口 + 标题菜单（存档系统的门面）。
///
/// 菜单就是普通 UI——"系统不出声"禁的是游戏内提示，标题画面是游戏外的。
/// 三条设计：
///   ① 有档必说清"继续去哪一章、账本多厚、上次何时"；
///   ② 抹账要二次确认（那是老吴二十年，不是一句 yes/no）；
///   ③ 续玩/新开局/退出是选项，不是快捷键彩蛋。
/// </summary>
public partial class Boot : Control
{
    public const int ViewportWidth = 640;
    public const int ViewportHeight = 360;

    private enum Screen { Menu, ConfirmWipe, Ledger }

    private ColorRect _white;
    private bool _leaving;
    private Screen _screen = Screen.Menu;
    private int _sel;
    private readonly List<(Label node, System.Action act)> _items = new();
    private Label _confirm1, _confirm2;
    private Label _ledgerBtn;
    private Panel _ledgerPanel;
    private Screen _confirmFrom = Screen.Menu;   // 删档确认后回哪儿
    private int _ledgerSel;
    private int _shotWait, _autoWait;
    private bool _openLedgerFirst;
    private static SceneTreeTimer _e2eTimer;

    public bool TestLeaving => _leaving;

    public override void _Ready()
    {
        // 复位渲染缓冲（运行期从不改 OS 窗口尺寸——丢焦教训见 git log）
        GetWindow().ContentScaleSize = new Vector2I(ViewportWidth, ViewportHeight);

        var w = GetWindow();
        w.FocusEntered += () => LogFocus("enter");
        w.FocusExited += () => { LogFocus("exit"); w.RequestAttention(); };

        AddChild(new ColorRect { Color = new Color(0, 0, 0), MouseFilter = MouseFilterEnum.Ignore,
                                 Size = new Vector2(ViewportWidth, ViewportHeight) });
        var titleTex = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title.png");
        if (titleTex != null)
            AddChild(new Sprite2D { Texture = titleTex, Centered = false, Scale = new Vector2(0.5f, 0.5f) });
        // 菜单区底衬：封面上烧了"開始遊戲/PRESS ANY KEY"，不压一层菜单字读不出。
        // （已反馈美术：终版封面最好去掉烧字，菜单是程序的事。）
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.42f),
            Position = new Vector2(0, 252), Size = new Vector2(ViewportWidth, 108),
            MouseFilter = MouseFilterEnum.Ignore });
        _white = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore,
                                 Size = new Vector2(ViewportWidth, ViewportHeight) };
        AddChild(_white);

        var path = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        Core.ProcGen.CachedFont = ThemeDB.FallbackFont;
        if (ResourceLoader.Exists(path))
            Core.ProcGen.CachedFont = ResourceLoader.Load<Font>(path);

        Core.SlotRegistry.Install();
        GD.Print(Core.SlotRegistry.Audit());
        foreach (var slot in Core.SlotRegistry.All)
            _ = Core.AssetIntake.Get(slot.Key);
        GD.Print(Core.AssetIntake.DumpReport());
        GD.Print(Core.AudioIndex.Audit());
        Callable.From(Core.AudioIndex.PlayTitle).CallDeferred();

        BuildMenu();

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot") >= 0) _shotWait = 20;
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot2") >= 0) { _shotWait = 60; _openLedgerFirst = true; }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "autostart") >= 0) _autoWait = 72;
    }

    private void LogFocus(string what)
    {
        var line = $"{Time.GetTicksMsec() / 1000.0:F2}s focus:{what}";
        GD.Print("[focus] " + line);
        try
        {
            using var f = Godot.FileAccess.Open("user://focus.log", Godot.FileAccess.ModeFlags.ReadWrite);
            if (f == null) return;
            var old = f.GetAsText();
            f.Seek(0); f.StoreString(line + "\n" + old.TrimEnd('\n'));
        }
        catch { /* 日志不许弄崩游戏 */ }
    }

    // ── 菜单构建 ────────────────────────────────────────────────────────

    private void BuildMenu()
    {
        bool has = Core.RunState.HasSave();
        var entries = new List<(string, string, System.Action)>();
        if (has)
        {
            var where = Game.ChapterFlow.Label(Game.ChapterFlow.Next());
            entries.Add(("继续　《" + where + "》　" + Core.RunState.Describe(), null,
                () => Leave(Game.ChapterFlow.Next())));
            entries.Add(("从头开始", null, null));   // null act=进确认页
        }
        else
            entries.Add(("开始", null, () => Leave("res://scenes/ChPrologue.tscn")));
        entries.Add(("退出", null, () => GetTree().Quit()));

        float y = has ? 262 : 262;
        foreach (var (label, sub, act) in entries)
        {
            var node = new Label
            {
                Position = new Vector2(0, y), Size = new Vector2(ViewportWidth, 26),
                HorizontalAlignment = HorizontalAlignment.Center, Text = label,
            };
            var item = (node, act);
            _items.Add(item);
            AddChild(node);
            if (sub != null)
            {
                var sl = new Label
                {
                    Position = new Vector2(0, y + 22), Size = new Vector2(ViewportWidth, 16),
                    HorizontalAlignment = HorizontalAlignment.Center, Text = sub,
                    Modulate = new Color(1, 1, 1, 0.45f),
                };
                AddChild(sl);
                y += 16;
            }
            y += 46;
        }
        // "从头开始"固定挂到确认页（第二个条目，如果有档）
        if (has)
        {
            var idx = _items.FindIndex(i => i.node.Text == "从头开始");
            var prev = _items[idx];
            _items[idx] = (prev.node, () => { _confirmFrom = Screen.Menu; _screen = Screen.ConfirmWipe; ShowConfirm(true); });
        }
        RefreshSel();

        // 角落入口：账本 = 存档面板（世界观说法；右下角，低调但找得到）
        _ledgerBtn = new Label
        {
            Position = new Vector2(560, 340), Size = new Vector2(72, 18),
            HorizontalAlignment = HorizontalAlignment.Right, Text = "账本 ▸",
            Modulate = new Color(1, 1, 1, 0.4f),
        };
        AddChild(_ledgerBtn);
    }

    private void OpenLedger()
    {
        _screen = Screen.Ledger;
        foreach (var (node, _) in _items) node.Visible = false;
        _ledgerBtn.Visible = false;
        _ledgerPanel?.QueueFree();
        _ledgerPanel = new Panel { Position = new Vector2(80, 40), Size = new Vector2(480, 280) };
        var has = Core.RunState.HasSave();
        var head = new Label { Position = new Vector2(20, 12), Size = new Vector2(440, 40), Text =
            has ? "账本 · " + Core.RunState.Describe() + "\n现在翻到：" + Game.ChapterFlow.Label(Game.ChapterFlow.Next())
                : "账本还是空的。" };
        _ledgerPanel.AddChild(head);
        if (has)
        {
            var st = Core.RunState.Load();
            int shown = 0;
            for (int i = st.Ledger.Count - 1; i >= 0 && shown < 5; i--, shown++)
                _ledgerPanel.AddChild(new Label
                {
                    Position = new Vector2(20, 64 + shown * 20), Size = new Vector2(440, 18),
                    Text = "· " + st.Ledger[i].Text,
                    ClipText = true,
                    Modulate = new Color(1, 1, 1, 0.8f),
                });
            if (st.Ledger.Count > 5)
                _ledgerPanel.AddChild(new Label { Position = new Vector2(20, 64 + 5 * 20),
                    Size = new Vector2(440, 18), Text = $"（前面还有 {st.Ledger.Count - 5} 行）",
                    Modulate = new Color(1, 1, 1, 0.45f) });
        }
        var btnWipe = new Label { Position = new Vector2(20, 236), Size = new Vector2(200, 22),
            Text = (_ledgerSel == 0 ? "▶ " : "　") + "合上账本（抹掉重来）" };
        var btnClose = new Label { Position = new Vector2(250, 236), Size = new Vector2(120, 22),
            Text = (_ledgerSel == 1 ? "▶ " : "　") + "◂ 回标题" };
        btnWipe.Name = "wipe"; btnClose.Name = "close";
        _ledgerPanel.AddChild(btnWipe); _ledgerPanel.AddChild(btnClose);
        AddChild(_ledgerPanel);
    }

    private void CloseLedger()
    {
        _screen = Screen.Menu;
        _ledgerPanel?.QueueFree();
        BuildMenu();
    }

    private void ShowConfirm(bool on)
    {
        _confirm1 ??= new Label { Position = new Vector2(0, 232), Size = new Vector2(ViewportWidth, 26),
            HorizontalAlignment = HorizontalAlignment.Center, Text = "账本上的每一行都要重抄一遍。确定合上它？" };
        _confirm2 ??= new Label { Position = new Vector2(0, 258), Size = new Vector2(ViewportWidth, 20),
            HorizontalAlignment = HorizontalAlignment.Center, Text = "Enter = 合上账本　　Esc = 不了",
            Modulate = new Color(1, 1, 1, 0.6f) };
        _confirm1.Visible = on; _confirm2.Visible = on;
        AddChild(_confirm1); AddChild(_confirm2);
        foreach (var (node, _) in _items) node.Visible = !on;
        _ledgerBtn.Visible = !on && _screen != Screen.Ledger;
        if (_ledgerPanel != null) _ledgerPanel.Visible = !on && _screen == Screen.Ledger;
    }

    private void RefreshSel()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var (node, _) = _items[i];
            bool on = i == _sel;
            node.Text = (on ? "▶ " : "　") + node.Text.TrimStart('▶', '　');
            node.Modulate = on ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.55f);
        }
    }

    private void ActivateSel()
    {
        var (_, act) = _items[_sel];
        act?.Invoke();
    }

    // ── 输入 ────────────────────────────────────────────────────────────

    public override void _Input(InputEvent e)
    {
        if (_leaving) return;

        // 角落「账本」：Tab 或点右下角
        if (_screen == Screen.Menu)
        {
            bool tab = e is InputEventKey tk && tk.Pressed && tk.Keycode == Key.Tab;
            bool clickBtn = e is InputEventMouseButton lb && lb.Pressed &&
                            _ledgerBtn.GetGlobalRect().HasPoint(lb.Position);
            if (tab || clickBtn) { OpenLedger(); return; }
        }
        if (_screen == Screen.Ledger)
        {
            if (e.IsActionPressed("ui_left") || e.IsActionPressed("ui_up")) { _ledgerSel = 0; OpenLedger(); }
            else if (e.IsActionPressed("ui_right") || e.IsActionPressed("ui_down")) { _ledgerSel = 1; OpenLedger(); }
            else if (e is InputEventMouseButton cb && cb.Pressed && _ledgerPanel != null)
            {
                foreach (var ch in _ledgerPanel.GetChildren())
                    if (ch is Label cl && cl.GetGlobalRect().HasPoint(cb.Position) && (cl.Name == "wipe" || cl.Name == "close"))
                    { _ledgerSel = cl.Name == "wipe" ? 0 : 1; }
                if (_ledgerSel == 1) { CloseLedger(); return; }
                // 点在面板空白：不响应，防误删
            }
            else if (e.IsActionPressed("ui_accept"))
            {
                if (_ledgerSel == 1) CloseLedger();
                else { _confirmFrom = Screen.Ledger; _screen = Screen.ConfirmWipe; ShowConfirm(true); }
            }
            else if (e.IsActionPressed("ui_cancel")) CloseLedger();
            return;
        }
        if (_screen == Screen.ConfirmWipe)
        {
            if (e.IsActionPressed("ui_accept"))
            {
                Core.RunState.DeleteSave();
                GD.Print("[title] 账本已合上，从头开始");
                Leave("res://scenes/ChPrologue.tscn");
            }
            else if (e.IsActionPressed("ui_cancel"))
            {
                ShowConfirm(false);
                if (_confirmFrom == Screen.Ledger) { _screen = Screen.Menu; OpenLedger(); }
                else { _screen = Screen.Menu; BuildMenu(); }
            }
            return;
        }

        if (e is InputEventKey { Pressed: true } k)
        {
            if (k.Keycode == Key.Up || e.IsActionPressed("ui_up")) { _sel = (_sel + _items.Count - 1) % _items.Count; RefreshSel(); }
            else if (k.Keycode == Key.Down || e.IsActionPressed("ui_down")) { _sel = (_sel + 1) % _items.Count; RefreshSel(); }
            else if (k.Keycode == Key.N && _items[_sel].node.Text.Contains("从头")) { _screen = Screen.ConfirmWipe; ShowConfirm(true); }
            else if (e.IsActionPressed("ui_accept") || k.Keycode == Key.Enter || k.Keycode == Key.Space) ActivateSel();
            else if (e.IsActionPressed("ui_cancel")) GetTree().Quit();
            return;
        }

        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].node.GetGlobalRect().HasPoint(mb.Position))
                {
                    _sel = i; RefreshSel(); ActivateSel();
                    return;
                }
            ActivateSel();   // 空白处点击 = 确认当前项（点击开始最直觉）
        }
    }

    // ── 转场（碎裂→闪烁→白场→进章）────────────────────────────────────

    private void Leave(string scenePath)
    {
        _leaving = true;
        Core.AudioIndex.Sfx("sfx_stone_crack");
        var tw = CreateTween();
        float k = 0.06f;
        for (int i = 0; i < 6; i++)
        {
            k *= 1.35f;
            tw.TweenProperty(this, "modulate", new Color(1 + k, 1 - k * 1.4f, 1 - k * 1.4f), 0.05f);
            tw.TweenProperty(this, "modulate", Colors.White, 0.05f);
            tw.TweenProperty(this, "position", new Vector2(k * 42, -k * 30), 0.04f);
            tw.TweenProperty(this, "position", Vector2.Zero, 0.04f);
        }
        tw.TweenProperty(_white, "color:a", 1f, 0.4f);
        tw.TweenInterval(0.5);
        tw.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(scenePath)));
    }

    public override void _Process(double delta)
    {
        if (_autoWait > 0 && --_autoWait == 0 && !_leaving)
        {
            GD.Print("[e2e] before window=" + GetWindow().Size);
            ActivateSel();   // autostart 走菜单当前项（无档=开始）
            _e2eTimer = GetTree().CreateTimer(8.0);
            _e2eTimer.Timeout += () =>
            {
                var tree = (SceneTree)Engine.GetMainLoop();
                GD.Print("[e2e] after  scene=" + tree.CurrentScene?.Name);
                tree.Quit();
            };
        }
        if (_openLedgerFirst && _shotWait == 20) OpenLedger();
        if (_shotWait > 0 && --_shotWait == 0)
        {
            if (_openLedgerFirst)
            { GetViewport().GetTexture().GetImage().SavePng("res://data/gen/title_ledger.png"); GD.Print("[title] ledger shot"); GetTree().Quit(); return; }
            GetViewport().GetTexture().GetImage().SavePng("res://data/gen/title_shot.png");
            GD.Print("[title] shot 已存");
            GetTree().Quit();
        }
    }
}
