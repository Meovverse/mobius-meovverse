using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 音频登记表 —— doc/音频岗需求.md 的机器可读版本，和 <see cref="SlotRegistry"/> 同构。
///
/// 游戏代码只按 ID 取声，文件名/采样率/位深怎么改都不惊动玩法侧。
/// 音频岗每交付一批，在这里加一行；缺的 ID 由 <see cref="Audit"/> 报出来。
///
/// ★ 响度分级（音频需求 §1.3）：S0 触觉 −20 / S1 操作 −14 / S2 事件 −8 / A 环境 −30 dBFS 峰值。
///   2026-10-07：**全部交付件已按此峰值目标归一化**（ffmpeg 峰值归一，原件备份在
///   /tmp/opencode/audio_orig）。下面的 MeasuredPeak 即归一化后的实测峰值；
///   <see cref="GainDb"/> 退化为 ±0.5dB 级兜底（编解码残余过冲）。各行 Note 里的
///   "超档/偏轻 XdB"是归一化前的历史记录，保留备查。
///   仍待复听的是**时长/多 take**类问题（归一化不解决）。
/// ★ 循环由程序控制（音频需求 §1.5）：loop_* 导入设置必须保持 Loop 关闭，
///   按住时 <see cref="StartHold"/>（播放器自身开环），松手 <see cref="StopHold"/> 立刻停。
///   导入期循环会造成"松手了声音还在响"——这条是死规矩。
/// </summary>
public static class AudioIndex
{
    public enum Bus { Sfx, Ambience, Music }

    /// <summary>响度级别（峰值目标，dBFS）。</summary>
    public enum Tier { S0, S1, S2, Amb, Music }

    public static float TierPeak(Tier t) => t switch
    {
        Tier.S0 => -20f, Tier.S1 => -14f, Tier.S2 => -8f,
        Tier.Music => -16f,     // 标题曲专用，正文永不响
        _ => -30f,
    };

    /// <param name="MeasuredPeak">导入实测峰值 dBFS；NaN = 未实测（不做播放端补偿）。</param>
    public sealed record Cue(string Id, string File, Bus Bus, Tier Tier, float MeasuredPeak, string Note = "");

