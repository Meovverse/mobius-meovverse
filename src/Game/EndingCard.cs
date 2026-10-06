using Godot;

namespace MoShi.Game;

/// <summary>
/// 结局卡（Natsume#6：BE 不能不明不白地"回退"）。
/// 黑场大字：哪个结局、为什么、账本怎么办——按 Enter 才走，没有倒计时。
/// </summary>
public partial class EndingCard : Node2D
{
    public static string Title = "", Theme = "", Foot = "";
    /// <summary>集成测试置 true：只记录结局、不真的换场（换场会连测试树一起删）。</summary>
    public static bool SuppressSceneChange;
    private ColorRect _white;

    public static void Open(SceneTree tree, string title, string theme, string foot = "")
    {
        Title = title; Theme = theme; Foot = foot;
        if (SuppressSceneChange) return;
        tree.ChangeSceneToFile("res://scenes/EndingCard.tscn");
    }

    public override void _Ready()
    {
        MenuHud.Ensure(GetTree(), null, false);   // 结局卡自成一体，不带返回钮
        AddChild(new ColorRect { Color = new Color(0.02f, 0.02f, 0.025f), Size = new Vector2(640, 360) });
        _white = new ColorRect { Color = new Color(1, 1, 1, 0), Size = new Vector2(640, 360),
                                 MouseFilter = Control.MouseFilterEnum.Ignore };
        var t = L(118, Title, 26, 1f); t.HorizontalAlignment = HorizontalAlignment.Center; t.Size = new Vector2(640, 40);
        var th = L(176, Theme, 15, 0.85f); th.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        th.Size = new Vector2(520, 60); th.Position = new Vector2(60, 176);
        var f = L(316, (Foot.Length > 0 ? Foot + "\n" : "") + "按 Enter 回封面 —— 账本还在，随时可以从头翻起。", 12, 0.55f);
        f.HorizontalAlignment = HorizontalAlignment.Center; f.Size = new Vector2(640, 40);
        AddChild(_white);
    }

    private Label L(float y, string text, int size, float alpha)
    {
        var l = new Label { Position = new Vector2(60, y), Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.Modulate = new Color(1, 1, 1, alpha);
        AddChild(l);
        return l;
    }

    public override void _Process(double dt)
    {
        if (Input.IsActionJustPressed("ui_accept"))
        {
            var tw = CreateTween();
            tw.TweenProperty(_white, "color:a", 1f, 0.4f);
            var t2 = GetTree();
            tw.TweenCallback(Callable.From(() => t2.ChangeSceneToFile("res://scenes/Boot.tscn")));
            SetProcess(false);
        }
    }
}
