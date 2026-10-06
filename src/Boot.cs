using Godot;

namespace MoShi;

/// <summary>
/// 启动场景 / 全局入口。
///
/// 职责（按实现顺序补齐）：
///   1. 持有 <see cref="Core.RunState"/>（账本即存档）
///   2. 章节点切换
///   3. 一次性音效总线
///
/// M0 阶段这里只做一件事：确认 640×360 + 中文字体这条管线是通的。
/// </summary>
public partial class Boot : Control
{
    public const int ViewportWidth = 640;
    public const int ViewportHeight = 360;

    private ColorRect _white;

    public override void _Process(double delta)
    {
        if (_shotWait > 0 && --_shotWait == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng("res://data/gen/title_shot.png");
            GD.Print("[title] shot 已存");
            GetTree().Quit();
        }
    }
    private bool _leaving;
    public bool TestLeaving => _leaving;

    // 真人测试期保留 F3；点击 = 开始游戏
    public override void _UnhandledInput(InputEvent e)
    {
        if (_leaving) return;
        if ((e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            || e.IsActionPressed("ui_accept"))
            Leave();
    }

    /// <summary>
    /// 转场（策划规格）：碎裂音起 → 画面与字同步闪烁、幅度递增 → 全白 → 淡出进游戏。
    /// 闪烁用 modulate 的色偏 + position 抖动，振幅每轮 ×1.35——"越来越大"是规格原话。
    /// </summary>
    private void Leave()
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
        // ★ 原来这里另起一条空 tween 做切场景——下一帧就执行，整个闪烁转场
        //   被跳过。收进同一条链尾：白场站稳 0.5s 再切。
        tw.TweenInterval(0.5);
        tw.TweenCallback(Callable.From(() =>
            GetTree().ChangeSceneToFile(MoShi.Game.ChapterFlow.Next())));
    }

    private int _shotWait;

    public override void _Ready()
    {
        // 运行期**绝不碰 OS 窗口尺寸**（真人反馈：点标题丢焦、之后点键全无——
        //   切换场景时重设窗口大小是根因）。只复位渲染缓冲：项目里所有场景
        //   统一 640×360 视口 + 1280×720 固定窗口；RPG 的宽视野靠相机 zoom 做。
        GetWindow().ContentScaleSize = new Vector2I(ViewportWidth, ViewportHeight);

        var bg0 = new ColorRect { Color = new Color(0f, 0f, 0f), MouseFilter = MouseFilterEnum.Ignore };
        bg0.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg0);
        // 标题画面：1280×720 用 Sprite2D 精确半倍铺满 640×360 视口。
        //   不用 TextureRect：锚点预置在入树前 size 是 0，只露左上角（"显示不全"的根因）。
        //   背景一律 MouseFilter=Ignore——Control 默认 Stop 会吞点击（"单击无反应"的根因）。
        var titleTex = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title.png");
        if (titleTex != null)
            AddChild(new Sprite2D { Texture = titleTex, Centered = false, Scale = new Vector2(0.5f, 0.5f) });
        _white = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        _white.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_white);

        var path = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();


        // ★ 槽位登记表：美术要做的只剩 6 张，其余全部程序生成。
        //   这是 doc/美术需求.md 的机器可读版本，两边不一致时以代码为准。
        Core.ProcGen.CachedFont = ThemeDB.FallbackFont;
        if (ResourceLoader.Exists(path))
            Core.ProcGen.CachedFont = ResourceLoader.Load<Font>(path);

        Core.SlotRegistry.Install();
        GD.Print(Core.SlotRegistry.Audit());

        // 试取全部槽位，验证没有一张会返回 null，并看看 AssetIntake 修正了什么
        foreach (var slot in Core.SlotRegistry.All)
            _ = Core.AssetIntake.Get(slot.Key);
        GD.Print(Core.AssetIntake.DumpReport());

        // ★ 音频验收：交付了什么、超没超档、还缺哪几条（doc/音频岗需求.md 的机器版对账）
        GD.Print(Core.AudioIndex.Audit());

        // 标题曲——全篇唯一允许响音乐的地方。延后一拍：Audio 宿主节点自己也是 call_deferred 挂的树
        Callable.From(Core.AudioIndex.PlayTitle).CallDeferred();

        // 验收截图钩子：-- shot 存标题画面后退出
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "shot") >= 0)
            _shotWait = 20;
        GD.Print(ResourceLoader.Exists(path) ? $"[boot] 字体：{path.GetFile()}" : "[boot] 字体未加载！");
        GD.Print("[boot] 点击画面开始");
    }
}