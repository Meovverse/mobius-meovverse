using Godot;

namespace MoShi;

/// <summary>
/// 启动场景 / 全局入口。
///
/// 2026-10-06 重定：标题页**只是封面**——不放菜单、不放面板（真人反馈：
/// 菜单和封面烧字打架、点空白没有反馈）。任何点击/Enter = 翻过封面，
/// 进入下一屏《桌前的账本》（ResumePoint），**分支选择在那里**。
/// Esc 在标题退出游戏（无边框窗口没有 ×）。
/// </summary>
public partial class Boot : Control
{
    public const int ViewportWidth = 640;
    public const int ViewportHeight = 360;

    private ColorRect _white;
    private bool _leaving;
    private int _shotWait, _autoWait;
    private static SceneTreeTimer _e2eTimer;

    public bool TestLeaving => _leaving;

    public override void _Ready()
    {
        MoShi.Game.MenuHud.Ensure(GetTree(), null, false);   // 标题本身就是菜单
        // 复位渲染缓冲；运行期从不改 OS 窗口尺寸（丢焦教训见 git log）
        GetWindow().ContentScaleSize = new Vector2I(ViewportWidth, ViewportHeight);

        var w = GetWindow();
        w.FocusEntered += () => LogFocus("enter");
        w.FocusExited += () => { LogFocus("exit"); w.RequestAttention(); };

        AddChild(new ColorRect { Color = new Color(0, 0, 0), MouseFilter = MouseFilterEnum.Ignore,
                                 Size = new Vector2(ViewportWidth, ViewportHeight) });
        var titleTex = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title.png");
        if (titleTex != null)
            AddChild(new Sprite2D { Texture = titleTex, Centered = false, Scale = new Vector2(0.5f, 0.5f) });
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
        GD.Print("[boot] 点击封面进入");

        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot") >= 0) _shotWait = 20;
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

    public override void _Input(InputEvent e)
    {
        if (_leaving) return;
        if (e.IsActionPressed("ui_cancel")) { GetTree().Quit(); return; }
        bool click = e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left;
        if (click || e.IsActionPressed("ui_accept") || e is InputEventKey { Pressed: true })
            Leave("res://scenes/ResumePoint.tscn");
    }

    /// <summary>碎裂转场：音效起 → 画面与字同步闪烁、振幅递增 → 白场 → 进下一屏。</summary>
    private void Leave(string scenePath)
    {
        _leaving = true;
        // ★ 标题曲只属于封面：真人反馈进游戏后它还在响。转场起就 1.5s 淡出。
        Core.AudioIndex.StopTitle(1.5f);
        Core.AudioIndex.Sfx("sfx_stone_crack");
        var tw = CreateTween();
        float k = 0.06f;
        for (int i = 0; i < 6; i++)
        {
            k *= 1.35f;
            tw.TweenProperty(this, "modulate", new Color(1f + k * 0.6f, 1f + k * 0.6f, 1f + k * 0.6f), 0.05f);   // #18 红闪→白闪
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
            Leave("res://scenes/ResumePoint.tscn");
            _e2eTimer = GetTree().CreateTimer(8.0);
            _e2eTimer.Timeout += () =>
            {
                var tree = (SceneTree)Engine.GetMainLoop();
                GD.Print("[e2e] after  scene=" + tree.CurrentScene?.Name + " musicPlaying=" + MoShi.Core.AudioIndex.TestMusicPlaying);
                tree.Quit();
            };
        }
        if (_shotWait > 0 && --_shotWait == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng("res://data/gen/title_shot.png");
            GD.Print("[title] shot 已存");
            GetTree().Quit();
        }
    }
}
