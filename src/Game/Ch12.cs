using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>
/// S18 · 终章 立碑：砸底座。剧本要求 90 秒不跳过——jam 演示版压成 6 锤
/// （每锤 ~2.4 秒演出），正式发布前把 ChiselTotal 调回 36 并接老吴背影视频。
/// </summary>
public partial class Ch12 : StorySceneBase
{
    private const int ChiselTotal = 6;
    private int _chisel;
    private Label _hud;
    private ColorRect _dust;

    protected override void SceneReady()
    {
        Plate("bg_cemetery_qingming");
        Ambient("amb_graveyard");
        Raw("res://assets/textures/char_luyun_back.png", new Vector2(320, 260));
        _hud = new Label { Position = new Vector2(200, 278), Size = new Vector2(280, 30),
                           Text = "Enter：凿" };
        Ui.AddChild(_hud);
        _dust = new ColorRect { Color = new Color(0.8f, 0.78f, 0.72f, 0f),
                                Position = new Vector2(280, 300), Size = new Vector2(120, 20) };
        Ui.AddChild(_dust);

        Subs(Hit, "A 区 7 号前围了人。苏航也来了。",
            "有人问：\"你在干什么？\"",
            "老吴：\"我在把日期挖出来。\"",
            "（锤子在手里。第一下落下。）");
    }

    private void Hit()
    {
        if (_revealed) return;
        Sfx(_chisel % 2 == 0 ? "sfx_hammer_swing" : "sfx_hammer_chisel");
        _dust.Color = new Color(0.8f, 0.78f, 0.72f, 0.35f);
        var tw = CreateTween();
        tw.TweenProperty(_dust, "color:a", 0f, 0.9f);
        _chisel++;
        _hud.Text = $"Enter：凿（{_chisel}/{ChiselTotal}）";
        // Citrate#48：原来只有"再按一下"才会揭底，可 _UnhandledInput 又把 _chisel>=6 的
        // 输入挡掉了 → 永远揭不了底。改成第 6 锤落下即揭底。
        if (_chisel >= ChiselTotal) Reveal();
    }

    // demo 版：Enter 连续敲也可（正式版锁节奏 = 每锤间隔）
    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_accept") && _chisel < ChiselTotal) Hit();
        base._UnhandledInput(e);
    }

    private bool _revealed;

    private void Reveal()
    {
        if (_revealed) return;
        _revealed = true;
        Sfx("sfx_base_crack");
        var card = new Panel { Position = new Vector2(150, 80), Size = new Vector2(340, 150), MouseFilter = Control.MouseFilterEnum.Ignore };
        card.AddChild(new Label { Position = new Vector2(20, 14), Text =
            "—— 底座下的原始石面 ——\n材料日期：2006 年 5 月 17 日\n刻字日期：2006 年 5 月 17 日\n（上面那块新碑：05 月 16 日）" });
        Ui.AddChild(card);
        Save.Set(RunState.Flag.BaseExposed); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile("res://scenes/Ch13.tscn"),
            "五月十七日，是真实的死亡日期，也是老吴最初刻下的日期。",
            "五月十六日，是伪造出来骗保的日期。",
            "两个日期，同时见了光。");
    }
}
