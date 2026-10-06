using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S17 · 第十一章 最后的选择：交警方并保留原件=A；B/C 给一句冷话然后退回。</summary>
public partial class Ch11 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        Gate();
        Subs(null, "东西都齐了。怎么把它变成\"证据\"？");
    }

    private void Gate() => Choices(new System.Collections.Generic.List<(Rect2, string, System.Action)>
    {
        (new Rect2(60, 250, 160, 60), "整套交警方，原件留手里", A),
        (new Rect2(250, 250, 140, 60), "先发到网上", B),
        (new Rect2(420, 250, 140, 60), "自己去找苏航", C),
    }, null);

    private void A()
    {
        Save.Set(RunState.Flag.EvidenceFixed); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()),
            "复印件交出去，登记本贴身放。",
            "（清明那天，安和园有人群。）");
    }
    private void B()
    {
        Subs(() => Gate(), "公开真相和证明真相，是两件不同的事。");
    }
    private void C()
    {
        Subs(() => Gate(), "最危险的不是没发现凶手——是证据不足时，让凶手知道你发现了他。");
    }
}
