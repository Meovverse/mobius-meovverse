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
    private int _target = 12;
    private int _subs;         // subs=N：先推进 N 条字幕再截
    private int _advanced;
    private float _clock;
    private Godot.Node _root;

    public override void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://data/gen/"));
        string scene = "res://scenes/Ch09.tscn";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("scene=")) scene = a["scene=".Length..];
            else if (a.StartsWith("frames=")) int.TryParse(a["frames=".Length..], out _target);
            else if (a.StartsWith("subs=")) int.TryParse(a["subs=".Length..], out _subs);
        }
        if (_subs > 0) _target = _subs * 30 + 40;

        var ps = ResourceLoader.Load<PackedScene>(scene);
        if (ps == null) { GD.PrintErr($"[shot] 找不到场景：{scene}"); GetTree().Quit(1); return; }
        if (scene.Contains("EndingCard"))   // 结局卡文本是静态字段，截图前先填
        {
            MoShi.Game.EndingCard.Title = "结局三 · 妥协";
            MoShi.Game.EndingCard.Theme = "知道真相，却选择沉默。";
        }
        if (scene.Contains("Ch13"))   // 十三章要求真结局前提，用临时存档喂满条件
        {
            MoShi.Core.RunState.SavePath = "user://shot_ledger.json";
            var st = new MoShi.Core.RunState();
            st.Set("clue_16"); st.Set("stone_altered"); st.Set("identity_swap"); st.Set("evidence_fixed");
            st.Save();
        }
        _root = ps.Instantiate();
        AddChild(_root);
        GD.Print($"[shot] 场景＝{scene}");
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _frame++;
        if (_subs > 0)
        {
            _clock += (float)delta;
            if (_advanced < _subs && _clock > 0.55f && _root is MoShi.Game.StorySceneBase sb)
            { sb.TestAdvance(); _advanced++; _clock = 0; }
            else if (_advanced >= _subs && _clock > 0.5f) Capture();
            return;
        }
        if (_frame < _target) return;
        Capture();
    }

    private void Capture()
    {
        if (_done) return;
        _done = true;
        GetViewport().GetTexture().GetImage().SavePng("res://data/gen/scene_shot.png");
        GD.Print("[shot] data/gen/scene_shot.png 已存");
        GetTree().Quit();
    }
}
