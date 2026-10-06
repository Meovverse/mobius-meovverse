using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S11 · 第五章 死者韩湘：两个身份叠在一起。调查选择五（C=结局二）。</summary>
public partial class Ch05 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_archive_room");
        var a = new Panel { Position = new Vector2(90, 60), Size = new Vector2(200, 150) };
        a.AddChild(new Label { Position = new Vector2(16, 12), Text = "韩湘\n状态：死亡\n（多年前）" });
        var b = new Panel { Position = new Vector2(350, 60), Size = new Vector2(200, 150) };
        b.AddChild(new Label { Position = new Vector2(16, 12), Text = "韩梅\n状态：失踪\n2006 年" });
        Ui.AddChild(a); Ui.AddChild(b);

        Subs(() => Subs(Gate,
            "档案里冒出一个名字：韩湘——苏航早已去世的妹妹。",
            "可是\"韩湘\"的身份信息，在韩梅失踪之后重新出现过。",
            "旧身份证明、死亡资料、社会关系——被拿来处理本应属于韩梅的事情。",
            "有人把两个女人的身份混在了一起。",
            "一个可怕的猜想：韩梅可能已经死了。韩湘是替她造的假出口。"),
            "两份档案并排放着。");
    }

    private void Gate()
    {
        Choices(new List<(Rect2, string, System.Action)>
        {
            (new Rect2(120, 240, 160, 60), "韩湘的死亡证明 ↔ 失踪时间", A),
            (new Rect2(360, 240, 160, 60), "韩梅最后出现 ↔ \"韩湘\"出现", B),
            (new Rect2(250, 315, 140, 40), "认为韩湘就是韩梅（停）", C),
        }, null);
        Subs(null, "（两份档案之间，他先看哪条线？）");
    }

    private void A()
    {
        Save.SetChoice(5, 'A');
        Clue(RunState.Flag.Clue13, "韩湘死于韩梅失踪之前。");
        Clue(RunState.Flag.Clue17, "死亡证明：日期是真的。人是早年就没了的。");
        Go("死亡证明的日期，比韩梅失踪早得多。死人没法自己失踪。");
    }
    private void B()
    {
        Save.SetChoice(5, 'B');
        Clue(RunState.Flag.Clue14, "韩梅没有变成韩湘——是有人在她消失后用了韩湘。");
        Clue(RunState.Flag.Clue18, "韩梅的真实身份资料。两个人的底子对不上。");
        Go("韩梅最后一次刷卡是五月。\"韩湘\"第一次出现是六月。");
    }
    private void C()
    {
        Save.SetChoice(5, 'C'); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"),
            "结局二 · 被隐藏的身份",
            "最危险的伪造，不是造一个假人，而是让一个死人替活人承担身份。");
    }
    private void Go(string line) =>
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（回墓道。）");
}