    // ── 已交付（2026-10-06 第一批，验收实测值写在这里） ──────────────────
    public static readonly List<Cue> Delivered = new()
    {
        new("sfx_brush_pile",   "res://assets/audio/sfx_brush_pile.wav",   Bus.Sfx, Tier.S0, -20.5f,
            "★ 有效内容只有 ~0.03s（需求 0.3s），尾部 0.64s 静音，疑似导出截错，待复听"),
        new("sfx_over_wipe",    "res://assets/audio/sfx_over_wipe.wav",    Bus.Sfx, Tier.S0, -20.5f, "超档 6.9dB，播放端已压回"),
        new("sfx_trace_start",  "res://assets/audio/sfx_trace_start.wav",  Bus.Sfx, Tier.S0, -20.5f, "超档 9.5dB，播放端已压回"),
        new("sfx_trace_break",  "res://assets/audio/sfx_trace_break.wav",  Bus.Sfx, Tier.S0, -20.5f,  "超档 12.8dB，播放端已压回"),
        new("sfx_trace_done",   "res://assets/audio/sfx_trace_done.wav",   Bus.Sfx, Tier.S0, -20.5f, "超档 4.1dB，播放端已压回"),
        new("loop_brush_stone", "res://assets/audio/loop_brush_stone.ogg", Bus.Ambience, Tier.S0, -20.0f,
            "时长 1.57s，低于需求的 2–4s，接缝两端静音问题不大，重导时注意"),
        new("loop_chisel_run",  "res://assets/audio/loop_chisel_run.ogg",  Bus.Ambience, Tier.S0, -20.8f,
            "★ 尾部约 1s 全静音、头部有能量——程序循环时会周期性'空一拍'，待音频岗确认是否导出尾巴"),
        new("amb_rain",         "res://assets/audio/amb_rain.ogg",         Bus.Ambience, Tier.Amb, -30.5f,
            "★ 四批换的 55.8s 新版（旧 12s 版作废）。超 A 档(-30) 13.6dB——"
            + "环境音规范是'几乎听不见'，请重导；源是误名 .ogg.wav 的 24bit WAV，已转 vorbis"),
        // ── 第四批（wait_for_integration，程序端标准化：sfx 剪尾转 16bit、amb 转 vorbis）──
        new("sfx_peel_paper",   "res://assets/audio/sfx_peel_paper.wav",   Bus.Sfx, Tier.S1, -14.5f,
            "2026-10-07 重交：已裁到 0.93s（原 5.0s），归一化 -14.5"),
        new("sfx_caliper_open", "res://assets/audio/sfx_caliper_open.wav", Bus.Sfx, Tier.S1, -14.5f,
            "2026-10-07 重交：已裁到 0.85s（原 3.5s），归一化 -14.5"),
        new("sfx_paper_lift",   "res://assets/audio/sfx_paper_lift.wav",   Bus.Sfx, Tier.S1, -14.5f,
            "2026-10-07 重交：已裁到 1.21s（原 6.0s），归一化 -14.5"),
        new("loop_caliper_slide","res://assets/audio/loop_caliper_slide.ogg",Bus.Ambience, Tier.S0, -20.5f,
            "12.5s（需求 2–3s，长版无妨）；偏轻 6.4dB"),
        new("amb_office_night", "res://assets/audio/amb_office_night.ogg", Bus.Ambience, Tier.Amb, -30.4f,
            "54.5s；超 A 档 10.4dB，与 amb_rain 同批重导"),

        // ── 第二批交付（15:13）。同时处理了冲突：对方把整批塞进 bgs/ 并用回 A 前缀，
        //    与根目录规范名逐字节相同（md5 已对）→ 删重复；A12 上批是 80B 坏文件，
        //    这批重交是真货（327KB）。bgm/Main Theme.wav 由老板拍板收编为标题曲，
        //    见 title_theme。 ──
        new("sfx_rub_blur",     "res://assets/audio/sfx_rub_blur.wav",     Bus.Sfx, Tier.S0, -20.5f,
            "上批坏文件的重交版。超档 11.6dB；有效 0.17s（需求 0.5s 的'闷掉'衰减偏快，复听）"),
        new("sfx_paper_place",  "res://assets/audio/sfx_paper_place.wav",  Bus.Sfx, Tier.S1, -14.5f,
            "超档 9.2dB；单声道（其余是立体声，混着没事但记一笔）"),
        new("sfx_paper_slide",  "res://assets/audio/sfx_paper_slide.wav",  Bus.Sfx, Tier.S1, -14.5f,
            "响度达标。全长 1.97s、有效仅 0.34s——'摩擦的起点和停'的起点段疑似被掐，复听"),
        new("sfx_page_turn",    "res://assets/audio/sfx_page_turn.wav",    Bus.Sfx, Tier.S1, -14.5f,
            "2026-10-07 重交：已裁到 0.75s（原 5.94s），归一化 -14.5"),
        new("sfx_pen_write",    "res://assets/audio/sfx_pen_write.wav",    Bus.Sfx, Tier.S1, -14.5f,
            "时长 1.18s 合理；偏轻 8.9dB（补偿只压不抬，等重导补齐）"),
        // ── 第三批交付（merge c05d768，11 件 B_/C_/D_ 前缀 → 已全部转规范名）。
        //    ★ D8_sfx_wheelchair 是文档标了封存仍交上来的：已拒删，见 音频岗需求 §六之三。──
        new("sfx_write_confirm", "res://assets/audio/sfx_write_confirm.wav", Bus.Sfx, Tier.S1, -14.5f, "账本落笔确认。达标"),
        new("sfx_paper_tear",    "res://assets/audio/sfx_paper_tear.wav",    Bus.Sfx, Tier.S2, -8.5f, "超 S2 档 1.5dB，播放端压回"),
        new("sfx_paper_burn",    "res://assets/audio/sfx_paper_burn.wav",    Bus.Sfx, Tier.S2, -8.5f,
            "2026-10-07 重交：已裁到 2.36s（原 5.96s），归一化 -8.5"),
        new("sfx_caliper_lock",  "res://assets/audio/sfx_caliper_lock.wav",  Bus.Sfx, Tier.S1, -14.5f, "超档 6.7dB；'咔'短促达标"),
        new("sfx_hammer_chisel", "res://assets/audio/sfx_trace_start.wav", Bus.Sfx, Tier.S2, -20.5f,
            "2026-10-07：**沿用 sfx_trace_start 素材**（音频岗定；原 hammer_chisel 3.94s 作废）"),
        new("sfx_hammer_swing",  "res://assets/audio/sfx_hammer_swing.wav",  Bus.Sfx, Tier.S2, -8.5f, "挥空风声，达标"),
        new("sfx_shovel_scrape", "res://assets/audio/sfx_shovel_scrape.wav", Bus.Sfx, Tier.S2, -8.5f, "剧本绑定件提前交，先收；偏轻 9.6dB"),
        new("sfx_camera_shutter", "res://assets/audio/sfx_camera_shutter.wav", Bus.Sfx, Tier.S2, -8.5f,
            "★ 全篇最响文件，超档 6.5dB——快门理应盖不过砸锤，播放端强制压回"),
        new("sfx_crowd_murmur",  "res://assets/audio/sfx_crowd_murmur.wav",  Bus.Ambience, Tier.Amb, -30.5f,
            "25s 人群低语，按环境量级收（走 Ambience 总线）；终章清明用"),
        new("sfx_tear_stop",     "res://assets/audio/sfx_tear_stop.wav",     Bus.Sfx, Tier.S2, -8.5f,
            "★ 需求文档里没有这个 ID——按名猜是撕纸的'停'尾（配 sfx_paper_tear 用）。已入库备用，用途请音频岗确认"),
        new("sfx_stone_crack", "res://assets/audio/sfx_stone_crack.mp3", Bus.Sfx, Tier.S2, -8.4f,
            "★ 四批。标题→游戏的转场碎裂音。峰 -1.2dBFS：又一个超档（压 6.8dB 播放）。mp3 格式入库（Godot 原生支持）"),
        new("amb_common_night", "res://assets/audio/amb_common_night.ogg", Bus.Ambience, Tier.Amb, -30.5f,
            "★ 音频岗的万金油夜环境（109.6s）。任何缺环境音的场景直接用它顶——"
            + "amb_graveyard/amb_archive 未到位时由 AudioIndex 自动回退到此件"),
        new("title_theme",      "res://assets/audio/title_theme.ogg",      Bus.Music, Tier.Music, -16.7f,
            "★ 标题画面专属——'全篇没有BGM'的唯一例外（2026-10-06 拍板）。" +
            "源为 18.4MB 48kHz WAV，已重编码 192k vorbis（64s/1MB）；进游戏即停，正文永不响"),

        // ── 第五批（2026-10-07）：补齐三处缺口 + 两件新增。整批（连同历史件）已按
        //    §1.3 峰值目标**归一化**（见类注释），下面实测即为归一化后峰值。──
        new("amb_graveyard",   "res://assets/audio/amb_graveyard.ogg",  Bus.Ambience, Tier.Amb, -30.2f,
            "20.2s 墓园环境（终章清明用）。原峰 -6.3 → 归一化 -30"),
        new("amb_archive",     "res://assets/audio/amb_archive.mp3",    Bus.Ambience, Tier.Amb, -30.6f,
            "55.8s 档案室环境。原峰 -16.4 → 归一化 -30"),
        new("sfx_base_crack",  "res://assets/audio/sfx_base_crack.mp3", Bus.Sfx, Tier.S2, -8.2f,
            "★ 终章砸底座、原始石面露出（第十二章用）。原峰 0.0 → 归一化 -8"),
        new("loop_cement_chip","res://assets/audio/loop_cement_chip.mp3",Bus.Ambience,Tier.S0, -20.5f,
            "★ 新件，不在需求表内——'水泥碎屑'按住循环，用途请音频岗确认（按 loop_ 约定走 Ambience/S0）"),
        new("ending_theme",    "res://assets/audio/ending_theme.mp3",   Bus.Music, Tier.Music, -16.5f,
            "★ 通关制作名单 BGM（`Credits` 用；正文不响，只在名单页循环）"),
        // 音频岗定：sfx_stone_grind **沿用 loop_chisel_run 的素材**（同一文件的两个 ID）。
        new("sfx_stone_grind", "res://assets/audio/loop_chisel_run.ogg", Bus.Sfx, Tier.S0, -20.8f,
            "沿用 loop_chisel_run 素材（2026-10-07 音频岗定）；作为一次性研磨声"),
    };

