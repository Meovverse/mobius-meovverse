using System;
using Godot;
using MoShi.Core;
using MoShi.Game;

namespace MoShi.Dev;

/// <summary>
/// 集成测试（代替外部流程测试）。headless 跑：
///   Godot --headless --path . res://scenes/IntegrationTest.tscn
/// 退出码 0 = 全过，1 = 有失败。断言失败不 throw——全部跑完再汇总，一次看全。
/// </summary>
public partial class IntegrationTest : Node
{
    int _fail, _pass;

    void Check(bool ok, string what)
    {
        GD.Print((ok ? "  ✓ " : "  ✗ ") + what);
        if (ok) _pass++; else _fail++;
    }

    public override void _Ready() => Run();

    async void Run()
    {
        GD.Print("════════ 集成测试 ════════");
        TestAssetsAndAudio();
        TestFlowRouting();
        TestTitleClick();
        await TestChapterOneFlow();
        await SmokeChapters();
        GD.Print($"════════ 结果：{_pass} 过 / {_fail} 挂 ════════");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    void TestAssetsAndAudio()
    {
        SlotRegistry.Install();
        Check(SlotRegistry.Total == 83, $"槽位总数 {SlotRegistry.Total}");
        int artNeed = 0;
        foreach (var s in SlotRegistry.All) if (s.NeedsArt) artNeed++;
        Check(artNeed == 4, $"待美术槽 {artNeed}（预期 4：铺子内景/办公室/档案室/清明定景——老吴背影四批已转正）");

        foreach (var c in AudioIndex.Delivered)
            Check(ResourceLoader.Exists(c.File), $"音频在库：{c.Id}");
        Check(AudioIndex.Missing().Count == 8,
            $"缺 8 条（实 {AudioIndex.Missing().Count}）：" + string.Join(" ", AudioIndex.Missing()));
    }

    async System.Threading.Tasks.Task SmokeChapters()
    {
        // 章节场景烟测：加载 + 跑 60 帧不出脚本错误即过（错误由 Godot 打 ERROR 行，
        // 这里只保证不崩；崩溃级会直接抛出）。
        foreach (var scn in new[] { "res://scenes/ChPrologue.tscn", "res://scenes/Ch02.tscn", "res://scenes/Ch03.tscn", "res://scenes/Ch04.tscn", "res://scenes/Ch05.tscn", "res://scenes/Ch06.tscn", "res://scenes/Ch07.tscn", "res://scenes/Ch08.tscn", "res://scenes/Ch09.tscn", "res://scenes/Ch10.tscn", "res://scenes/Ch11.tscn" })
        {
            // 直接实例化场景根（.tscn 的脚本在 StorySceneBase 下）
            var inst = GD.Load<PackedScene>(scn).Instantiate();
            AddChild(inst);
            for (int i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(GodotObject.IsInstanceValid(inst), $"烟测：{scn.GetFile()}");
            inst.QueueFree();
        }
    }

    void TestTitleClick()
    {
        // 真人反馈"单击无反应"回归：合成一次左键，Boot 必须进转场
        var boot = GD.Load<PackedScene>("res://scenes/Boot.tscn").Instantiate<Boot>();
        AddChild(boot);
        var mb = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(300, 200) };
        boot._UnhandledInput(mb);
        Check(boot.TestLeaving, "标题：单击左键触发转场（MouseFilter 回归）");
        boot.QueueFree();
    }

    void TestFlowRouting()
    {
        RunState.DeleteSave();
        Check(ChapterFlow.Next().EndsWith("ChPrologue.tscn"), "路由：新档 → 序章");
        var s = new RunState(); s.Set(RunState.Flag.Clue16); s.Save();
        Check(ChapterFlow.Next().EndsWith("ChapterOne.tscn"), "路由：有序章 → 一章");
        s = RunState.Load(); s.Set(RunState.Flag.Clue01); s.Set(RunState.Flag.StoneAltered); s.Save();
        Check(ChapterFlow.Next().EndsWith("Ch02.tscn"), "路由：有一章 → 二章");
        // 直接推到终章
        s = new RunState();
        s.Set(RunState.Flag.Clue16); s.Set(RunState.Flag.Clue01); s.Set(RunState.Flag.StoneAltered);
        s.Set(RunState.Flag.Clue05); s.Set(RunState.Flag.Clue06); s.Set(RunState.Flag.Clue10);
        s.Set(RunState.Flag.Clue13); s.Set(RunState.Flag.Clue15); s.Set("ch7_done"); s.Set("ch8_done");
        s.Set(RunState.Flag.TimelineBuilt); s.Set("ch10_done"); s.Set(RunState.Flag.EvidenceFixed);
        s.Save();
        Check(ChapterFlow.Next().EndsWith("Ch12.tscn"), "路由：证据齐 → 终章");
        RunState.DeleteSave();
    }

    async System.Threading.Tasks.Task TestChapterOneFlow()
    {
        // ── A 路：擦 → 露 → 描 → clue_01 + 落盘可回读 ──
        RunState.DeleteSave();
        var scene = new ChapterOne();
        AddChild(scene);
        scene.SetProcess(false);   // 钩子驱动，禁真实 _Process 竞争
        for (int i = 0; i < 200 && !scene.TestReady; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.TestReady, "ChapterOne 构建（碑面/笔画就绪）headless 可用");
        Check(scene.TestPhase == ChapterOne.Phase.Wiping, "初始阶段=Wiping");

        scene.Test_WipeDate();
        Check(scene.TestPhase == ChapterOne.Phase.Choice, "擦净日期区 → 解锁 Choice");

        scene.Test_TraceSixteen();
        var st = RunState.Load();
        Check(st.Has(RunState.Flag.Clue01), "描满 → clue_01 落盘");
        Check(st.Has(RunState.Flag.StoneAltered), "stone_altered 落盘");
        Check(st.GetChoice(1) == 'A', "ch1_choice=A");
        Check(st.Ledger.Count >= 1, "账本多了一行");
        scene.QueueFree();

        // ── 擦过头：物理后果（不可逆）──
        RunState.DeleteSave();
        var s2 = new ChapterOne();
        AddChild(s2); s2.SetProcess(false);
        for (int i = 0; i < 200 && !s2.TestReady; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        s2.Test_Overwipe();
        Check(s2.Test_CrushedGrew(), "擦过头真的压毁了刻痕（Crushed 增长）");
        s2.QueueFree();

        // ── C 路：结局一判定 ──
        RunState.DeleteSave();
        var s3 = new ChapterOne();
        AddChild(s3); s3.SetProcess(false);
        for (int i = 0; i < 200 && !s3.TestReady; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        s3.Test_ChooseC();
        var st3 = RunState.Load();
        Check(st3.GetChoice(1) == 'C' && st3.EndingOneFired, "选 C → 结局一判定成立");
        s3.QueueFree();
        RunState.DeleteSave();   // 测完清档，别把 C 留给下一次开机
    }
}
