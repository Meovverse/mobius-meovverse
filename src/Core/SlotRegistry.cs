using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>
/// ★ 全项目的资产槽位登记表。
///
/// 这是 `doc/美术需求.md` 的**机器可读版本**——一张表说清三件事：
///
///   1. 有哪些槽位
///   2. 每个槽位美术**要不要**做（`NeedsArt`）
///   3. 不做的时候程序拿什么顶上（`Proc`）
///
/// 所以美术那份需求表不再是"愿望清单"，而是这张表的人类可读投影。
/// **两边不一致时，以这张表为准**（校验见 <see cref="Audit"/>）。
///
/// 这个设计的直接后果：
///   美术要做的图从 42 张降到 **7 张**，其余全部程序生成。
///   而且美术就算交了图，尺寸/颜色/半透明不对，AssetIntake 也会就地修正。
/// </summary>
public static class SlotRegistry
{
    /// <summary>
    /// ★ Plate = 一格画面底板（doc/分镜稿.md §1 的 P1–P12）。
    ///
    /// 分镜稿把全篇 12 格底板列成一张表，并在"要美术交的"一栏里
    /// **直接沿用了本类的槽位名**（bg_graveyard_area_A / char_luyun_back …）。
    /// 所以本类就是那张表的机器可读版本。
    ///
    /// 复用价值很高：一个 Plate 在多场之间复用（S1/S14/S15/S16/S20 都用 P1），
    /// 所以**底板是这份清单里最安全的一类资产**。
    /// </summary>
    public enum Plate
    {
        P1,   // 铺子 · 内（木头门面、堆着的石料、蒙布的石碑）
        P2,   // 石面 · 全屏（碑面本体 Base + Detail）
        P3,   // 登记本 · 纸面
        P4,   // 纸 · 全屏（料单/事故报告/各种记录，共用一套纸面样式）
        P5,   // 办公室 · 桌面（木纹 + CRT）
        P6,   // 老系统 · 屏幕（灰蓝底白字）
        P7,   // 窗口 · 柜台（殡仪馆/医院档案室/派出所，换牌子）
        P8,   // 档案室（格架）
        P9,   // 墓区 · 2026（冷灰）
        P10,  // 墓区 · 清明（雾 + 新碑）
        P11,  // 底座 · 接缝 / 垫石
        P12,  // 黑场（所有字幕卡与结局文字）
    }

    /// <summary>哪个槽位提供哪一格底板。对照 doc/分镜稿.md §1。</summary>
    public static readonly Dictionary<Plate, string> PlateSource = new()
    {
        [Plate.P1] = "bg_shop_interior",
        [Plate.P2] = "stele_A7_face_base",
        [Plate.P3] = "prop_ledger_spread",
        [Plate.P4] = "prop_paper_sheet",
        [Plate.P5] = "bg_office_desk",
        [Plate.P6] = "ui_system_screen",
        [Plate.P7] = "bg_counter",
        [Plate.P8] = "bg_archive_room",
        [Plate.P9] = "bg_graveyard_area_A",
        [Plate.P10] = "bg_cemetery_qingming",
        [Plate.P11] = "stele_A7_base",
        [Plate.P12] = "endcard_paper",
    };


    public sealed record Slot(
        string Key,
        int W,
        int H,
        AssetIntake.Kind Kind,
        int MaxColors,
        bool AllowAlpha,
        string[] Candidates,
        Func<Image>? Proc,
        bool NeedsArt,
        string Reason,
        bool DrawnInCode = false);

    public static readonly List<Slot> All = new();

    private static void Add(
        string key, int w, int h, AssetIntake.Kind kind, int maxColors, bool allowAlpha,
        Func<Image>? proc, bool needsArt, string reason,
        params string[] candidates)
    {
        // 标了"程序绘制"的槽位不需要贴图，proc 为 null 是正常的
        bool drawn = reason.StartsWith("[code]");
        All.Add(new Slot(key, w, h, kind, maxColors, allowAlpha, candidates, proc, needsArt, reason, drawn));
    }

