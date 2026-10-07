using Godot;

namespace MoShi.Game;

/// <summary>实验：第二章「查档」之前的走位段（map_office）。走到电脑前按 Enter 进查档。</summary>
public partial class RpgOffice : RpgWalk
{
    protected override string ChapterName => "第二章 · 走访途中";
    protected override string MapPath => "res://assets/textures/map_office.png";
    protected override Vector2 SpawnWorld => new(460, 640);
    protected override string[] Guidance => new[]
    {
        "方向键走动　　走到「▼ 打开墓园系统」前，按 Enter",
    };
    protected override RpgTarget[] Targets => new[]
    {
        new RpgTarget("打开墓园系统", new Vector2(500, 520), "res://scenes/Ch02.tscn"),
    };
}
