using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MoShi.Core;

/// <summary>玩家在这一周目里"记下"的东西。账本即存档 —— 这个对象就是 save.json。</summary>
public sealed class RunState
{
    /// <summary>终章要用到的 flag。名字和 <c>doc/玩法设计.md</c> 第十三节一一对应。</summary>
    public static class Flag
    {
        /// <summary>账本第一页背面那行"厚料立碑…"是否读过。HE 的前提。</summary>
        public const string BacknoteRead = "ledger_backnote_read";

        /// <summary>第一张拓片是否还在身上。</summary>
        public const string RubbingCarried = "rubbing_carried";

        /// <summary>第五章复印件是否已入档。HE 完整版 vs 弱化版。</summary>
        public const string DeedCached = "deed_cached";

        /// <summary>第四章选了哪个回答。</summary>
        public const string Ch04Answer = "ch04_answer";

        /// <summary>终章底座是否已被砸开。</summary>
        public const string BaseExposed = "base_exposed";

        /// <summary>第一章是否已经清碑（不可逆）。</summary>
        public const string StoneCleaned = "stone_cleaned";

        /// <summary>拓片是否还在工��箱里（放回去了 = 没带走证据）。</summary>
        public const string RubbingLeft = "rubbing_left";

        /// <summary>第一章三段是否都自己完成了（没看料头提示）。</summary>
        public const string SelfFound = "self_found";
    }

    // ── 账本 ────────────────────────────────────────────────────────────

    /// <summary>玩家抄进账本的每一行。账本的厚度就是进度。</summary>
    public List<LedgerLine> Ledger { get; set; } = new();

    // ── 旗标 ────────────────────────────────────────────────────────────

    public HashSet<string> Flags { get; set; } = new();

    /// <summary>第四章的回答：0=沉默 1="是您的" 2="是同名的人" 3=说实话</summary>
    public int Ch04Answer
    {
        get => Flags.Contains(Flag.Ch04Answer) ? Misc.GetValueOrDefault("ch04", 0) : -1;
        set { Flags.Add(Flag.Ch04Answer); Misc["ch04"] = value; }
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