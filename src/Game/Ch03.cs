using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S9 · 第三章 两个日期：五张纸并排 → 只有碑不同 → 调查选择三。</summary>
public partial class Ch03 : StorySceneBase
{
    protected override void SceneReady()
    {
        // 美术交付 `ch03_clues.png`（1920×1080，16:9）：自带背景 + 五个来源的日期。
        // 原来程序摆的底图/文本框/日期文字全部取消，直接满屏铺 640×360（1/3）。
        var clues = ResourceLoader.Load<Texture2D>("res://assets/textures/ch03_clues.png");
        if (clues != null)
        {
            // 收在**底部资料卡之上**（卡带 y=250..340）——五个日期全程可见，不被选项挡住。
            float k = 248f / clues.GetHeight();
            AddChild(new Sprite2D { Texture = clues, Centered = true,
                Position = new Vector2(VW / 2f, 124), Scale = new Vector2(k, k) });
        }
        else Plate("bg_shop_front");   // 缺图兜底

        Subs(() => Subs(Gate,
            "五个来源。四个日期是十七。",
            "只有墓碑上是十六。",
            "「17」被磨掉，重新刻成了「16」。",
            "为什么有人要改一天？"), "他把目前的资料并排放在一起。");
    }

    private void Gate()
    {
        DocChoices("为什么有人要改一天？答案在哪张纸上——",
            ("保险合同", "受益人是谁？日期和赔付有什么关系", A),
            ("事故报告：原件 ↔ 副本", "递进保险公司那份，日期对得上吗", B),
            ("去问苏航本人", "当面问，也许能要到说法", C));
    }

    private void A()
    {
        Save.SetChoice(3, 'A');
        Clue(RunState.Flag.Clue06, "受益人：苏航。");
        Clue(RunState.Flag.Clue07, "公司当年的债务数字，比保险金额小不了多少。");
        Clue(RunState.Flag.Clue08, "日期差一天，赔偿就是两回事。");
        Go("保险合同上有一条线：死亡日期决定赔付。");
    }
    private void B()
    {
        Save.SetChoice(3, 'B');
        Clue(RunState.Flag.Clue09, "提交给保险公司的材料：05-16。");
        Go("事故报告的副本里，日期被写成了五月十六日。原件不是。");
    }
    private void C()
    {
        Save.SetChoice(3, 'C');
        Save.Set(RunState.Flag.SuhangAlerted); Save.Save();
        Dialogue(new[]
        {
            ("su", "二十年前的东西，谁还记得那么清楚？"),
            ("su", "有些事情，过去了就是过去了。"),
            ("", "他没有得到答案。但苏航知道：老吴在重新查这件事。"),
        }, () => Go("（回去。）"));
    }

    private void Go(string line) =>
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（继续。）");
}
