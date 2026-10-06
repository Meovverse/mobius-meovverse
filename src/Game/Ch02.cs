using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S8 · 第二章 查档：CRT 系统 → 日期矛盾 → 墓位成交 → 调查选择二。</summary>
public partial class Ch02 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_office_desk");
        HoldStart("amb_office_night");
        Clue(RunState.Flag.Clue02, "墓园系统：苏兰／材料 05-17／刻字 05-17／经办 老吴。");

        var screen = new Panel { Position = new Vector2(180, 60), Size = new Vector2(300, 180), Visible = false };
        screen.AddChild(new Label { Position = new Vector2(18, 14),
            Text = "> A区7号\n\n姓名：苏兰\n材料日期：2006-05-17\n刻字日期：2006-05-17\n经办人：老吴" });
        Ui.AddChild(screen);

        Subs(() =>
        {
            screen.Visible = true; Sfx("sfx_pen_write");
            Subs(() => Subs(ShowDeed,
                 "墓碑上是 05-16。系统里是 05-17。",
                 "这意味着至少有一样东西，被人为改过。"), "系统没被改。碑被改了。");
        }, "他输入：A 区 7 号。");
    }

    private void ShowDeed()
    {
        var deed = Plate("prop_deed_ledger", true); deed.Position = new Vector2(0, 120); deed.Modulate = new Color(1, 1, 1, 0.95f);
        deed.Visible = false;
        Subs(() =>
        {
            deed.Visible = true;
            Subs(Gate, "A 区 7 号 · 成交日期：2006 年 4 月 26 日",
                 "比苏兰真正死亡的那天，早了二十一天。",
                 "苏航在母亲还活着的时候，就买好了墓位。");
        }, "他又调出墓位购买记录。");
    }

    private void Gate()
    {
        // 调查选择二：三个都能"放到光标上"的东西
        Choices(new List<(Rect2, string, System.Action)>
        {
            (new Rect2(120, 30, 150, 60), "把医院和殡仪馆的记录调出来，并排", () => Pick('A')),
            (new Rect2(400, 140, 150, 60), "量一量：成交和死亡之间隔了多久", () => Pick('B')),
            (new Rect2(520, 300, 110, 52), "相信墓碑（离开）", () => Pick('C')),
        }, null);
        Subs(null, "（光标放在哪一样上，就在查哪一样。）");
    }

    private void Pick(char c)
    {
        Save.SetChoice(2, c); Save.Save();
        switch (c)
        {
            case 'A':
                Clue(RunState.Flag.Clue03, "医院最初记录：2006-05-17 死亡。");
                Clue(RunState.Flag.Clue04, "殡仪馆最初火化记录：2006-05-17。");
                End("两份最原始的记录，都是五月十七日。");
                break;
            case 'B':
                Clue(RunState.Flag.Clue05, "墓位成交在死亡前 21 天。");
                Sfx("sfx_caliper_lock");
                End("他量了两行日期之间的跨度。二十一天。");
                break;
            default:
                End(Save.EndingOneFired
                    ? "结局一 · 没发现碑的问题——真相就在眼前，但你没有看见。"
                    : "你现在相信的\"事实\"，可能正是凶手希望你相信的。");
                if (Save.EndingOneFired) { GetTree().ChangeSceneToFile("res://scenes/ChapterOne.tscn"); return; }
                Gate();   // 没触发结局一：退回重选（系统不出声，只把光标还给你）
                return;
        }
    }

    private void End(string line)
    {
        HoldStop();
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（回墓道。）");
    }
}
