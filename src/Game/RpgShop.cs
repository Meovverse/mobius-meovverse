using Godot;

namespace MoShi.Game;

/// <summary>实验：第三章「两个日期」之前的走位段（map_shop）。走到灯下按 Enter 进对照。</summary>
public partial class RpgShop : RpgWalk
{
    protected override string ChapterName => "第三章 · 走访途中";
    protected override string MapPath => "res://assets/textures/map_shop.png";
    protected override Vector2 SpawnWorld => new(240, 420);
    protected override string[] Guidance => new[]
    {
        "方向键走动　　走到左下角的账台前，按 Enter",
    };
    protected override RpgTarget[] Targets => new[]
    {
        // #41：工作台是**左下角那张桌子**，目标点落在桌前能站的位置
        new RpgTarget("到工作台前", new Vector2(210, 655), ChapterFlow.NextContent()),
    };
}
