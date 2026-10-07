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

    public override void _Ready()
    {
        // 测试写 user://test_ledger.json——上一版测试直接写玩家存档，
        // 真人反馈"标题进去直接跳到第五章"就是被测试污染的档。
        RunState.SavePath = "user://test_ledger.json";
        RunState.DeleteSave();
        MoShi.Game.EndingCard.SuppressSceneChange = true;   // 测试不许触发真换场
        Run();
    }

    async void Run()
    {
        GD.Print("════════ 集成测试 ════════");
        TestAssetsAndAudio();
        TestFlowRouting();
        TestTitleClick();
        await TestPrologueClicks();
        await TestCh01Flow();
        await TestDocChoiceFlow();
        await TestIdentitySwapRoute();
        await SmokeChapters();
        RunState.DeleteSave();   // 清的是 test 档
        GD.Print($"════════ 结果：{_pass} 过 / {_fail} 挂 ════════");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    void TestAssetsAndAudio()
    {
        SlotRegistry.Install();
        var dbg = AssetIntake.Get("bg_shop_interior").GetImage();
        dbg.SavePng("res://data/gen/intake_dump.png");
        GD.Print("[dump] bg_shop_interior 实得 " + dbg.GetWidth() + "x" + dbg.GetHeight());
        Check(SlotRegistry.Total == 84, $"槽位总数 {SlotRegistry.Total}");
        int artNeed = 0;
        foreach (var s in SlotRegistry.All) if (s.NeedsArt) artNeed++;
        Check(artNeed == 0, $"待美术槽 {artNeed}（全部美术已交付）");

        foreach (var c in AudioIndex.Delivered)
            Check(ResourceLoader.Exists(c.File), $"音频在库：{c.Id}");
        Check(AudioIndex.Missing().Count == 4,
            $"缺 4 条（实 {AudioIndex.Missing().Count}）：" + string.Join(" ", AudioIndex.Missing()));
    }

    async System.Threading.Tasks.Task TestDocChoiceFlow()
    {
        // #16 回归：分支资料卡必须点得动（旧版是隐形热区）
        RunState.DeleteSave();
        var c7 = new Ch07();
        AddChild(c7);
        bool armed = false;
        for (int i = 0; i < 40 && !armed; i++)
        {
            c7.TestAdvance();
            for (int f = 0; f < 25; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            armed = c7.TestDocCount == 2;
        }
        Check(armed, "第七章：资料卡出现（2 张）");
        if (armed)
        {
            // 结构断言：整棵树里不许有"全屏级、MouseFilter=Stop"的控件——
            // 那正是 Black() 吞光点击的 bug 类（Citrate#20）。
            int eater = CountFullscreenClickEater(c7);
            Check(eater == 0, $"无全屏吞点击控件（实测 {eater} 个）");
            c7.TestClickDoc(0);
            for (int f = 0; f < 5; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(RunState.Load().Has(RunState.Flag.AskedHanmei), "第七章：点资料卡触发后续（asked_hanmei 落盘）");
        }
        c7.QueueFree();
    }

    async System.Threading.Tasks.Task TestIdentitySwapRoute()
    {
        // 真结局前提②「发现身份异常」：第五章 A 必须落 identity_swap——
        // 否则 TrueEndingReady 恒假、第十三章被判回标题，真结局永远走不到。
        RunState.DeleteSave();
        var c5 = new Ch05();
        AddChild(c5);
        bool armed = false;
        for (int i = 0; i < 60 && !armed; i++)
        {
            c5.TestAdvance();
            for (int f = 0; f < 25; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            armed = c5.TestDocCount == 3;
        }
        Check(armed, "第五章：资料卡出现（3 张）");
        if (armed)
        {
            c5.TestClickDoc(0);   // A：韩湘的死亡证明 ↔ 失踪时间
            for (int f = 0; f < 5; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var s = RunState.Load();
            Check(s.Has(RunState.Flag.Clue13), "第五章 A → clue_13 落盘（推进项）");
            Check(s.Has(RunState.Flag.IdentitySwap), "第五章 A → identity_swap 落盘（真结局前提②）");
        }
        c5.QueueFree();

        // 真结局判定 = 碑面异常 + 身份异常 + 未收钱 + 固定证据
        var t = new RunState();
        t.Set(RunState.Flag.Clue16); t.Set(RunState.Flag.StoneAltered); t.Set(RunState.Flag.IdentitySwap);
        t.Set(RunState.Flag.EvidenceFixed); t.Save();
        Check(RunState.Load().TrueEndingReady, "四条件齐 → TrueEndingReady 成立");
        var t2 = RunState.Load(); t2.Set(RunState.Flag.MoneyTaken); t2.Save();
        Check(!RunState.Load().TrueEndingReady, "收过钱 → 真结局不成立");
        RunState.DeleteSave();
    }

    static int CountFullscreenClickEater(Node n)
    {
        int c = 0;
        if (n is Control ctl && ctl.Visible && ctl.MouseFilter == Control.MouseFilterEnum.Stop)
        {
            var r = ctl.GetGlobalRect();
            if (r.Size.X >= 600 && r.Size.Y >= 300) c++;   // 全屏级
        }
        foreach (var ch in n.GetChildren()) c += CountFullscreenClickEater(ch);
        return c;
    }

    async System.Threading.Tasks.Task SmokeChapters()
    {
        // 章节场景烟测：加载 + 跑 60 帧不出脚本错误即过（错误由 Godot 打 ERROR 行，
        // 这里只保证不崩；崩溃级会直接抛出）。
        foreach (var scn in new[] { "res://scenes/ChPrologue.tscn", "res://scenes/Ch02.tscn", "res://scenes/Ch03.tscn", "res://scenes/Ch04.tscn", "res://scenes/Ch05.tscn", "res://scenes/Ch06.tscn", "res://scenes/Ch07.tscn", "res://scenes/Ch08.tscn", "res://scenes/Ch09.tscn", "res://scenes/Ch10.tscn", "res://scenes/Ch11.tscn", "res://scenes/EndingCard.tscn", "res://scenes/RpgOffice.tscn", "res://scenes/RpgShop.tscn", "res://scenes/RpgArchive.tscn" })
        {
            if (!GodotObject.IsInstanceValid(this)) return;   // 序章等场景的自动换场可能波及测试树
            // 直接实例化场景根（.tscn 的脚本在 StorySceneBase 下）
            var inst = GD.Load<PackedScene>(scn).Instantiate();
            AddChild(inst);
            for (int i = 0; i < 60; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            int sprites = CountNodes(inst, 0);
            Check(GodotObject.IsInstanceValid(inst) && sprites > 0, $"烟测：{scn.GetFile()}（活体+{sprites}个渲染节点）");
            inst.QueueFree();
        }
    }

    void TestTitleClick()
    {
        // 真人反馈"单击无反应"回归：合成一次左键，Boot 必须进转场
        var boot = GD.Load<PackedScene>("res://scenes/Boot.tscn").Instantiate<Boot>();
        AddChild(boot);
        var enter = new InputEventKey { Keycode = Key.Enter, Pressed = true };
        boot._Input(enter);
        Check(boot.TestLeaving, "标题：Enter 触发当前菜单项→转场");
        boot.QueueFree();
    }

    void TestFlowRouting()
    {
        RunState.DeleteSave();
        Check(ChapterFlow.Next().EndsWith("ChPrologue.tscn"), "路由：新档 → 序章");
        var s = new RunState(); s.Set(RunState.Flag.Clue16); s.Save();
        Check(ChapterFlow.Next().EndsWith("Ch01.tscn"), "路由：有序章 → 一章");
        s = RunState.Load(); s.Set(RunState.Flag.Clue01); s.Set(RunState.Flag.StoneAltered); s.Save();
        Check(ChapterFlow.Next().EndsWith("RpgOffice.tscn"), "路由：有一章 → 二章（实验：先进办公室走位段）");
        s = RunState.Load(); s.Set(RunState.Flag.Clue05); s.Save();
        Check(ChapterFlow.Next().EndsWith("RpgShop.tscn"), "路由：三章 → 铺子走位段（shop 章共用）");
        s = RunState.Load(); s.Set(RunState.Flag.Clue06); s.Save();
        Check(ChapterFlow.Next().EndsWith("RpgArchive.tscn"), "路由：四章 → 档案室走位段");
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

    async System.Threading.Tasks.Task TestPrologueClicks()
    {
        // 真人反馈"序章点不动"的回归：点击必须推进字幕与对话
        RunState.DeleteSave();
        var p = GD.Load<PackedScene>("res://scenes/ChPrologue.tscn").Instantiate<ChPrologue>();
        AddChild(p);
        for (int i = 0; i < 60 && !p.TestReady2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(p.TestSubVisible, "序章：开场字幕在放");
        var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(320, 180) };
        var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(320, 180) };
        bool advanced = false;
        for (int i2 = 0; i2 < 6 && !advanced; i2++)   // 限步：绝不推进到序章尾部的自动换场
        {
            p._UnhandledInput(click); p._UnhandledInput(release);
            for (int f = 0; f < 8; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            advanced = !p.TestSubVisible || i2 > 4;
        }
        Check(advanced, "序章：点击推进字幕（第二次踩同一坑的回归）");

        bool faceSeen = false;
        for (int i2 = 0; i2 < 4 && !faceSeen; i2++)   // 4 步内到 wu 行，绝不到序章尾部换场
        {
            if (!GodotObject.IsInstanceValid(p)) break;
            p.TestAdvance();
            for (int f = 0; f < 25; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            faceSeen = p.TestFace != null && p.TestFace.Visible;
        }
        Check(faceSeen, "序章：步进到带头像的台词");
        if (faceSeen)
        {
            var r = p.TestFace.GetGlobalRect();
            Check(r.Position.X >= 0 && r.End.X <= 640 && r.End.Y <= 360,
                  $"头像在视口内 ({r.Position.X},{r.Position.Y})-({r.End.X},{r.End.Y})");
        }
        var pp = p.TestPanel;
        if (pp != null)
        {
            var pr = pp.GetGlobalRect();
            Check(pr.Size.X >= 639.9f && pr.End.Y <= 360.01f && pr.Position.Y >= 200,
                  $"对话框通铺且贴底 ({pr.Position.X},{pr.Position.Y}) {pr.Size.X}×{pr.Size.Y}");
        }
        p.QueueFree();
    }

    static int CountNodes(Node n, int depth)
    {
        if (depth > 6) return 0;
        int c = n is Sprite2D or ColorRect or Panel or Label ? 1 : 0;
        foreach (var ch in n.GetChildren()) c += CountNodes(ch, depth + 1);
        return c;
    }

    async System.Threading.Tasks.Task TestCh01Flow()
    {
        // ── A 路：擦 → 露 → 描 → clue_01 + 落盘可回读 ──
        RunState.DeleteSave();
        var scene = new Ch01();
        AddChild(scene);
        scene.SetProcess(false);   // 钩子驱动，禁真实 _Process 竞争
        for (int i = 0; i < 200 && !scene.TestReady; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (int i = 0; i < 200 && !scene.TestReady; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.TestReady, "Ch01 构建（碑面/笔画就绪）headless 可用");
        Check(scene.TestPhase == Ch01.Phase.Wiping, "初始阶段=Wiping");

        scene.Test_WipeDate();
        var hud = MenuHud.Instance;
        for (int f = 0; f < 4; f++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // 等延迟挂树
        Check(hud != null && hud.Visible && hud.IsInsideTree() && hud.TestBackVisible && hud.TestChapterText.Contains("第一章"),
              $"HUD 就位且已挂树（「{hud?.TestChapterText}」+返回按钮）");   // Citrate#5/#6/#23
        Check(scene.TestPhase == Ch01.Phase.Choice, "擦净日期区 → 解锁 Choice");

        // Natsume 回归：进-出-进，进度只增不减
        var ctr = new Vector2(373, 277);
        scene.Test_TraceStep(ctr, true);
        scene.Test_TraceStep(ctr + new Vector2(8, 6), true);
        float afterIn = scene.Test_SixteenProgress();
        scene.Test_TraceStep(new Vector2(20, 20), false);          // 手离开描迹区
        float afterLeave = scene.Test_SixteenProgress();
        Check(afterIn > 0 && afterLeave >= afterIn, $"描离开进度保留（{afterIn:P0}→{afterLeave:P0}）");
        // Citrate#4：快速 进-出-进-出 抖动，冷却窗口内只允许一次断音
        int plays = scene.CountBreakSfxForTest();
        scene.Test_TraceStep(ctr + new Vector2(9, 7), true); scene.Test_TraceStep(new Vector2(20, 20), false);
        scene.Test_TraceStep(ctr + new Vector2(9, 7), true); scene.Test_TraceStep(new Vector2(20, 20), false);
        scene.Test_TraceStep(ctr + new Vector2(9, 7), true); scene.Test_TraceStep(new Vector2(20, 20), false);
        Check(scene.CountBreakSfxForTest() - plays <= 1, $"断音时间闸门生效（3 次抖动仅 {scene.CountBreakSfxForTest() - plays} 响）");

        scene.Test_TraceSixteen();
        var st = RunState.Load();
        Check(st.Has(RunState.Flag.Clue01), "描满 → clue_01 落盘");
        Check(st.Has(RunState.Flag.StoneAltered), "stone_altered 落盘");
        Check(st.GetChoice(1) == 'A', "ch1_choice=A");
        Check(st.Ledger.Count >= 1, "账本多了一行");
        scene.QueueFree();

        // ── 擦过头：物理后果（不可逆）──
        RunState.DeleteSave();
        var s2 = new Ch01();
        AddChild(s2); s2.SetProcess(false);
        for (int i = 0; i < 200 && !s2.TestReady; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        s2.Test_Overwipe();
        Check(s2.Test_CrushedGrew(), "擦过头真的压毁了刻痕（Crushed 增长）");
        s2.QueueFree();

        // ── C 路：结局一判定 ──
        RunState.DeleteSave();
        var s3 = new Ch01();
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
