using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S1 · 序章（约 60 秒）：2006 定景 + 对话 + 登记本。无分支。</summary>
public partial class ChPrologue : StorySceneBase
{
    protected override void SceneReady()
    {
        // 2006：铺子内景（程序版暖一档的垫子，美术 A0 交付前用它）
        Plate("bg_title_shopfront");
        Raw("res://assets/textures/stele_A7_full.png", new Vector2(470, 210));   // 蒙布前那块碑的位置感

        Subs(() => Dialogue(new[]
        {
            ("", "2006 年 5 月。老吴石刻。"),
            ("su", "师傅，给我妈刻块碑。山西黑，手工，宋体，字口两毫米。"),
            ("wu", "怎么没的。"),
            ("su", "意外。"),
            ("", "老吴没有多问。他只是按照材料刻字。"),
        }, ShowLedger), "这是一桩二十年前的案子。");
    }

    private void ShowLedger()
    {
        var card = new Panel { Position = new Vector2(150, 40), Size = new Vector2(340, 240) };
        card.AddChild(new Label
        {
            Position = new Vector2(28, 26),
            Text = "墓位号    A 区 7 号\n姓名      苏兰\n材料日期   2006-05-17\n刻字日期   2006-05-17\n经办人    老吴",
        });
        Ui.AddChild(card);
        Sfx("sfx_pen_write");
        Subs(() =>
        {
            Clue(RunState.Flag.Clue16, "材料日期 05-17 ／ 刻字日期 05-17。两行同一个日期。");
            Save.Set(RunState.Flag.LedgerP6);
            Save.Set("prologue_done");
            Save.Save();
            Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Entry2026),
                 "他把本子合上。这一天的活结束了。",
                 "二十年后。——",
                 "安和园 A 区 7 号，售后回访。");
        }, "他写下两行日期。同一个日期。");
    }
}