    // ── 需求全集（音频岗需求 §一~§四 + 分镜稿引用到的 ID）──缺谁，催谁 ──
    public static readonly string[] Required =
    {
        // 擦 / 描 / 拓
        "loop_brush_stone", "sfx_brush_pile", "sfx_over_wipe",
        "sfx_trace_start", "loop_chisel_run", "sfx_trace_break", "sfx_trace_done",
        "sfx_rub_tap_light", "sfx_rub_tap_mid", "sfx_rub_tap_heavy", "sfx_rub_blur", "sfx_peel_paper",
        // 对 / 量 / 账本
        "sfx_paper_slide", "sfx_paper_lift", "sfx_paper_place",
        "sfx_caliper_open", "loop_caliper_slide", "sfx_caliper_lock",
        "sfx_page_turn", "sfx_pen_write", "sfx_write_confirm",
        // 不可逆事件
        "sfx_stone_grind", "sfx_base_crack", "sfx_stone_drop",
        "sfx_paper_tear", "sfx_paper_burn", "sfx_hammer_swing", "sfx_hammer_chisel",
        // 环境
        "amb_graveyard", "amb_rain", "amb_office_night", "amb_archive",
    };

    public static Cue Find(string id) => Delivered.Find(c => c.Id == id);

    /// <summary>取声：缺专用环境音时回退到万金油 CommomNight（音频岗约定）。</summary>
    public static Cue FindOrFallback(string id)
    {
        var c = Find(id);
        if (c != null) return c;
        if (id.StartsWith("amb_")) return Find("amb_common_night");
        return null;
    }

