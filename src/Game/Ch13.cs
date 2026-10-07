using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>S19+S20 · 第十三章 + 真结局文本卡。</summary>
public partial class Ch13 : StorySceneBase
{
    protected override void SceneReady()
    {
        // 同上：真结局文字卡也垫墓园 A 区定景（压暗），不空黑
        Plate("bg_graveyard_area_A");
        Black(0.86f);
        var s = RunState.Load();
        if (!s.TrueEndingReady)   // 兜底路由：条件不齐不该走到这
        {
            GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
            return;
        }
        Subs(() =>
        {
            Save.Set("ch13_done"); Save.Save();
            Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()),
                "—— 真结局 · 石头不会撒谎 ——",
                "苏航：制造事故杀害母亲 · 骗取保险 · 杀害韩梅 · 伪造身份 · 隐瞒犯罪。数罪并罚。",
                "债务没有因为一笔赔偿消失。韩梅的家人知道了真相。",
                "那块碑被保留下来，作为案卷的一部分。",
                "老吴没有成为英雄。他回铺子，继续刻碑。");
        },
        "警方到场。老吴把整套材料交出去——原件还在他手里。",
        "重新调查从保险理赔倒推回那场\"事故\"。",
        "突破口是韩湘的旧死亡证明：一个死人，替一个活人\"死\"了二十年。",
        "韩梅不是韩湘。韩梅被杀害了。");
    }
}
