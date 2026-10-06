using Godot;

namespace MoShi.Game;

/// <summary>
/// 常驻 HUD（Citrate657 #5/#6）：右上角固定两样——当前章节名 + 「返回主菜单」。
///
/// 挂在 Root 上跨场景存活：各章节经 <see cref="StorySceneBase"/> 自动亮起，
/// 标题（Boot）与存档切换页（ResumePoint）自己熄灯——那两个本来就是菜单。
/// 返回不需要确认：本作"账本即存档"，每条线索落盘即保存，回标题=随时可续。
/// </summary>
public partial class MenuHud : CanvasLayer
{
    private static MenuHud _inst;
    public static MenuHud Instance => _inst;
    public string TestChapterText => _chap?.Text ?? "";
    public bool TestBackVisible => _back != null && _back.Visible && GodotObject.IsInstanceValid(_back);
    private Label _chap;
    private TextureButton _back;

    public static MenuHud Ensure(SceneTree tree, string chapter, bool show)
    {
        if (_inst == null || !GodotObject.IsInstanceValid(_inst))
        {
            _inst = new MenuHud { Layer = 90 };
            tree.Root.AddChild(_inst);
            _inst.Build();
        }
        _inst.Visible = show;
        if (show && chapter != null) _inst._chap.Text = chapter;
        return _inst;
    }

    private void Build()
    {
        _chap = new Label
        {
            Position = new Vector2(2, 4), Size = new Vector2(636, 18),
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = new Color(1, 1, 1, 0.7f),
        };
        _chap.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
        _chap.AddThemeConstantOverride("shadow_offset_x", 1);
        _chap.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_chap);

        // ≡ 三条横线（Citrate#8：样式就该简洁）。程序画的图标，不依赖主题。
        var icon = Image.CreateEmpty(34, 26, false, Image.Format.Rgba8);
        foreach (int ly in new[] { 5, 12, 19 })
            for (int x = 5; x < 29; x++) { icon.SetPixel(x, ly, new Color(1, 1, 1, 0.9f)); icon.SetPixel(x, ly + 1, new Color(1, 1, 1, 0.55f)); }
        _back = new TextureButton
        {
            TextureNormal = ImageTexture.CreateFromImage(icon),
            Position = new Vector2(602, 4),
            TooltipText = "返回主菜单",
        };
        _back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
        AddChild(_back);
    }

    public static string LabelFor(string marker)
    {
        if (marker.Contains("RPGExplo")) return "墓园 · 走访途中";
        return ChapterFlow.Label(marker);
    }
}
