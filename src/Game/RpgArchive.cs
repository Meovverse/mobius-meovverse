using Godot;

namespace MoShi.Game;

/// <summary>实验：第四章「韩梅」之前的走位段（map_archive_room，已裁掉透明区）。</summary>
public partial class RpgArchive : RpgWalk
{
    protected override string ChapterName => "第四章 · 走访途中";
    protected override string MapPath => "res://assets/textures/map_archive_room.png";
    protected override Vector2 SpawnWorld => new(250, 340);
    protected override string[] Guidance => new[]
    {
        "方向键走动　　走到「▼ 抽出那份卷宗」前，按 Enter",
    };
    protected override RpgTarget[] Targets => new[]
    {
        new RpgTarget("抽出那份卷宗", new Vector2(250, 185), ChapterFlow.NextContent()),
    };
}
