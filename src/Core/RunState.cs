using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MoShi.Core;

/// <summary>玩家在这一周目里"记下"的东西。账本即存档 —— 这个对象就是 save.json。</summary>
public sealed class RunState
{
    /// <summary>
    /// flag 表。★ 与 doc/剧本.md 的 flag 一一对应——名字全部是策划在新剧本里
    /// 写死的（clue_01..18 / ch1..5_choice / money_taken / evidence_fixed …），
    /// 本类跟着它走，不要自己加。分场里出现过的：
    ///   序章 clue_16 + ledger_p6 ｜ 一章 ch1_choice clue_01/02 ｜ 二章 ch2_choice clue_02..05
    ///   三章 ch3_choice clue_06..09 suhang_alerted ｜ 四章 ch4_choice clue_10..12 hanmei_line_dropped
    ///   五章 ch5_choice clue_13/14/17/18 ｜ 六章 clue_15 ｜ 七章 money_taken asked_hanmei
    ///   八章 rush_to_police ｜ 九章 timeline_built ｜ 十一章 evidence_fixed ｜ 十二章 base_exposed
    ///   判定层 stone_altered identity_swap
    /// </summary>
    public static class Flag
    {
        // ── 18 条线索（§2 线索表）。拿到即 Set；登记本右页的一行就是这条 ──
        public const string Clue01 = "clue_01";   // 「16」存在磨痕
        public const string Clue02 = "clue_02";   // 墓园系统 5-17
        public const string Clue03 = "clue_03";   // 医院记录 5-17
        public const string Clue04 = "clue_04";   // 殡仪馆记录 5-17
        public const string Clue05 = "clue_05";   // 墓位提前购买
        public const string Clue06 = "clue_06";   // 苏航是保险受益人
        public const string Clue07 = "clue_07";   // 公司巨额债务
        public const string Clue08 = "clue_08";   // 死亡日期影响保险
        public const string Clue09 = "clue_09";   // 保险材料改成 5-16
        public const string Clue10 = "clue_10";   // 韩梅调查资金流向
        public const string Clue11 = "clue_11";   // 韩梅掌握苏航异常资金
        public const string Clue12 = "clue_12";   // 韩梅并非正常辞职
        public const string Clue13 = "clue_13";   // 韩湘早于韩梅死亡
        public const string Clue14 = "clue_14";   // 韩梅没有主动变成韩湘
        public const string Clue15 = "clue_15";   // 石料 4 月到货
        public const string Clue16 = "clue_16";   // 老吴 2006 登记本（最关键）
        public const string Clue17 = "clue_17";   // 韩湘死亡证明
        public const string Clue18 = "clue_18";   // 韩梅真实身份资料

        /// <summary>序章登记：{苏兰, 2006-05-17, 2006-05-17}。值放 <see cref="Ledger"/>。</summary>
        public const string LedgerP6 = "ledger_p6";

        // ── 判定层 ──
        public const string StoneAltered = "stone_altered";     // 一章：碑面被磨改过
        public const string IdentitySwap = "identity_swap";     // 五章：死人替活人承担身份

        // ── 状态 ──
        public const string SuhangAlerted = "suhang_alerted";       // 三章选 C：打草惊蛇
        public const string HanmeiLineDropped = "hanmei_line_dropped"; // 四章选 C：结局二判定用
        public const string AskedHanmei = "asked_hanmei";           // 七章「韩梅呢？」问出口
        public const string MoneyTaken = "money_taken";             // 七章收钱 → 结局三
        public const string RushToPolice = "rush_to_police";        // 八章急着报警 → 结局四
        public const string TimelineBuilt = "timeline_built";       // 九章时间线排齐
        public const string EvidenceFixed = "evidence_fixed";       // 十一章选 A：原件固定住
        public const string BaseExposed = "base_exposed";           // 十二章砸开底座（前置 EvidenceFixed）
    }

    // ── 调查选择（§1 结局表的触发条件都从这里读） ──────────────────────

    /// <summary>
    /// 第几章的调查选择。值存在 <see cref="Misc"/>，存 'A'/'B'/'C' 的 char 码。
    /// ★ 键名就是剧本 flag 表里的 ch1_choice..ch5_choice，别自创缩写——
    ///   存档要和策划口径逐字对上，不然接剧情系统时对不上号。
    /// </summary>
    public char GetChoice(int chapter) =>
        Misc.TryGetValue($"ch{chapter}_choice", out int v) ? (char)v : '\0';

