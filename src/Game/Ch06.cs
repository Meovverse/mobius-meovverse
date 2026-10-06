using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S12 · 第六章 石料：料单。无分支，一条线索。</summary>
public partial class Ch06 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        var slip = Plate("prop_deed_ledger", true); slip.Position = new Vector2(0, 120);
        slip.AddChild(new Label2D { Position = new Vector2(210, 170), Text = "山西黑  1200×600×80  12 块\n到货：2006-04-02" });
        Subs(() =>
        {
            Clue(RunState.Flag.Clue15, "料单：到货 04-02；一块退单，几天后转到 A 区 7 号。");
            Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()),
                "这块石头，四月初就在等一个还没死的人。",
                "提前买墓位、准备保险、准备墓碑——不是临时起意。",
                "所有人都相信墓碑上的日期是真的。",
                "只有老吴知道：石头上留下的痕迹，比纸上的字更难伪造。",
                "（回家。桌上该摊开算了。）");
        }, "他把 2006 年的料单翻出来。");
    }
}

/// <summary>碑上/纸上贴一行的最简世界内文字。</summary>
public partial class Label2D : Label { public Label2D() { MouseFilter = MouseFilterEnum.Ignore; } }
