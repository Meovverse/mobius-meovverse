using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S21 · 尾声 60 秒：新生意 → 「证明呢？」→ 两行日期 → 合上本子。</summary>
public partial class Epilogue : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        Dialogue(new[]
        {
            ("", "结案后某一天。有人进来，三十多岁："),
            ("", "\"师傅，给我妈刻块碑。\""),
            ("wu", "证明呢？"),
            ("", "——他没先问价钱。"),
        }, Close);
    }

    private void Close()
    {
        Sfx("sfx_pen_write");
        Save.Set("epilogue_done"); Save.Save();
        Subs(() => Subs(() => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"),
            "窗外天快黑了。门边立着一块新碑，字还没刻完。",
            "旧登记本，仍在他手边。"),
            "材料日期。刻字日期。他把本子合上。");
    }
}