    public void SetChoice(int chapter, char c) => Misc[$"ch{chapter}_choice"] = c;

    /// <summary>结局一 · 没发现碑的问题：一章 C（当场离开），或二章 C 且此前也 C。</summary>
    public bool EndingOneFired => GetChoice(1) == 'C' || (GetChoice(2) == 'C' && GetChoice(1) == 'C');

    /// <summary>结局二 · 被隐藏的身份：四章 C（放弃韩梅线）或五章 C（认为韩湘就是韩梅）。</summary>
    public bool EndingTwoFired => Has(Flag.HanmeiLineDropped) || GetChoice(5) == 'C';

    /// <summary>结局三 · 妥协：七章收了信封。</summary>
    public bool EndingThreeFired => Has(Flag.MoneyTaken);

    /// <summary>结局四 · 冲动：八章没固定证据就去警局。</summary>
    public bool EndingFourFired => Has(Flag.RushToPolice);

    /// <summary>
    /// 真结局四条件（§1）：①发现碑面异常 ②发现身份异常 ③拒绝收买 ④报警前固定证据。
    /// 十二章砸底座只是演出，判定在这里。
    /// </summary>
    public bool TrueEndingReady =>
        Has(Flag.StoneAltered) && Has(Flag.IdentitySwap)
        && !Has(Flag.MoneyTaken) && Has(Flag.EvidenceFixed);

    // ── 账本 ────────────────────────────────────────────────────────────

    /// <summary>玩家抄进账本的每一行。账本的厚度就是进度。</summary>
    public List<LedgerLine> Ledger { get; set; } = new();

    // ── 旗标 ────────────────────────────────────────────────────────────

    public HashSet<string> Flags { get; set; } = new();

    /// <summary>第七章「韩梅呢？」的回答。0=沉默 1=问出口 2=其它。等价 flag：asked_hanmei。</summary>
    public int ChapterAnswer
    {
        get => Misc.TryGetValue("s15_answer", out int v) ? v : -1;
        set => Misc["s15_answer"] = value;
    }

    public int OverWipedCount
    {
        get => Misc.GetValueOrDefault("over_wiped", 0);
        set => Misc["over_wiped"] = value;
    }

    // ── 随身物 ──────────────────────────────────────────────────────────

    /// <summary>身上的证物。key 见 <see cref="Evidence"/>。</summary>
    public List<string> Carried { get; set; } = new();

    // ── 杂项计数 ────────────────────────────────────────────────────────

    public Dictionary<string, int> Misc { get; set; } = new();

    // ── 查询 ────────────────────────────────────────────────────────────

    public bool Has(string flag) => Flags.Contains(flag);

    public void Set(string flag) => Flags.Add(flag);

    public void Clear(string flag) => Flags.Remove(flag);

    public bool Carries(string evidenceId) => Carried.Contains(evidenceId);

    public void Take(string evidenceId)
    {
        if (!Carried.Contains(evidenceId))
            Carried.Add(evidenceId);
    }

    public void Drop(string evidenceId) => Carried.Remove(evidenceId);

    // ── 存档 ────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>集成测试把这里换成 test 档，绝不允许碰玩家的真实进度。</summary>
    public static string SavePath = "user://ledger_save.json";

    public void Save()
    {
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            if (f == null)
            {
                GD.PushError($"账本写不进去：{SavePath} {Godot.FileAccess.GetOpenError()}");
                return;
            }
            f.StoreString(JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (Exception e)
        {
            GD.PushError($"存账失败：{e.Message}");
        }
    }

    public static RunState Load()
    {
        if (!Godot.FileAccess.FileExists(SavePath))
            return new RunState();

        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            var s = f?.GetAsText();
            if (string.IsNullOrWhiteSpace(s))
                return new RunState();
            return JsonSerializer.Deserialize<RunState>(s, JsonOpts) ?? new RunState();
        }
        catch (Exception e)
        {
            GD.PushWarning($"账本读不出来，开一个新的：{e.Message}");
            return new RunState();
        }
    }

    public static bool HasSave() => Godot.FileAccess.FileExists(SavePath);

    public static void DeleteSave()
    {
        if (HasSave())
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
    }

    public void Reset()
    {
        Ledger.Clear();
        Flags.Clear();
        Carried.Clear();
        Misc.Clear();
    }
}