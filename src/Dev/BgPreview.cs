using Godot;
using GraveCanTell.Core;

namespace GraveCanTell.Dev;

/// <summary>
/// 场景底板预览。把 Program/Art 生成的背景原样写到 data/gen/preview_*.png，
/// 用来肉眼验收"程序版到底能不能看"——因为 headless 下截不了图。
///
/// 用法：
///   Godot --path &lt;proj&gt; res://scenes/BgPreview.tscn   ← 必须带窗口
/// </summary>
public partial class BgPreview : Control
{
    public override async void _Ready()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://data/gen/"));

        void Shot(string name, Image img)
        {
            var p = $"res://data/gen/preview_{name}.png";
            GD.Print($"{p}  {img.GetWidth()}×{img.GetHeight()}");
            img.SavePng(p);
        }

        Shot("bg_graveyard", ProcGen.BgGraveyard(640, 360));
        Shot("bg_shop_interior", Art.BgShopInterior(640, 360));
        Shot("bg_office", Art.BgOffice(640, 360));
        Shot("bg_archive", Art.BgArchive(640, 360));
        Shot("bg_counter", Art.BgCounter(640, 360));
        Shot("bg_shop", Art.BgShop(640, 360));

        // 道具与人物剪影拼在一张对照表里
        var sheet = Image.CreateEmpty(320, 360, false, Image.Format.Rgba8);
        sheet.Fill(ProcGen.Rgb(26, 26, 26, 0xff));
        var items = new (string, Image)[]
        {
            ("scrap", Art.PropScrapStone(96, 96)),
            ("toolbox", Art.PropToolbox(160, 100)),
            ("deed", Art.PropDeedLedger(300, 360)),
            ("form", Art.PropArchiveForm(280, 200)),
        };
        int yy = 4;
        foreach (var (tag, im) in items)
        {
            var small = im.Duplicate() as Image;
            if (small == null) continue;
            if (small.GetWidth() > 110)
            {
                int nh = small.GetHeight() * 110 / small.GetWidth();
                small = Resize(small, 110, nh);
            }
            for (int y = 0; y < small.GetHeight() && yy + y < 360; y++)
            for (int x = 0; x < small.GetWidth() && 4 + x < 320; x++)
            {
                var c = small.GetPixel(x, y);
                if (c.A < 0.5f) continue;
                sheet.SetPixel(4 + x, yy + y, new Color(c.R, c.G, c.B, 1));
            }
            yy += small.GetHeight() + 4;
        }
        Shot("props", sheet);

        // 两个人物剪影
        var chars = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        chars.Fill(ProcGen.Rgb(26, 26, 26, 0xff));
        var lu = Art.CharLuYunBack(160, 240);
        var su = Art.CharSuTeacher(160, 220);
        for (int y = 0; y < 240; y++)
        for (int x = 0; x < 160; x++)
        {
            var c = lu.GetPixel(x, y);
            if (c.A > 0.5f) chars.SetPixel(x, y, new Color(c.R, c.G, c.B, 1));
            if (y < su.GetHeight())
            {
                var c2 = su.GetPixel(x, y);
                if (c2.A > 0.5f) chars.SetPixel(160 + x, y, new Color(c2.R, c2.G, c2.B, 1));
            }
        }
        Shot("chars", chars);

        GD.Print("════ 底板预览写完 ════");
        GetTree().Quit(0);
    }

    /// <summary>Godot 的 Image.Resize 在 C# 里返回 void，得包一层。</summary>
    private static Image Resize(Image src, int w, int h)
    {
        var copy = src.Duplicate() as Image;
        copy?.Resize(w, h, Image.Interpolation.Nearest);
        return copy;
    }
}