using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>S1 · 序章（约 60 秒）：2006 定景 + 对话 + 登记本。无分支。</summary>
public partial class ChPrologue : StorySceneBase
{
    public bool TestReady2 => true;
    protected override void SceneReady()
    {
        // 2006 铺子内景（程序版垫场，美术 A0 交付后 AssetIntake 自动顶掉）。
        // 分镜 S1 其实是"黑场里只亮一张登记卡"：内景压到近黑当底衬，
        // 亮部只留登记卡与字幕——上一版满屏木板纹才是"杂乱"的来源。
        Plate("bg_shop_interior");
        Black(0.88f);

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
        // 2026-10-07 交付的「账本单页」实拍纸：垫在登记文字下面
        var page = AssetIntake.Get("prop_ledger_page");
        if (page != null)
        {
            float pk = page.GetHeight() > 0 ? 300f / page.GetHeight() : 1f;
            AddChild(new Sprite2D { Texture = page, Centered = true, Position = new Vector2(VW / 2f, 170),
                                    Scale = new Vector2(pk, pk), ZIndex = 5 });
        }
        var txt = new Label
        {
            Position = new Vector2(VW / 2f - 100, 52), Size = new Vector2(200, 220),
            Text = "墓位号    A 区 7 号\n姓名      苏兰\n材料日期   2006-05-17\n刻字日期   2006-05-17\n经办人    老吴",
        };
        txt.AddThemeColorOverride("font_color", new Color(0.20f, 0.14f, 0.09f));   // 墨字写在纸上
        Ui.AddChild(txt);
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
