using Godot;

namespace MoShi.Dev;

/// <summary>
/// 把一个场景渲染几帧后截到 data/gen/scene_shot.png——用于肉眼验收 UI 排版。
/// headless 下截不了图，所以必须带窗口跑：
///   Godot.exe --path &lt;proj&gt; res://scenes/SceneShot.tscn -- scene=res://scenes/Ch09.tscn
/// </summary>
public partial class SceneShot : Node
{
    private int _frame;
    private bool _done;

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://data/gen/"));
        string scene = "res://scenes/Ch09.tscn";
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("scene=")) scene = a["scene=".Length..];

        var ps = ResourceLoader.Load<PackedScene>(scene);
        if (ps == null) { GD.PrintErr($"[shot] 找不到场景：{scene}"); GetTree().Quit(1); return; }
        if (scene.Contains("EndingCard"))   // 结局卡文本是静态字段，截图前先填
        {
            MoShi.Game.EndingCard.Title = "结局三 · 妥协";
            MoShi.Game.EndingCard.Theme = "知道真相，却选择沉默。";
        }
        AddChild(ps.Instantiate());
        GD.Print($"[shot] 场景＝{scene}");
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        if (++_frame < 12) return;
        _done = true;
        GetViewport().GetTexture().GetImage().SavePng("res://data/gen/scene_shot.png");
        GD.Print("[shot] data/gen/scene_shot.png 已存");
        GetTree().Quit();
    }
}
