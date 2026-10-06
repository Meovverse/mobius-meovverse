using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S10 · 第四章 韩梅：账目发现 → 威胁 → 失踪。调查选择四。</summary>
public partial class Ch04 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_archive_room");
        Raw("res://assets/textures/char_suteacher_portrait.png", new Vector2(120, 150));  // 已改作韩梅档案照
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
        Choices(new List<(Rect2, string, System.Action)>
        {
            (new Rect2(80, 250, 150, 60), "调她的账本，对苏航的账户", A),
            (new Rect2(250, 250, 150, 60), "查她的离职手续和物品", B),
            (new Rect2(430, 250, 150, 60), "先放一放，查别的", C),
        }, null);
        Subs(null, "（他把手放在哪一份上？）");
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
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（回墓道。）");
}
