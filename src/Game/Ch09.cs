using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S15 · 第九章 真相拼合：按时间把证据点上去（对的按顺序亮起，系统不夸你）。</summary>
public partial class Ch09 : StorySceneBase
{
    private readonly List<(Label node, string when)> _order = new();
    private int _next;

    private static readonly (string when, string what)[] Items =
    {
        ("04-02", "山西黑到货"), ("04-26", "买墓位（苏兰还活着）"),
        ("05-17", "苏兰死亡 · 医院/殡仪馆/登记本 同一天"),
        ("5月后", "保险材料改成 05-16 · 碑上「17」磨成「16」"),
        ("同年", "韩梅发现一切 → 威胁报警 → 失踪"),
        ("失踪后", "韩湘的旧身份重新出现"),
    };

    protected override void SceneReady()
    {
        Plate("bg_shop_front");
        var scatter = new Rect2[]
        {
            new(380, 200, 150, 34), new(90, 90, 150, 34), new(300, 60, 170, 34),
            new(120, 220, 160, 34), new(420, 110, 150, 34), new(180, 150, 150, 34),
        };
        for (int i = 0; i < Items.Length; i++)
        {
            int idx = i;
            var lb = new Label { Text = "？", Position = new Vector2(scatter[i].Position.X, scatter[i].Position.Y),
                                 Size = new Vector2(scatter[i].Size.X, 30), MouseFilter = Control.MouseFilterEnum.Stop };
            Ui.AddChild(lb);
            lb.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed) Try(idx, lb); };
            _order.Add((lb, Items[idx].when));
        }
        Subs(null, "九样东西摊满桌面。他要在纸上重排一遍时间。", "（按时间顺序，把纸片一张张点到右边的横线上。）");
    }

    private void Try(int idx, Label lb)
    {
        if (idx != _next) { AudioIndex.Sfx("sfx_caliper_lock"); return; }   // 不对：一声轻响，不给解释
        lb.Text = _next + 1 + ". " + Items[idx].when + " · " + Items[idx].what;
        lb.Modulate = new Color(1f, 0.98f, 0.9f);
        Sfx("sfx_paper_place");
        _next++;
        if (_next == Items.Length)
        {
            Save.Set(RunState.Flag.TimelineBuilt); Save.Save();
            Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()),
                "时间排完，线全在一头：苏航。",
                "还差最后一步：这些东西，得交到能固定它的地方。");
        }
    }
}
