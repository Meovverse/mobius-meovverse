using System.Collections.Generic;
using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// S15 · 第九章 真相拼合：把九样线索按时间先后依次点出来。
///
/// 呈现（Citrate#25）：背景是一叠信（缩小到整叠可见），每一件事用**小红点 +
/// 折线引线**标注——红点扎在信纸上，先一段斜线牵出，再接一段水平线，
/// 文本就写在水平线上。不再用飘在空中的白字。
/// </summary>
public partial class Ch09 : StorySceneBase
{
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

    private readonly List<(Label node, int idx)> _cards = new();

    protected override void SceneReady()
    {
        AddChild(new ColorRect { Color = new Color(0.10f, 0.09f, 0.08f), Size = new Vector2(640, 360),
                                 MouseFilter = Control.MouseFilterEnum.Ignore });

        // 一堆信：**整叠完整可见**（原来 cover 裁掉上下、显得局促）——缩到高 260 居中。
        var pile = ResourceLoader.Load<Texture2D>("res://assets/textures/letters_pile.png");
        if (pile != null)
        {
            float k = 260f / pile.GetHeight();
            AddChild(new Sprite2D { Texture = pile, Centered = true,
                Position = new Vector2(320, 180), Scale = new Vector2(k, k) });
        }

        var dot = ResourceLoader.Load<Texture2D>("res://assets/textures/red_point.png");
        var lineCol = new Color(0.62f, 0.22f, 0.16f);

        // #31：红点**错落有致**地落在信纸本体上（不再笔直两列）；六个选项的
        // 视觉位置与"正确先后"**打乱**——你在屏幕上按内容判断，而不是按位置顺序。
        float[] dotX   = { 232, 398, 270, 360, 218, 420 };
        float[] dotY   = { 120,  88, 210, 158, 272, 250 };
        float[] labelY = {  96,  90, 178, 176, 262, 268 };
        int[]   perm   = { 2, 5, 0, 3, 1, 4 };   // 槽位 → 事情索引（乱序）

        for (int s = 0; s < 6; s++)
        {
            int idx = perm[s];
            bool right = s % 2 == 1;
            float lineY = labelY[s];
            float dx = dotX[s], dy = dotY[s];
            float elbowX = right ? 462 : 178;
            float outX = right ? 622 : 18;

            if (dot != null)
                AddChild(new Sprite2D { Texture = dot, Position = new Vector2(dx, dy),
                                        Scale = new Vector2(0.07f, 0.07f) });
            // 折线：斜线（红点→折点）+ 水平线（折点→外端）
            AddChild(new Line2D { Points = new[] { new Vector2(dx, dy), new Vector2(elbowX, lineY), new Vector2(outX, lineY) },
                                  Width = 1.4f, DefaultColor = lineCol, Antialiased = false });

            // 文本写在水平线上（靠外端一侧），可点击当作"选它"。
            // ★ 用固定尺寸的 Control 盒子 + 把 Label FullRect 锚进去：
            //   直接给 Label 设 Size 会被引擎"撑到内容宽"（含空格/数字的行尤其
            //   明显），换行与对齐全部失效——盒子才是可靠的宽度约束。
            float tw = right ? outX - elbowX - 6 : elbowX - outX - 4;
            float tx = right ? elbowX + 4 : outX;
            var box = new Control { Position = new Vector2(tx, lineY - 54), Size = new Vector2(tw, 52),
                                    MouseFilter = Control.MouseFilterEnum.Stop };
            var lb = new Label
            {
                Text = Items[idx].what,
                AutowrapMode = TextServer.AutowrapMode.Arbitrary,   // 中文无空格，必须任意处断行
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = right ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            lb.AddThemeFontSizeOverride("font_size", 14);
            lb.AddThemeColorOverride("font_color", new Color(0.42f, 0.32f, 0.18f));
            lb.AddThemeColorOverride("font_shadow_color", new Color(0.98f, 0.96f, 0.88f, 0.5f));
            lb.AddThemeConstantOverride("shadow_offset_x", 1);
            lb.AddThemeConstantOverride("shadow_offset_y", 1);
            box.AddChild(lb);
            lb.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            Ui.AddChild(box);
            box.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true } mb && mb.ButtonIndex == MouseButton.Left) Try(idx, lb); };
            _cards.Add((lb, idx));
        }

        _hint2 = new Label { Position = new Vector2(16, 6), Size = new Vector2(608, 18),
                             HorizontalAlignment = HorizontalAlignment.Center };
        _hint2.AddThemeColorOverride("font_color", new Color(0.45f, 0.35f, 0.22f));
        _hint2.AddThemeColorOverride("font_shadow_color", new Color(0.98f, 0.96f, 0.88f, 0.5f));
        Ui.AddChild(_hint2);

        Subs(null,
            "一堆纸摊在桌上。把它们按**事情发生的先后**，依次点出来。",
            "点对了，它会标上序号；点错了只轻轻一响——再想想哪件最早发生。");
    }

    private void Try(int idx, Label lb)
    {
        if (idx != _next)
        {
            AudioIndex.Sfx("sfx_caliper_lock");
            _hint2.Text = $"还没轮到它（已排 {_next}/6）。先想想哪件事最早发生。";
            return;
        }
        _hint2.Text = "";
        lb.Text = _next + 1 + "　" + Items[idx].what;
        lb.AddThemeColorOverride("font_color", new Color(0.26f, 0.18f, 0.09f));
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
