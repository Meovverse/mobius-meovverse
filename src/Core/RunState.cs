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
    /// flag 表。★ 与 doc/剧本.md §3「flag 总表」一一对应 ——
    /// 那是策划给的，本类跟着它走，不要自己加。
    /// </summary>
    public static class Flag
    {
        /// <summary>S6 序章登记 / S10 系统复核 → 终章"砸底座"的知识前提。</summary>
        public const string LedgerP6 = "ledger_p6";

        /// <summary>S10 翻到账本第一页背面那行"厚料立碑…"。★ 没读 → 终章没有"砸底座"。</summary>
        public const string BacknoteRead = "ledger_backnote_read";

        /// <summary>S9 每一次擦过头。</summary>
        public const string OverWiped = "over_wiped_count";

        /// <summary>S9 看完两处改动 → 进入第二章。</summary>
        public const string SurfaceAnomaly = "surface_anomaly_found";

        /// <summary>S7 真相层：碑面被磨改过。</summary>
        public const string StoneAltered = "stone_altered";

        /// <summary>S10 系统复核 / 成交日期。第三章用。</summary>
        public const string SystemVerified = "system_verified";

        /// <summary>S10 墓位成交日期（2006-04-26）。</summary>
        public const string DeedDate = "deed_date";

        /// <summary>S11 几张纸比对过了。</summary>
        public const string PapersCompared = "papers_compared";

        /// <summary>S12 查到韩梅这个人。</summary>
        public const string HanmeiFound = "hanmei_found";

        /// <summary>S12 确认她失踪了。</summary>
        public const string HanmeiMissing = "hanmei_missing";

        /// <summary>S13 查到韩湘这个身份。★ 第十章的突破口。</summary>
        public const string HanxiangIdentity = "hanxiang_identity_found";

        /// <summary>S14 石料的来路。</summary>
        public const string StoneLotTraced = "stone_lot_traced";

        /// <summary>S15 苏航来过。</summary>
        public const string ConfrontedSuhang = "confronted_suhang";

        /// <summary>S15 那个信封收了没有 → 结局的语气。</summary>
        public const string TookEnvelope = "took_envelope";

        /// <summary>S17 底座砸开了。</summary>
        public const string BaseExposed = "base_exposed";

        /// <summary>若保留拓片机制（S9）。</summary>
        public const string RubbingCarried = "rubbing_carried";

        /// <summary>S15「韩梅呢？」那句话，玩家打出来了没有。</summary>
        public const string AskedHanmei = "asked_hanmei";

        /// <summary>S20 尾声「证明呢？」。</summary>
        public const string AskedProof = "asked_proof";

        /// <summary>第一章是否已经清碑（不可逆）。</summary>
        public const string StoneCleaned = "stone_cleaned";
    }

    // ── 账本 ────────────────────────────────────────────────────────────

    /// <summary>玩家抄进账本的每一行。账本的厚度就是进度。</summary>
    public List<LedgerLine> Ledger { get; set; } = new();

    // ── 旗标 ────────────────────────────────────────────────────────────

    public HashSet<string> Flags { get; set; } = new();

    /// <summary>S15 里玩家的回答。0=沉默（5 秒不输入）1="韩梅呢？" 2=其它</summary>
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

    private const string SavePath = "user://ledger_save.json";

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