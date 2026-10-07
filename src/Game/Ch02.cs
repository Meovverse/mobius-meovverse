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
        Ambient("amb_office_night");
        Clue(RunState.Flag.Clue02, "墓园系统：苏兰／材料 05-17／刻字 05-17／经办 老吴。");

        var screen = new Panel { Position = new Vector2(180, 60), Size = new Vector2(300, 180), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
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
        // 2026-10-07 新交付的「购买凭证」实拍文档（替代程序版 deed）
        var tex = AssetIntake.Get("prop_purchase_receipt");
        float k = tex.GetHeight() > 0 ? 306f / tex.GetHeight() : 1f;
        var deed = new Sprite2D { Texture = tex, Centered = true, Position = new Vector2(VW / 2f, 178),
                                  Scale = new Vector2(k, k), Modulate = new Color(1, 1, 1, 0.97f) };
        AddChild(deed); deed.Visible = false;
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
        // 调查选择二：摆成桌上的三份资料（隐形热区已被投诉两次）
        DocChoices("他想接着往下查。桌上摊着三份东西。",
            ("医院与殡仪馆的原始记录", "和系统并排——遗体到底哪天接走的", () => Pick('A')),
            ("墓位购买记录", "量一量：买墓位和死亡之间隔了多久", () => Pick('B')),
            ("只信墓碑", "家属留下的“正式信息”。合上抽屉", () => Pick('C')));
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
                if (Save.EndingOneFired)
                {
                    EndingCard.Open(GetTree(), "结局一 · 没发现碑的问题", "真相就在眼前，但你没有看见。");
                    return;
                }
                // Citrate#44：冷话先说完、再重新摆资料卡——否则字幕会被卡片压住
                Subs(Gate, "你现在相信的\"事实\"，可能正是凶手希望你相信的。");
                return;
        }
    }

    private void End(string line)
    {
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()), line, "（继续。）");
    }
}
