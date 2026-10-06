using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S14 · 第八章：什么时候报警。关键选择七。</summary>
public partial class Ch08 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        Subs(() => Choices(new System.Collections.Generic.List<(Rect2, string, System.Action)>
        {
            (new Rect2(160, 240, 130, 60), "拿上登记本就骑车去警局", Impulse),
            (new Rect2(350, 240, 150, 60), "先把九样东西按时间摆开", Steady),
        }, null),
        "证据都在。可它们还只是一堆纸。",
        "他还没有把它们交给警方。");
    }

    private void Impulse()
    {
        Save.Set(RunState.Flag.RushToPolice); Save.SetChoice(7, 'A'); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"),
            "结局四 · 冲动",
            "知道真相，不等于拥有证明真相的能力。",
            "第二天，苏航的人进了城。剩下的材料，再也没能找到。");
    }
    private void Steady()
    {
        Save.Set("ch8_done"); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), "先不动。把桌子清空。");
    }
}
