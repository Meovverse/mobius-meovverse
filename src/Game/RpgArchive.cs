using Godot;

namespace GraveCanTell.Game;

/// <summary>实验：第四章「韩梅」之前的走位段（map_archive_room，已裁掉透明区）。</summary>
public partial class RpgArchive : RpgWalk
{
    protected override string ChapterName => "第四章 · 走访途中";
    protected override string MapPath => "res://assets/textures/map_archive_room.png";
    protected override Vector2 SpawnWorld => new(150, 345);
    protected override string[] Guidance => new[]
    {
        "方向键走动　　走到右边那排书架前，按 Enter",
    };
    protected override RpgTarget[] Targets => new[]
    {
        // #45：出生点和目标点放在**同一条过道**里（不用跨书架），避免"往书架上走不动"。
        new RpgTarget("抽出那份卷宗", new Vector2(400, 345), ChapterFlow.NextContent()),
    };
}