    public static void RegisterAll()
    {
        if (All.Count > 0) return;
        All.Clear();

        // ── A 噪声：全部程序生成。美术做这个毫无意义（程序能做得更准且保证无缝）──
        Add("noise_stone_grain", 64, 64, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.StoneNoise, false, "程序生成：周期噪声天然无缝");
        Add("noise_chisel_a", 32, 32, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.ChiselA, false, "程序生成。★ 毛边朝向单一，是判定依据");
        Add("noise_chisel_b", 32, 32, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.ChiselB, false, "程序生成。★ 毛边双向 + 崩口 + 密度更高");
        Add("noise_paper_fiber", 64, 64, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.FiberNoise, false, "程序生成：横向拉丝");
        Add("noise_dust", 32, 32, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.DustNoise, false, "程序生成");
        Add("noise_cement", 64, 64, AssetIntake.Kind.Noise, 0, true,
            () => ProcGen.CementNoise, false, "程序生成");

        // ── B 石碑：数据全部程序生成，只有"好看"可以交给美术 ──
        Add("stele_A7_face_base", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => ProcGen.SteleFace(640, 360), false, "程序生成三阶明暗 + 斜向高光带");
        Add("stele_A7_new", 320, 360, AssetIntake.Kind.Pixel, 10, false,
            () => ProcGen.SteleNew(320, 360), false, "程序生成两阶 + 窄亮接缝");
        Add("stele_A7_base", 320, 200, AssetIntake.Kind.Background, 12, false,
            () => ProcGen.SteleBase(320, 200), false, "程序生成十字接缝 + 细裂纹");
        Add("stele_A7_full", 320, 360, AssetIntake.Kind.Pixel, 10, false,
            null, true, "★ 碑的立体感（碑身+底座+草地）程序画不像，要美术");
        Add("map_stone_carve", 640, 360, AssetIntake.Kind.Mask, 0, false,
            () => SteleBuilder.BuildBatchMap(), false,
            "[code] 程序从字形遮罩推导（SteleBuilder.MarkRepairBar）");

        // ── C 背景：程序版能跑但很平。美术做这几张性价比最高 ──
        Add("bg_graveyard_area_A", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgGraveyard(640, 360), true,
            "远景=地图/墓地.png 顶部横带重处理，中景=抠出的真碑；石头质感仍要人/SD");
        Add("bg_cemetery_qingming", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgGraveyard(640, 360), true, "★ 同上，且要和 C1 是同一个地方");

        // ── 从 地图/墓地.png 复用出来的素材。都是已抠好的可用资源 ──
        Add("bg_graveyard_far", 640, 224, AssetIntake.Kind.Pixel, 12, true,
            () => null!, false,
            "墓地远景层：顶部横带去色+压对比+模糊。俯视原图不能直接当背景，"
            + "这么处理后不再读作俯视，所以能留");
        Add("stele_bg_01", 28, 52, AssetIntake.Kind.Pixel, 8, true, () => null!, false, "墓地抠出的墓碑 A");
        Add("stele_bg_02", 28, 33, AssetIntake.Kind.Pixel, 8, true, () => null!, false, "墓地抠出的墓碑 B");
        Add("stele_bg_03", 24, 34, AssetIntake.Kind.Pixel, 8, true, () => null!, false, "墓地抠出的墓碑 C");
        Add("stele_bg_04", 52, 35, AssetIntake.Kind.Pixel, 8, true, () => null!, false, "墓地抠出的宽碑 D");

        // ── 墓地.png 里抠出的其余道具。都是彩色原像素，未去色，供任意场景复用 ──
        Add("prop_chapel", 195, 195, AssetIntake.Kind.Pixel, 32, true, () => null!, false,
            "红瓦砖教堂（守墓人小屋的现成底子），带窗和门口绿植");
        Add("prop_well", 82, 62, AssetIntake.Kind.Pixel, 24, true, () => null!, false, "石砌水井");
        Add("prop_bench", 62, 50, AssetIntake.Kind.Pixel, 20, true, () => null!, false,
            "木指路牌（键名沿用旧称，图是双横板路牌）");
        // ★ 键名必须等于文件名：对账按 key.png 找。别加 plate_ 前缀，
        //   上次这么命名结果"已交付"被报成"无贴图"。
        Add("prop_mailbox_map", 42, 54, AssetIntake.Kind.Pixel, 24, true, () => null!, false,
            "地图抠出的红邮筒（美术包的 prop_mailbox 是商店场景 64×80 那张，别混）。"
            + "★ 新剧本里它是'这里一直有人来'的反证，用之前想清楚");
        Add("prop_bush", 44, 42, AssetIntake.Kind.Pixel, 20, true, () => null!, false, "白花灌木");
        Add("prop_fence", 58, 42, AssetIntake.Kind.Pixel, 16, true, () => null!, false, "木栅栏一段，可平铺");
        Add("prop_pine", 62, 101, AssetIntake.Kind.Pixel, 20, true, () => null!, false, "松树");
        Add("prop_rock", 43, 43, AssetIntake.Kind.Pixel, 16, true, () => null!, false, "紫灰圆石（带底座）");
        Add("bg_office_desk", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgOffice(640, 360), true, "★ 木纹 + CRT 打光，程序版太糙");
        Add("bg_archive_room", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgArchive(640, 360), true, "★ 几百个格架，程序版只是示意");
        Add("bg_shop_front", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgShop(640, 360), false, "程序生成：卷帘门条纹 + 地上石粉");
        // ★ 新增：P1 铺子内景。S1/S14/S15/S16/S20 五场共用，是复用面最大的一张
        Add("bg_shop_interior", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgShopInterior(640, 360), true,
            "★ P1 铺子内景。五场共用，复用面最大，但程序版画不出木头和石料的质感");
        Add("bg_title_shopfront", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgRain(640, 360), false, "程序生成：雨夜");

        // ── D 角色：全篇不画正脸，剪影程序能顶，但姿态是表演，要美术 ──
        Add("char_luyun_back", 160, 240, AssetIntake.Kind.Pixel, 10, false,
            () => Art.CharLuYunBack(160, 240), true,
            "★ 老吴背影。★ 2006 四十多岁 / 2026 六十多岁 —— 需要老年版，终章唯一的表演");
        // ★ 2026 剧本里苏老师这个角色不存在了。这张已交付的肖像改作韩梅的档案照。
        Add("char_suteacher_wheelchair", 160, 220, AssetIntake.Kind.Pixel, 10, false,
            () => Art.CharSuTeacher(160, 220), false,
            "★ 程序剪影。2026 版无此人；已交付的 char_suteacher_portrait 改作韩梅档案照");
        Add("char_photographer", 120, 200, AssetIntake.Kind.Pixel, 10, false,
            () => Art.CharPhotographer(120, 200), false, "程序生成剪影（1 阶色）");

        // ── E 道具 ──
        Add("prop_ledger_spread", 560, 320, AssetIntake.Kind.Pixel, 12, false,
            () => ProcGen.PaperSpread(276, 320), false,
            "P3 双页：左页 2006 两行／右页 2026 十八行线索。★ 本子上的字全是程序叠的");
        Add("prop_ledger_cover", 300, 380, AssetIntake.Kind.Pixel, 10, false,
            null, false, "[code] 程序生成封面 + 内衬衬纸；★ 里面那行字程序叠");
        Add("prop_scrap_stone", 96, 96, AssetIntake.Kind.Pixel, 10, false,
            () => Art.PropScrapStone(96, 96), false,
            "程序生成。★ 和碑面共用同一套刀口，所以「同一个人刻的」是算出来的");
        Add("prop_toolbox", 160, 100, AssetIntake.Kind.Pixel, 10, false,
            () => Art.PropToolbox(160, 100), false, "程序生成：石粉罐必须可辨认");
        Add("prop_deed_ledger", 300, 360, AssetIntake.Kind.Pixel, 10, false,
            () => Art.PropDeedLedger(300, 360), false, "程序生成。★ 压深的那一行是玩法");
        Add("prop_archive_form", 280, 200, AssetIntake.Kind.Pixel, 10, false,
            () => Art.PropArchiveForm(280, 200), false, "程序生成：明显更白 + 盖章位");

        // ── F 纸上元素：全部程序生成。中文印章和签名程序画得更好 ──
        Add("stamp_civil_bureau", 64, 64, AssetIntake.Kind.Pixel, 6, false,
            () => ProcGen.StampRound(64), false, "程序生成：双环 + 五角星 + 缺墨。文字程序绕排");
        Add("stamp_cremation", 64, 64, AssetIntake.Kind.Pixel, 6, false,
            () => ProcGen.StampRound(64), false, "同上");
        Add("stamp_insurance", 64, 64, AssetIntake.Kind.Pixel, 6, false,
            () => ProcGen.StampRound(64), false, "同上");
        Add("stamp_archive", 64, 64, AssetIntake.Kind.Pixel, 6, false,
            () => ProcGen.StampSquare(64), false, "程序生成方章，和圆章区分得开");
        Add("fx_crease_set", 128, 128, AssetIntake.Kind.Pixel, 8, false,
            () => ProcGen.PaperCrease(128, 128), false, "程序生成。★ 中段 1px 错位 = 同源特征");
        Add("ink_signature_suhang", 128, 48, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序用字体渲染「苏航」+ 形变模拟「练过的」");
        Add("ink_alteration_17to16", 96, 32, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序用字体渲染 + 一道斜划");

        // ── G UI：程序绘制，不用贴图 ──
        Add("ui_cursor_hand", 32, 32, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序用 Draw API 画，不占贴图槽");
        Add("ui_loupe_ring", 96, 96, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序画圆环 + 边光。美术做不出这么干净的 3 阶金属");
        Add("ui_caliper", 480, 32, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序画，刻度线要对齐像素格");
        Add("ui_paper_sheet", 256, 320, AssetIntake.Kind.Pixel, 10, false,
            () => ProcGen.PaperSheet(256, 320), false, "程序生成 9-patch 纸底");

        // ── P7 窗口柜台（2026 剧本新增）──────────────────────────────
        Add("bg_counter", 640, 360, AssetIntake.Kind.Background, 16, false,
            () => Art.BgCounter(640, 360), false,
            "[code] 程序生成：柜台 + 玻璃 + 一个窗口。殡仪馆/医院/派出所共用，换牌子");
        Add("ui_system_screen", 320, 200, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序用 Label 画的 Control。P6 老系统屏幕，灰蓝底白字");
        Add("prop_paper_sheet", 256, 320, AssetIntake.Kind.Pixel, 10, false,
            () => ProcGen.PaperSheet(256, 320), false, "程序生成 P4 全屏纸面：所有文书共用一套");

        // ── FX ──
        Add("fx_dust_particle", 1, 1, AssetIntake.Kind.Pixel, 2, false,
            () => ProcGen.DustParticle, false, "程序生成单像素");
        Add("fx_cement_chip", 8, 8, AssetIntake.Kind.Pixel, 4, false,
            () => ProcGen.CementChip, false, "程序生成 4px 碎片");

        // ── H ──
        Add("endcard_paper", 512, 288, AssetIntake.Kind.Background, 8, false,
            () => Art.Endcard(512, 288), false, "程序生成：纸纹 + 极暗");
        Add("sign_lu_shi", 160, 48, AssetIntake.Kind.Pixel, 8, false,
            null, false, "[code] 程序用字体渲染「吴氏石刻」，SD 画不了汉字");

        // ── E2 美术已交付（来自 /Downloads/gamejam素材）─────────────────
        //
        // ★ 这些图不是 640×360 像素画：512×768 的肖像、12 色的队标、
        //   带半透明的对话框。AssetIntake 会自动缩放 + 量化 + alpha 二值化。
        //   实测：苏老师量化到 12 级灰阶、缩到 176×264 仍然清晰可读。
        Add("logo_team", 256, 256, AssetIntake.Kind.Pixel, 4, false,
            null, false, "美术已交付：纯黑剪影，1 色，天然契合灰度。标题画面");
        Add("ui_dialog_frame", 320, 64, AssetIntake.Kind.Pixel, 8, false,
            null, false, "美术已交付：320×64 圆角面板，原图填充是 alpha 83，会被二值化成实心");
        Add("char_suteacher_portrait", 256, 384, AssetIntake.Kind.Pixel, 12, false,
            null, false, "美术已交付：512×768 肖像。★ 全篇唯一给人看的正脸");
        Add("char_laofan", 128, 192, AssetIntake.Kind.Pixel, 12, false,
            null, false, "美术已交付。★ 2026 版无老樊此人，可改作墓园经办 / 殡仪馆窗口");
        Add("char_suhang", 256, 384, AssetIntake.Kind.Pixel, 12, false,
            null, false, "美术已交付：512×768。终章 HE 他被带走问话");
        Add("prop_letter_written", 64, 64, AssetIntake.Kind.Pixel, 8, false,
            null, false, "美术已交付：64×64 一张有字的纸");
        Add("prop_mailbox", 64, 80, AssetIntake.Kind.Pixel, 10, false,
            null, false, "美术已交付：64×80 红色信箱");
        Add("prop_envelope_closed", 128, 128, AssetIntake.Kind.Pixel, 10, false,
            null, false, "美术已交付：512×512 信封，缩到 128");

        // ★ 被取消的：程序生成比手画好，而且它本来就只是"三种字的质感"
        // （死亡证明 16 / 医院记录 16 / 火化证明 17，靠渲染参数区分）
    }

    /// <summary>把槽位登记给 AssetIntake，并预热。</summary>
    public static void Install()
    {
        RegisterAll();
        AssetIntake.Warmup();
        foreach (var s in All)
        {
            AssetIntake.Expect(
                s.Key, s.W, s.H, s.Kind, s.MaxColors, s.AllowAlpha, s.Proc, s.Candidates);
        }
    }

    // ── 统计 ────────────────────────────────────────────────────────────

    public static int Total => All.Count;
    public static int ArtCount => All.FindAll(s => s.NeedsArt).Count;
    public static int ProcCount => All.FindAll(s => !s.NeedsArt && s.Proc != null).Count;
    public static int DrawnCount => All.FindAll(s => s.DrawnInCode).Count;
    public static int NoProcNoArt => All.FindAll(s => !s.NeedsArt && s.Proc == null && !s.DrawnInCode).Count;

    public static List<Slot> ArtOnly() => All.FindAll(s => s.NeedsArt);

    /// <summary>打印一份和 <c>doc/美术需求.md</c> 对账的结果。</summary>
    public static string Audit()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("════ 资产槽位对账 ════");
        // 三类互斥，别把"程序生成但同时标了 code"的重复计数
        int procOnly = All.FindAll(s => !s.NeedsArt && s.Proc != null && !s.DrawnInCode).Count;
        sb.AppendLine($"  槽位总数     {Total}");
        sb.AppendLine($"  美术要做     {ArtCount}   ← 这就是全部了");
        sb.AppendLine($"  程序生成     {procOnly}");
        sb.AppendLine($"  代码绘制/叠字 {DrawnCount}（不占贴图槽）");
        sb.AppendLine();

        sb.AppendLine("  美术要做的（这就是全部了）：");
        foreach (var s in ArtOnly())
            sb.AppendLine($"    {s.Key,-30} {s.W}×{s.H}  {s.Reason}");

        var missing = All.FindAll(s => !s.NeedsArt && s.Proc == null && s.Candidates.Length == 0);
        if (missing.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  ★ 既没美术也没程序顶上的槽位（会走纯色兜底）：");
            foreach (var s in missing)
                sb.AppendLine($"    {s.Key}");
        }
        return sb.ToString();
    }
}