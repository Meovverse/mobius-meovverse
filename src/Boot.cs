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
    private bool _leaving;

    // 真人测试期保留 F3；点击 = 开始游戏
    public override void _UnhandledInput(InputEvent e)
    {
        if (_leaving) return;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
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
        var tw2 = CreateTween();
        tw2.TweenCallback(Callable.From(() =>
            GetTree().ChangeSceneToFile(MoShi.Game.ChapterFlow.Next())));
    }

    public override void _Ready()
    {
        var bg0 = new ColorRect { Color = new Color(0f, 0f, 0f) };
        bg0.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg0);
        // 四批：正式标题画面（1280×720，覆盖到全屏）
        var titleTex = ResourceLoader.Load<Texture2D>("res://assets/textures/bg_title.png");
        if (titleTex != null)
        {
            var tr = new TextureRect { Texture = titleTex, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
            tr.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(tr);
        }
        // 白闪层：转场最后一棒
        _white = new ColorRect { Color = new Color(1, 1, 1, 0) };
        _white.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_white);

        var text = new Label
        {
            Position = new Vector2(24, 24),
            Size = new Vector2(400, 24),
            Text = "墓时",
        };
        AddChild(text);

        var info = new Label
        {
            Position = new Vector2(24, 54),
            Size = new Vector2(400, 24),
            Text = $"{ViewportWidth}×{ViewportHeight} · gl_compatibility · C#",
        };
        AddChild(info);

        var hint = new Label
        {
            Position = new Vector2(24, 114),
            Size = new Vector2(592, 24),
        };
        AddChild(hint);

        var fontStatus = new Label
        {
            Position = new Vector2(24, 84),
            Size = new Vector2(592, 24),
        };
        AddChild(fontStatus);

        var path = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        fontStatus.Text = ResourceLoader.Exists(path)
            ? $"字体已加载：{path.GetFile()}"
            : "字体未加载 —— 请把 fusion_pixel_12px.ttf 放到 assets/fonts/";

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
        hint.Text = "点击画面开始 ｜ F3 = 资产对账";
    }
}