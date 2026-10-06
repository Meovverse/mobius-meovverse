using Godot;

namespace MoShi.Game;

/// <summary>S16 · 第十章 真相：纯演出（黑场 + 段落文本卡）。</summary>
public partial class Ch10 : StorySceneBase
{
    protected override void SceneReady()
    {
        Black();
        Subs(() => GetTree().ChangeSceneToFile("res://scenes/Ch11.tscn"),
            "苏航的公司欠下巨额债务。他提前为母亲买了高额保险，然后设计了一场事故。",
            "苏兰不是死于意外。真正的死亡日期是 2006 年 5 月 17 日——老吴照这个日期刻了碑。",
            "为了让保险材料成立，日期被改成 5 月 16 日。碑上的「17」被磨掉，重刻成「16」。",
            "但他没算到：登记本在老吴手里。系统没改干净。料单还在。石头记得。",
            "韩梅看见了这一切。所以她也没有回来。",
            "苏航用早已死去的妹妹韩湘的身份，替韩梅造了一个假出口——让「韩梅」从未死过。",
            "韩梅不是韩湘。韩梅是被杀害的。");
    }
}
