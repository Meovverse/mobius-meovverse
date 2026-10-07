using System.Collections.Generic;
using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>S10 · 第四章 韩梅：账目发现 → 威胁 → 失踪。调查选择四。</summary>
public partial class Ch04 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_archive_room");
        Ambient("amb_archive");
        Raw("res://assets/textures/char_suteacher_portrait.png", new Vector2(96, 148), 208);  // 韩梅档案照：208px 高的一寸档照，立在卷宗上
        Subs(() => Subs(Gate,
            "2006 年 5 月，公司会计韩梅在账目里发现一笔奇怪的支出。和苏兰的保险有关。",
            "她继续往下查：保险、墓位、墓碑、资金流向——都发生在苏兰死之前。",
            "苏兰的\"意外\"，不是意外。",
            "她找到苏航：\"如果你不收手，我就把账目和保险合同一起交给警方。\"",
            "几天后，韩梅失踪。公司说她辞职了。她的家人从没收到过她的去向。"),
            "当年的会计，叫韩梅。");
    }

    private void Gate()
    {
        DocChoices("韩梅的抽屉拉开了一半。先查哪一摞？",
            ("她的账本 ↔ 苏航的账户", "铅笔线连到哪儿，钱就藏在哪儿", A),
            ("离职手续与个人物品清单", "真要走的人，杯子不会留在桌上", B),
            ("先放一放", "也许只是失踪。去查别的", C));
    }

    private void A()
    {
        Save.SetChoice(4, 'A');
        Clue(RunState.Flag.Clue10, "韩梅在查资金流向。");
        Clue(RunState.Flag.Clue11, "她手里握着苏航的秘密。");
        Go("账本边缘有铅笔线，一笔一笔连到苏航的个人账户。");
    }
    private void B()
    {
        Save.SetChoice(4, 'B');
        Clue(RunState.Flag.Clue12, "没有离职手续。桌上的东西没带走。");
        Go("离职单是空白的。她的杯子还在架子上。");
    }
    private void C()
    {
        Save.SetChoice(4, 'C');
        Save.Set(RunState.Flag.HanmeiLineDropped); Save.Save();
        Go("一名已经掌握苏航秘密的会计，在苏兰死后突然消失。这很可能不是另一件案子。");
    }
    private void Go(string line) =>
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（继续。）");
}
