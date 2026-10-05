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

    public override void _Ready()
    {
        // 整屏纯黑。全篇没有 BGM，黑屏本身就是这个游戏的第一帧。
        var bg = new ColorRect { Color = new Color(0f, 0f, 0f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var text = new Label
        {
            Position = new Vector2(24, 24),
            Size = new Vector2(400, 24),
            Text = "墓石",
        };
        AddChild(text);

        var info = new Label
        {
            Position = new Vector2(24, 54),
            Size = new Vector2(400, 24),
            Text = $"{ViewportWidth}×{ViewportHeight} · gl_compatibility · C#",
        };
        AddChild(info);

        var fontStatus = new Label
        {
            Position = new Vector2(24, 84),
            Size = new Vector2(592, 24),
        };
        AddChild(fontStatus);

        // 字体是否落地，是 M0 唯一的验收项，所以直接报在屏幕上。
        var path = ProjectSettings.GetSetting("gui/theme/custom_font").AsString();
        fontStatus.Text = ResourceLoader.Exists(path)
            ? $"字体已加载：{path.GetFile()}"
            : "字体未加载 —— 请把 fusion_pixel_12px.ttf 放到 assets/fonts/";
    }
}