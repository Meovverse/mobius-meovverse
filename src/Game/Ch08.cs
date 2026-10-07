using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>S14 · 第八章：什么时候报警。关键选择七。</summary>
public partial class Ch08 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        Subs(() => DocChoices("东西都在。下一步，哪只手先动？",
            ("现在就去警局", "骑上车就走，材料抓一把算一把", Impulse),
            ("先把九样东西摆开", "排成一条时间线，再决定交给谁", Steady)),
        "证据都在。可它们还只是一堆纸。",
        "他还没有把它们交给警方。");
    }

    private void Impulse()
    {
        Save.Set(RunState.Flag.RushToPolice); Save.SetChoice(7, 'A'); Save.Save();
        EndingCard.Open(GetTree(), "结局四 · 冲动",
            "知道真相，不等于拥有证明真相的能力。第二天苏航的人进了城，散着的材料再也没能找到。");
    }
    private void Steady()
    {
        Save.Set("ch8_done"); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), "先不动。把桌子清空。");
    }
}
