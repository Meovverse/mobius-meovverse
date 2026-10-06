using Godot;
using MoShi.Core;

namespace MoShi.Game;

/// <summary>S13 · 第七章：信封与「韩梅呢？」。关键选择六。打字交互降级为两句可选台词（键盘输入 M2 之后接）。</summary>
public partial class Ch07 : StorySceneBase
{
    protected override void SceneReady()
    {
        Plate("bg_title_shopfront");
        var night = Black(0.45f);
        Raw("res://assets/textures/char_suhang.png", new Vector2(452, 128), 236);   // 苏航立在门口，脚落在柜台前
        var env = Raw("res://assets/textures/prop_envelope_closed.png", new Vector2(320, 150));   // #19：上移，别被对话框压住
        if (env != null) env.Scale = Vector2.One * 0.35f;

        Dialogue(new[]
        {
            ("su", "老吴，这件事到这里就结束吧。"),
            ("su", "你只需要说，碑上的日期一直都是十六号。"),
            ("", "信封推过来。里面是一笔钱。"),
        }, Ask);
    }

    private void Ask()
    {
        DocChoices("信封推到面前。老吴得先决定说不说那一句话。",
            ("开口：「韩梅呢？」", "问出来，今晚就别想睡", () => AskLine()),
            ("沉默", "先收下话，什么都还不收", () => { Save.Set("asked_hanmei_false"); Money(); }));
    }

    private void AskLine()
    {
        Save.Set(RunState.Flag.AskedHanmei); Save.Save();
        Dialogue(new[]
        {
            ("wu", "韩梅呢？"),
            ("", "苏航沉默了。"),
            ("", "这是老吴第一次从苏航脸上看见真正的恐惧。"),
            ("", "他没有承认。但也没有否认。"),
        }, Money);
    }

    private void Money()
    {
        Dialogue(new[]
        {
            ("", "几天后，又有人来铺子：「不要再查。」"),
            ("", "夜里只剩桌上那个信封。石头不会撒谎——但人会。"),
        }, () =>
        Subs(() => DocChoices("夜里，桌上只剩那个信封。",
            ("收下信封", "答应那句话：「日期一直是十六号」", Take),
            ("把信封推回去", "让他明天自己拿走", Refuse)), "钱和一句要背二十年的话，二选一。"));
    }

    private void Take()
    {
        Save.Set(RunState.Flag.MoneyTaken); Save.SetChoice(6, 'A'); Save.Save();
        EndingCard.Open(GetTree(), "结局三 · 妥协", "知道真相，却选择沉默。");
    }
    private void Refuse()
    {
        Save.Set("ch7_done"); Save.Save();
        Subs(() => GetTree().ChangeSceneToFile(ChapterFlow.Next()),
            "信封被推回桌面中央。明天，把摊子摆开。");
    }
}
