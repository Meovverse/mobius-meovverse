using GraveCanTell.Core;

namespace GraveCanTell.Game;

/// <summary>
/// 章节路由：只认 flag，不认场景自己——任何场景问"现在该演哪出"都走这里。
/// 顺序对齐剧本 v3：序章(2006) → 一章(碑) → 二章(查档) → 三章(两个日期) →
/// 四章(韩梅) → 五章(韩湘) → 六章(石料) → 七章(苏航) → 八章(选择) →
/// 九章(拼合) → 十章(演出) → 十一章(固定证据) → 终章(砸) → 十三(公开) → 尾声。
/// </summary>
public static class ChapterFlow
{
    public const string Entry2026 = "res://scenes/RPGExplo.tscn";

    /// <summary>路由目标 → 人话章节名（标题界面必须告诉玩家"点开始去哪"）。</summary>
    public static string Label(string scenePath)
    {
        if (scenePath.EndsWith("ChPrologue.tscn")) return "序章 · 石头不会撒谎";
        if (scenePath.EndsWith("Ch01.tscn")) return "第一章 · 碑上的名字";
        if (scenePath.EndsWith("Ch02.tscn") || scenePath.EndsWith("RpgOffice.tscn")) return "第二章 · 查档";
        if (scenePath.EndsWith("Ch03.tscn") || scenePath.EndsWith("RpgShop.tscn")) return "第三章 · 两个日期";
        if (scenePath.EndsWith("Ch04.tscn") || scenePath.EndsWith("RpgArchive.tscn")) return "第四章 · 韩梅";
        if (scenePath.EndsWith("Ch05.tscn")) return "第五章 · 死者韩湘";
        if (scenePath.EndsWith("Ch06.tscn")) return "第六章 · 石料";
        if (scenePath.EndsWith("Ch07.tscn")) return "第七章 · 苏航的压力";
        if (scenePath.EndsWith("Ch08.tscn")) return "第八章 · 最危险的选择";
        if (scenePath.EndsWith("Ch09.tscn")) return "第九章 · 真相拼合";
        if (scenePath.EndsWith("Ch10.tscn")) return "第十章 · 真相";
        if (scenePath.EndsWith("Ch11.tscn")) return "第十一章 · 最后的选择";
        if (scenePath.EndsWith("Ch12.tscn")) return "第十二章 · 立碑";
        if (scenePath.EndsWith("Ch13.tscn")) return "第十三章 · 真相公开";
        if (scenePath.EndsWith("Epilogue.tscn")) return "尾声";
        return "墓园";
    }

    /// <summary>**逻辑上的下一章内容场**（永不是走位段）。走位段用它当目标点。</summary>
    public static string NextContent()
    {
        var s = RunState.Load();
        if (!s.Has(RunState.Flag.Clue16)) return "res://scenes/ChPrologue.tscn";
        if (!s.Has(RunState.Flag.StoneAltered) && !s.Has(RunState.Flag.Clue02)) return "res://scenes/Ch01.tscn";
        if (!s.Has(RunState.Flag.Clue05) && s.GetChoice(2) != 'C') return "res://scenes/Ch02.tscn";
        if (!s.Has(RunState.Flag.Clue06)) return "res://scenes/Ch03.tscn";
        if (!s.Has(RunState.Flag.Clue10)) return "res://scenes/Ch04.tscn";
        if (!s.Has(RunState.Flag.Clue13)) return "res://scenes/Ch05.tscn";
        if (!s.Has(RunState.Flag.Clue15)) return "res://scenes/Ch06.tscn";
        if (!s.Has("ch7_done")) return "res://scenes/Ch07.tscn";
        if (!s.Has("ch8_done")) return "res://scenes/Ch08.tscn";
        if (!s.Has(RunState.Flag.TimelineBuilt)) return "res://scenes/Ch09.tscn";
        if (!s.Has("ch10_done")) return "res://scenes/Ch10.tscn";
        if (!s.Has(RunState.Flag.EvidenceFixed)) return "res://scenes/Ch11.tscn";
        if (!s.Has(RunState.Flag.BaseExposed)) return "res://scenes/Ch12.tscn";
        if (!s.Has("ch13_done")) return "res://scenes/Ch13.tscn";
        if (!s.Has("epilogue_done")) return "res://scenes/Epilogue.tscn";
        return "res://scenes/Boot.tscn";   // 全通：回标题
    }

    /// <summary>
    /// 场景流入口（各章 ChangeSceneToFile 都用它）：在内容场外面套一层**走位段**——
    /// §实验：二章（办公室）、四章（档案室）、三/六/八/十一章（铺子）先在地图上走到目标点。
    /// 走位段自身再用 <see cref="NextContent"/> 回到内容场。
    /// </summary>
    public static string Next()
    {
        var content = NextContent();
        if (content.EndsWith("Ch02.tscn")) return "res://scenes/RpgOffice.tscn";
        if (content.EndsWith("Ch04.tscn")) return "res://scenes/RpgArchive.tscn";
        foreach (var shop in new[] { "Ch03.tscn", "Ch06.tscn", "Ch08.tscn", "Ch11.tscn" })
            if (content.EndsWith(shop)) return "res://scenes/RpgShop.tscn";
        return content;
    }
}