    /// <summary>需求里还没交付的 ID（测试与催更共用这一个口径）。</summary>
    public static System.Collections.Generic.List<string> Missing()
    {
        var l = new System.Collections.Generic.List<string>();
        foreach (var r in Required) if (Find(r) == null) l.Add(r);
        return l;
    }

    /// <summary>播放端响度补偿：只向下压，不向上抬（抬了会削顶，且掩盖超档问题）。</summary>
    public static float GainDb(Cue c) =>
        float.IsNaN(c.MeasuredPeak) ? 0f : Mathf.Min(0f, TierPeak(c.Tier) - c.MeasuredPeak);

    // ── 播放 ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, AudioStream> Loaded = new();
    private static readonly List<AudioStreamPlayer> Pool = new();
    private static AudioStreamPlayer _hold;
    private static readonly AudioStreamPlayer[] AmbPlayers = new AudioStreamPlayer[2];
    private static int _ambCur = -1;
    private static Node _host;

    private static Node Host()
    {
        if (_host != null && GodotObject.IsInstanceValid(_host)) return _host;
        _host = new Node { Name = "Audio" };
        // ★ 任何场景 _Ready 期间 Root 都在装配子节点，直接 AddChild 会失败且静默
        //   （音频从此变孤儿、整局静音）。一律延迟挂树。
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _host);

        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer { Bus = BusName(Bus.Sfx) };
            _host.AddChild(p); Pool.Add(p);
        }
        _hold = new AudioStreamPlayer { Bus = BusName(Bus.Ambience) };
        _host.AddChild(_hold);
        for (int i = 0; i < 2; i++)
        {
            AmbPlayers[i] = new AudioStreamPlayer { Bus = BusName(Bus.Ambience), VolumeDb = -80 };
            _host.AddChild(AmbPlayers[i]);
        }
        return _host;
    }

    private static string BusName(Bus b)
    {
        // 总线布局没装（跑单测/旧工程）时退回 Master，不炸。
        var want = b switch { Bus.Sfx => "SFX", Bus.Music => "Music", _ => "Ambience" };
        return AudioServer.GetBusIndex(want) >= 0 ? want : "Master";
    }

    private static AudioStream Load(string file)
    {
        if (!Loaded.TryGetValue(file, out var s))
        {
            s = ResourceLoader.Load<AudioStream>(file);
            if (s == null) GD.PushWarning($"音频加载失败：{file}");
            Loaded[file] = s;
        }
        return s;
    }

    /// <summary>一次性音效（S0/S1/S2）。找不到 ID 就安静地什么都不做——音效不该崩游戏。</summary>
    public static void Sfx(string id)
    {
        var cue = Find(id); if (cue == null) return;
        Host();
        if (!_host.IsInsideTree()) { Callable.From(() => Sfx(id)).CallDeferred(); return; }
        // ★ 集成测试抓出的真 bug：Sfx 从不 Init 池——先描后擦的纯键鼠路径 IndexOutOfRange
        var stream = Load(cue.File); if (stream == null) return;
        AudioStreamPlayer free = null;
        foreach (var p in Pool) if (!p.Playing) { free = p; break; }
        free ??= Pool[0];
        free.Bus = BusName(cue.Bus);
        free.VolumeDb = GainDb(cue);
        free.Stream = stream;
        free.Play();
    }

    /// <summary>按住型循环（loop_*）：程序开环，见类注释的死规矩。</summary>
    public static void StartHold(string id)
    {
        Host();
        if (!_host.IsInsideTree()) { Callable.From(() => StartHold(id)).CallDeferred(); return; }
        var cue = Find(id); if (cue == null) return;
        var stream = Load(cue.File); if (stream == null) return;
        Host();
        _hold.Bus = BusName(cue.Bus);
        _hold.VolumeDb = GainDb(cue);
        _hold.Stream = stream;
        // 循环点：ogg 的 AudioStreamOggVorbis.Loop 是资源属性，运行时可设
        // （设了就等于"这一条要循环"，和导入期 Loop 是两回事——不会"松手还在响"，
        //  因为松手走 StopHold）。wav 走 AudioStreamWav.LoopMode。
        SetLoop(stream);
        _hold.Play();
    }

    /// <summary>运行期开环（导入期 Loop 一律关）。ogg/wav/mp3 都照顾到。</summary>
    private static void SetLoop(AudioStream s)
    {
        if (s is AudioStreamOggVorbis ov) ov.Loop = true;
        else if (s is AudioStreamMP3 mp3) mp3.Loop = true;
        else if (s is AudioStreamWav w) w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
    }

    /// <summary>松手：立刻停，不淡出——"擦/描"的尾巴必须是手的尾巴。</summary>
    public static void StopHold() { Host(); _hold.Stop(); }

    /// <summary>环境音交叉淡化 2s（音频需求 §1.4 的"换气"）。</summary>
    public static void Ambience(string id)
    {
        var cue = Find(id); if (cue == null || id == CurrentAmb()) return;
        Host();
        if (!_host.IsInsideTree()) { Callable.From(() => Ambience(id)).CallDeferred(); return; }
        int next = (_ambCur + 1) % 2;
        var stream = Load(cue.File); if (stream == null) return;
        SetLoop(stream);   // 环境是长循环床（含 mp3 版 amb_archive），运行期开环
        var (inP, outP) = (AmbPlayers[next], AmbPlayers[_ambCur >= 0 ? _ambCur : next]);
        inP.Stream = stream;
        inP.VolumeDb = -80;
        inP.Play();
        var twIn = inP.CreateTween();
        twIn.TweenProperty(inP, "volume_db", GainDb(cue), 2f);
        if (_ambCur >= 0 && outP.Playing)
        {
            var twOut = outP.CreateTween();
            twOut.TweenProperty(outP, "volume_db", -80f, 2f);
            twOut.TweenCallback(Callable.From(outP.Stop));
        }
        _ambCur = next;
        _ambId = id;
    }

    private static string _ambId;
    private static string CurrentAmb() => _ambId;

    private static AudioStreamPlayer _music;

    /// <summary>
    /// 标题曲。'全篇没有 BGM'的唯一例外：只在标题画面响，循环。
    /// 调用方（Boot）进游戏前必须 StopTitle —— 正文一旦开始，Music 总线就该空着。
    /// </summary>
    private static bool _titleOn;

    public static void PlayTitle()
    {
        Host();
        if (_host != null && !_host.IsInsideTree()) { Callable.From(PlayTitle).CallDeferred(); return; }
        if (_music == null)
        {
            _music = new AudioStreamPlayer { Bus = BusName(Bus.Music) };
            // ★ 循环走手动重触发，不开资源级 Loop：
            //   ffmpeg 编的 vorbis 没有 VorbisMeta 的 LOOPSTART 元数据，
            //   Godot 开环 seek 会越界刷 "page_cursor >= page_data.size"。
            //   Finished 重播不依赖 seek 表，代价是循环点一声呼吸级间隙——标题画面无所谓。
            _music.Finished += () => { if (_titleOn && GodotObject.IsInstanceValid(_music)) _music.Play(); };
            _host.AddChild(_music);
        }
        var cue = Find("title_theme"); if (cue == null || _music.Playing) return;
        _music.Stream = Load(cue.File);
        _music.VolumeDb = GainDb(cue);
        _titleOn = true;
        _music.Play();
    }

    public static bool TestMusicPlaying => _music != null && _music.Playing;

    public static void StopTitle(float fadeSec = 1.2f)
    {
        _titleOn = false;
        if (_music == null || !_music.Playing) return;
        var tw = _music.CreateTween();
        tw.TweenProperty(_music, "volume_db", -80f, fadeSec);
        tw.TweenCallback(Callable.From(_music.Stop));
    }

    /// <summary>停止环境音（黑屏/结局定格用）。</summary>
    public static void SilenceAmbience(float fadeSec = 2f)
    {
        Host();
        for (int i = 0; i < 2; i++)
        {
            if (!AmbPlayers[i].Playing) continue;
            var p = AmbPlayers[i];
            var tw = p.CreateTween();
            tw.TweenProperty(p, "volume_db", -80f, fadeSec);
            tw.TweenCallback(Callable.From(p.Stop));
        }
        _ambCur = -1; _ambId = null;
    }

    // ── 验收报告 ────────────────────────────────────────────────────────

    public static string Audit()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("════ 音频验收 ════\n");
        int ok = 0, hot = 0;
        foreach (var c in Delivered)
        {
            bool exists = ResourceLoader.Exists(c.File);
            if (exists) ok++;
            string lv = float.IsNaN(c.MeasuredPeak) ? "未测"
                       : $"峰{c.MeasuredPeak:+0.0;-0.0}dBFS 标{TierPeak(c.Tier):+0;-0}";
            bool over = !float.IsNaN(c.MeasuredPeak) && c.MeasuredPeak > TierPeak(c.Tier) + 1f;
            if (over) hot++;
            sb.Append($"  {(exists ? "✓" : "✗")} {c.Id,-18} {c.Tier} {lv}{(over ? "  ★超档" : "")}\n     {c.Note}\n");
        }
        var missing = new List<string>();
        foreach (var r in Required) if (Find(r) == null) missing.Add(r);
        sb.Append($"  已交付 {ok}/{Delivered.Count}，需求 {Required.Length} 条，缺 {missing.Count} 条：{string.Join(" ", missing)}\n");
        sb.Append($"  超档需重导的：{hot} 条（播放端已补偿，但那只是遮丑布）\n");
        return sb.ToString();
    }
}
