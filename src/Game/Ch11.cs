using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S17 · 第十一章 最后的选择：交警方并保留原件=A；B/C 给一句冷话然后退回。</summary>
public partial class Ch11 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        Subs(Gate, "东西都齐了。怎么把它变成\"证据\"？");
    }

    // #33：原来用 Choices（隐形热区）——玩家看不到任何可点的东西，必然卡住。
    // 改成可见的资料卡（同其余章节），并有明确的问句指引。
    private void Gate() => DocChoices("证据怎么用，才既是真相、又拿得出手？",
        ("整套交警方，原件留手里", "把复印件交出去，能对得上的原件自己收着", A),
        ("先发到网上", "让所有人先看见，再谈别的", B),
        ("自己去找苏航", "当面摊牌，逼他给个说法", C));

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
