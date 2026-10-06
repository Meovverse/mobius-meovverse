using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S15 · 第九章 真相拼合：按时间把证据点上去（对的按顺序亮起，系统不夸你）。</summary>
public partial class Ch09 : StorySceneBase
{
    private readonly List<(Label node, string when)> _order = new();
    private int _next;
    private Label _hint2;


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
        // 背景＝一堆信（美术交付）。cover 铺满：按宽对齐 640，纵向裁到 360。
        // 深色桌面作底：信纸清理过棋盘格，现在是透明抠像，需要底衬
        AddChild(new ColorRect { Color = new Color(0.10f, 0.09f, 0.08f), Size = new Vector2(640, 360),
                                 MouseFilter = Control.MouseFilterEnum.Ignore });
        var pileTex = ResourceLoader.Load<Texture2D>("res://assets/textures/letters_pile.png");
        if (pileTex != null)
        {
            float k = 640f / pileTex.GetWidth();
            AddChild(new Sprite2D { Texture = pileTex, Centered = true,
                Position = new Vector2(320, 180), Scale = new Vector2(k, k) });
        }
        else Plate("bg_shop_front");
        var scatter = new Rect2[]
        {
            new(380, 200, 150, 34), new(90, 90, 150, 34), new(300, 60, 170, 34),
            new(120, 220, 160, 34), new(420, 110, 150, 34), new(180, 150, 150, 34),
        };
        // #22：原来每张纸写"？"，玩家看不到内容就无从判断先后——必然卡住。
        // 现在纸上直接写"发生了什么"（内容本身是线索），玩家据此推理时间顺序。
        for (int i = 0; i < Items.Length; i++)
        {
            int idx = i;
            var lb = new Label { Text = Items[i].what, Position = new Vector2(scatter[i].Position.X, scatter[i].Position.Y),
                                 Size = new Vector2(scatter[i].Size.X, 30), MouseFilter = Control.MouseFilterEnum.Stop,
                                 AutowrapMode = TextServer.AutowrapMode.WordSmart };
            // 与素材同调：墨棕色写在信纸上（原来白字压深影，和浅色纸面相冲）
            lb.AddThemeColorOverride("font_color", new Color(0.40f, 0.31f, 0.18f));
            lb.AddThemeColorOverride("font_shadow_color", new Color(0.98f, 0.96f, 0.88f, 0.55f));
            lb.AddThemeConstantOverride("shadow_offset_x", 1);
            lb.AddThemeConstantOverride("shadow_offset_y", 1);
            Ui.AddChild(lb);
            lb.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed) Try(idx, lb); };
            _order.Add((lb, Items[idx].when));
        }
        _hint2 = new Label { Position = new Vector2(16, 30), Size = new Vector2(608, 18),
                             Modulate = new Color(1, 1, 1, 0.95f) };
        _hint2.AddThemeColorOverride("font_color", new Color(0.42f, 0.33f, 0.20f));
        _hint2.AddThemeColorOverride("font_shadow_color", new Color(0.98f, 0.96f, 0.88f, 0.5f));
        Ui.AddChild(_hint2);
        Subs(null,
            "九样东西摊在桌上。把它们按**事情发生的先后**，依次点出来。",
            "点对了，它自己会标上序号；点错了只轻轻一响——再想想哪件最早发生。");
    }

    private void Try(int idx, Label lb)
    {
        if (idx != _next)
        {
            AudioIndex.Sfx("sfx_caliper_lock");   // 不对：一声轻响
            _hint2.Text = $"还没轮到它（已排 {_next}/6）。先想想哪件事最早发生。";
            return;
        }
        _hint2.Text = "";
        lb.Text = _next + 1 + ". " + Items[idx].when + " · " + Items[idx].what;
        lb.Modulate = new Color(0.72f, 0.6f, 0.4f);   // 排好后压成更深的墨
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
