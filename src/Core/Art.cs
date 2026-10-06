using System;
using Godot;

namespace MoShi.Core;

/// <summary>
/// 成品的程序化资产。建立在 <see cref="ProcGen"/> 的图元之上。
///
/// ★ 存在的唯一理由：**把美术的工时压到只剩"程序画不好的那几张"。**
///   凡是能用图元拼出来的东西，就不要外包——外包意味着等待、返工、
///   以及"规格对不上"的风险，而这三件事在 GameJam 里都是致命的。
///
/// 这里的每一样东西都会先被 <see cref="AssetIntake"/> 询问：
/// 美术交了图就用美术的，没交就用这里的。程序版是**默认路径**，不是占位符。
/// </summary>
public static class Art
{
    // ── 场景 ────────────────────────────────────────────────────────────

    /// <summary>阴天墓地。三阶硬边：灰白天空 / 枯草地平线 / 远处一排碑的剪影。</summary>
    public static Image BgGraveyard(int w, int h) => ProcGen.BgGraveyard(w, h);

    /// <summary>墓园办公室：极暗，只有 CRT 的一点冷光。</summary>
    public static Image BgOffice(int w, int h)
    {
        var img = ProcGen.BgGraveyard(w, h);   // 先拿底，再压暗
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            // 桌面只留中间一片亮（CRT），四周压到很暗
            float dx = (x - w * 0.5f) / (w * 0.55f);
            float dy = (y - h * 0.62f) / (h * 0.62f);
            float glow = Mathf.Clamp(1f - (dx * dx + dy * dy), 0f, 1f);
            float v = (0.06f + glow * 0.72f) * (c.R * 0.9f + 0.1f);
            // 一点冷调
            img.SetPixel(x, y, new Color(v * 0.92f, v * 0.96f, v, 1));
        }
        // 桌面横线（木纹）
        int deskY = h * 2 / 3;
        for (int y = deskY; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float grain = ProcGen.FractalAccessor(x, y * 4, 64, 17, 2);
                var c = img.GetPixel(x, y);
                float v = c.R * (0.82f + grain * 0.36f);
                img.SetPixel(x, y, new Color(v, v * 0.97f, v * 0.92f, 1));
            }
        return img;
    }

    /// <summary>档案室：竖直格架 + 顶灯直下 + 前景阅览台。</summary>
    public static Image BgArchive(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int shelfTop = 0, shelfBottom = h * 3 / 4, tableTop = h * 3 / 4;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v;
            if (y < tableTop)
            {
                // 格架：每格 24px 高、40px 宽
                int cellY = (y - shelfTop) / 24;
                int cellX = x / 40;
                bool inCell = ((y - shelfTop) % 24) is > 2 and < 22 && (x % 40) is > 1 and < 38;

                if (!inCell)
                {
                    // 隔板：上沿亮、下沿暗
                    bool lit = ((y - shelfTop) % 24) <= 3;
                    v = lit ? 0.42f : 0.16f;
                }
                else
                {
                    // 牛皮纸袋
                    float n = ProcGen.FractalAccessor(cellX * 40, cellY * 24, 32, 29, 2);
                    bool bag = ((x % 40) - 3) / 32f > n * 0.5f;
                    v = bag ? 0.55f : 0.10f;
                }
            }
            else
            {
                // 阅览台：木纹
                v = 0.30f + ProcGen.FractalAccessor(x, y, 48, 31, 2) * 0.28f;
            }
            img.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.94f, 1));
        }
        return img;
    }

    /// <summary>石刻铺：卷帘门拉到一半，地上有石粉。</summary>
    public static Image BgShop(int w, int h)
    {
        var img = ProcGen.BgGraveyard(w, h);
        int doorTop = h / 6, doorBottom = h * 4 / 5;
        int doorL = w / 6, doorR = w * 5 / 6;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            if (doorL <= x && x < doorR && doorTop <= y && y < doorBottom)
            {
                // 卷帘门：横向条纹，一色
                bool slat = (y % 7) < 5;
                float v = slat ? 0.34f : 0.20f;
                // 门拉到一半：下面透出黑
                if (y > doorBottom - (doorBottom - doorTop) / 3) v *= 0.15f;
                img.SetPixel(x, y, new Color(v, v, v * 1.02f, 1));
            }
            else if (y >= doorBottom)
            {
                // 地上石粉：白色小点
                float n = ProcGen.TileValueFor(x, y, 32);
                if (n > 0.80f) img.SetPixel(x, y, new Color(0.82f, 0.82f, 0.78f, 1));
            }
        }
        return img;
    }

    /// <summary>雨（HE 结局：派出所门口）。只有雨。</summary>
    public static Image BgRain(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = 0.05f + (float)y / h * 0.10f;
            img.SetPixel(x, y, new Color(v, v, v * 1.05f, 1));
        }
        // 雨丝：1px 竖线，稀疏
        for (int x = 0; x < w; x++)
        {
            int seed = ProcGen.TileValueFor(x, 0, 97) > 0.88f ? x : -1;
            if (seed < 0) continue;
            int y0 = (int)(ProcGen.TileValueFor(x, 1, 89) * h);
            int len = 14 + (int)(ProcGen.TileValueFor(x, 2, 91) * 12);
            for (int y = y0; y < y0 + len && y < h; y++)
            {
                var c = img.GetPixel(x, y);
                img.SetPixel(x, y, new Color(c.R + 0.22f, c.G + 0.22f, c.B + 0.24f, 1));
            }
        }
        return img;
    }

    /// <summary>P1 · 铺子内景。木头搭的门面、堆着的山西黑、蒙布的尚未立起的石碑。</summary>
    public static Image BgShopInterior(int w, int h)
    {
        var img = BgShop(w, h);
        int floorY = h * 3 / 5;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = img.GetPixel(x, y);
            if (y < floorY)
            {
                // 木板墙：横向板缝
                bool seam = (y % 11) < 2;
                float grain = ProcGen.FractalAccessor(x, y, 48, 83, 2);
                float v = (seam ? 0.16f : 0.34f + grain * 0.16f);
                img.SetPixel(x, y, new Color(v, v * 0.96f, v * 0.90f, 1));
            }
            else
            {
                // 水泥地 + 石粉
                float n = ProcGen.FractalAccessor(x, y, 40, 89, 2);
                float v = 0.22f + n * 0.18f;
                img.SetPixel(x, y, new Color(v, v, v * 0.98f, 1));
            }
        }

        // 堆着的石料：几块深色长方体（轮廓清楚，不画纹理）
        var rng = new System.Random(20060517);
        for (int i = 0; i < 7; i++)
        {
            int bx = 30 + i * 82 + rng.Next(-8, 8);
            int by = floorY + 20 + rng.Next(-10, 26);
            int bw = 58 + rng.Next(0, 22);
            int bh = 16 + rng.Next(0, 12);
            FillRect(img, bx, by, bw, bh, ProcGen.StoneDeep);
            FillRect(img, bx, by, bw, 2, ProcGen.StoneLit);
            FillRect(img, bx, by + bh - 2, bw, 2, ProcGen.StoneShade);
        }

        // 蒙着布的碑：一块竖着的浅色长方，布的褶皱用竖条表现
        int cx = w / 2 - 40, cy = floorY - 70;
        FillRect(img, cx, cy, 80, 110, ProcGen.PaperOld);
        for (int x = cx + 3; x < cx + 77; x += 7)
            FillRect(img, x, cy, 2, 110, ProcGen.PaperMid);

        return img;
    }

    /// <summary>P7 · 窗口柜台（殡仪馆 / 医院档案室 / 派出所共用，换牌子）。</summary>
    public static Image BgCounter(int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int counterY = h * 2 / 3;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v;
            if (y < counterY - 60)
            {
                // 上方：机构牌子那一带，压暗
                v = 0.18f;
            }
            else if (y < counterY)
            {
                // 玻璃窗：一道斜向反光
                float diag = (float)x / w * 0.5f + (float)y / h * 0.5f;
                float g = (diag > 0.55f && diag < 0.68f) ? 1.9f : 1f;
                v = 0.24f * g;
            }
            else
            {
                // 柜台面：木纹 + 台灯的一小片光
                float grain = ProcGen.FractalAccessor(x, y, 48, 97, 2);
                float lamp = 1f - Mathf.Min(1f, Mathf.Pow(Mathf.Abs(x - w * 0.5f) / (w * 0.30f), 2f));
                v = (0.26f + grain * 0.14f) * (1f + lamp * 0.9f);
            }
            img.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.94f, 1));
        }

        // 台面的一道亮边
        FillRect(img, 0, counterY - 2, w, 2, ProcGen.StoneLit);
        return img;
    }

    // ── 角色（只做剪影，因为全篇不画正脸）─────────────────────────────

    /// <summary>老吴背影：蹲姿，只有轮廓 + 一块背光面。底边对齐 y = h。</summary>
    public static Image CharLuYunBack(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        // 蹲着的人：一个大椭圆（躯干）+ 一个圆（头）+ 伸出的手臂
        FillBlob(img, w * 0.50f, h * 0.62f, w * 0.30f, h * 0.34f, ProcGen.StoneMid);
        FillBlob(img, w * 0.46f, h * 0.28f, w * 0.14f, h * 0.16f, ProcGen.StoneMid);
        FillRect(img, (int)(w * 0.55f), (int)(h * 0.58f), (int)(w * 0.42f), (int)(h * 0.09f),
                 ProcGen.StoneLit);   // 伸出去的手臂
        // 底部坐实
        FillRect(img, (int)(w * 0.18f), (int)(h * 0.86f), (int)(w * 0.66f), (int)(h * 0.14f),
                 ProcGen.StoneShade);
        RimLight(img, ProcGen.StoneHi);
        return img;
    }

    /// <summary>苏老师 + 轮椅，侧后 3/4 背影。★ 伸出摸碑的那只手要清楚。</summary>
    public static Image CharSuTeacher(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);

        // 轮椅：大后轮 + 小前轮 + 扶手
        StrokeCircle(img, w * 0.36f, h * 0.70f, h * 0.26f, 4, ProcGen.StoneMid);
        StrokeCircle(img, w * 0.72f, h * 0.86f, h * 0.11f, 3, ProcGen.StoneShade);
        FillRect(img, (int)(w * 0.28f), (int)(h * 0.56f), (int)(w * 0.56f), 5, ProcGen.StoneMid);
        FillRect(img, (int)(w * 0.28f), (int)(h * 0.56f), 5, (int)(h * 0.30f), ProcGen.StoneMid);
        FillRect(img, (int)(w * 0.80f), (int)(h * 0.56f), 5, (int)(h * 0.20f), ProcGen.StoneMid);
        FillRect(img, (int)(w * 0.26f), (int)(h * 0.86f), (int)(w * 0.60f), 6, ProcGen.StoneShade);

        // 身体：前倾
        FillBlob(img, w * 0.46f, h * 0.40f, w * 0.20f, h * 0.24f, ProcGen.SuitMid);
        // 头（白发）
        FillBlob(img, w * 0.52f, h * 0.20f, w * 0.10f, h * 0.11f, ProcGen.DustWhite);
        // ★ 伸出的手
        FillRect(img, (int)(w * 0.56f), (int)(h * 0.42f), (int)(w * 0.34f), 6, ProcGen.SuitLit);
        FillBlob(img, w * 0.88f, h * 0.43f, w * 0.05f, h * 0.05f, ProcGen.DustWhite);

        RimLight(img, ProcGen.StoneHi);
        return img;
    }

    /// <summary>记者剪影，举着摄像机。</summary>
    public static Image CharPhotographer(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        FillBlob(img, w * 0.45f, h * 0.55f, w * 0.26f, h * 0.36f, ProcGen.StoneDeep);
        FillBlob(img, w * 0.44f, h * 0.20f, w * 0.12f, h * 0.13f, ProcGen.StoneDeep);
        FillRect(img, (int)(w * 0.58f), (int)(h * 0.30f), (int)(w * 0.30f), (int)(h * 0.12f),
                 ProcGen.StoneDeep);
        RimLight(img, ProcGen.StoneHi);
        return img;
    }

    // ── 道具 ────────────────────────────────────────────────────────────

    /// <summary>练手料头：一块小石片，上面一行随手刻的废字。★ 和碑面同源的刀口。</summary>
    public static Image PropScrapStone(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);

        // 不规则小石片
        int pad = 3;
        for (int y = pad; y < h - pad; y++)
        for (int x = pad; x < w - pad; x++)
        {
            float dx = (x - w * 0.5f) / (w * 0.46f);
            float dy = (y - h * 0.5f) / (h * 0.46f);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float wobble = ProcGen.FractalAccessor(x, y, 16, 7, 2) * 0.16f;
            if (r > 1f - wobble) continue;

            float v = 0.30f + (1f - r) * 0.22f + ProcGen.TileValueFor(x, y, 23) * 0.06f;
            img.SetPixel(x, y, ProcGen.StoneMid.Lerp(ProcGen.StoneLit, 1f - r));
        }

        // 随手刻的一行废字——★ 必须是同一个人的刀口，所以用同一套凿痕逻辑
        int size = Mathf.RoundToInt(h * 0.42f);
        int bx = Mathf.RoundToInt(w * 0.14f), by = Mathf.RoundToInt(h * 0.30f);
        foreach (char c in "试刻")
        {
            var glyph = SteleBuilder.RenderGlyph(ProcGen.CachedFont, c, size);
            if (glyph == null) continue;
            int gw = glyph.GetWidth(), gh = glyph.GetHeight();
            for (int y = 0; y < gh && by + y < h; y++)
            for (int x = 0; x < gw && bx + x < w; x++)
            {
                var px = glyph.GetPixel(x, y);
                float a = px.A < 0.5f ? px.R : px.A;
                if (a < 0.4f) continue;
                // 凿痕：往右下偏 1px，并压深
                PutChisel(img, bx + x + 1, by + y + 1, 210, ProcGen.ChiselA);
                PutChisel(img, bx + x, by + y, 180, ProcGen.ChiselA);
            }
            bx += size + 2;
        }
        return img;
    }

    /// <summary>把一个"凿痕"像素写进图：深色底 + 一侧的翻起毛边。毛边方向由 chisel 决定。</summary>
    private static void PutChisel(Image img, int x, int y, int depth, Image chisel)
    {
        if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) return;
        float c = SampleChisel(chisel, x, y);
        var baseCol = ProcGen.StoneDeep;
        float k = depth / 255f * (0.55f + c * 0.45f);
        img.SetPixel(x, y, baseCol.Lerp(ProcGen.StoneLit, k * 0.5f));
    }

    private static float SampleChisel(Image chisel, int x, int y)
    {
        if (chisel == null) return 0.5f;
        int cx = Mathf.PosMod(x, chisel.GetWidth());
        int cy = Mathf.PosMod(y, chisel.GetHeight());
        return chisel.GetPixel(cx, cy).R;
    }

    /// <summary>工具箱：敞开的铁皮箱，能看到石粉罐（★ 第一章的抉择道具）。</summary>
    public static Image PropToolbox(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        FillRect(img, 2, (int)(h * 0.30f), w - 4, (int)(h * 0.62f), ProcGen.StoneShade);
        FillRect(img, 2, (int)(h * 0.30f), w - 4, 3, ProcGen.StoneLit);
        FillRect(img, 2, (int)(h * 0.86f), w - 4, 4, ProcGen.StoneDeep);

        // 石粉罐：白色小圆柱，放在右上
        int jx = (int)(w * 0.66f), jy = (int)(h * 0.20f);
        int jw = Mathf.RoundToInt(w * 0.18f), jh = Mathf.RoundToInt(h * 0.34f);
        FillRect(img, jx, jy, jw, jh, ProcGen.DustWhite);
        FillRect(img, jx, jy, jw, 2, Colors.White);

        // 錾子
        for (int i = 0; i < 3; i++)
        {
            int sx = (int)(w * 0.10f) + i * 7;
            FillRect(img, sx, (int)(h * 0.46f), 3, (int)(h * 0.30f), ProcGen.StoneLit);
        }
        // 拓包
        FillBlob(img, w * 0.42f, h * 0.62f, w * 0.09f, h * 0.16f, ProcGen.PaperOld);
        return img;
    }

    /// <summary>成交台账：★ 其中一行"压得更深"，那是第五章要用尺量的地方。</summary>
    public static Image PropDeedLedger(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        // 纸
        for (int y = 2; y < h - 2; y++)
        for (int x = 2; x < w - 2; x++)
        {
            float v = ProcGen.FractalAccessor(x, y, 40, 53, 2);
            img.SetPixel(x, y, v > 0.55f ? ProcGen.PaperMid : ProcGen.PaperOld);
        }
        // 行
        int top = (int)(h * 0.18f), gap = Mathf.RoundToInt(h * 0.11f);
        for (int r = 0; r < 6; r++)
        {
            int y = top + r * gap;
            if (y + 2 >= h) break;
            bool hot = r == 3;      // ★ 成交日期那一行
            FillRect(img, (int)(w * 0.10f), y, (int)(w * 0.80f), hot ? 2 : 1,
                     hot ? ProcGen.Ink : ProcGen.PaperOld);
            // 压痕：那一行下面画一道更深的影子
            if (hot)
                FillRect(img, (int)(w * 0.10f), y + 2, (int)(w * 0.80f), 2, ProcGen.PaperOld);
        }
        return img;
    }

    /// <summary>存档登记表：★ 比别的纸新、白，上面留一个盖章位。</summary>
    public static Image PropArchiveForm(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        for (int y = 2; y < h - 2; y++)
        for (int x = 2; x < w - 2; x++)
            img.SetPixel(x, y, ProcGen.PaperLit);      // ★ 明显比旧纸白

        // 表格线
        int rows = 5;
        int top = (int)(h * 0.30f), gap = Mathf.RoundToInt(h * 0.13f);
        for (int r = 0; r <= rows; r++)
        {
            int y = top + r * gap;
            if (y >= h - 3) break;
            FillRect(img, (int)(w * 0.08f), y, (int)(w * 0.84f), 1, ProcGen.PaperOld);
        }
        for (int c = 1; c < 3; c++)
            FillRect(img, (int)(w * (0.08f + c * 0.28f)), top, 1, rows * gap, ProcGen.PaperOld);

        // 盖章位：右上角一个浅圆印
        StrokeCircle(img, w * 0.78f, h * 0.16f, w * 0.10f, 1, ProcGen.PaperOld);
        return img;
    }

    /// <summary>结局文本卡底：纸纹 + 极暗。</summary>
    public static Image Endcard(int w, int h)
    {
        var img = ProcGen.NewTransparent(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float fiber = ProcGen.TileNoiseFor(x, y, 32, 41) > 0.62f ? 0.035f : 0f;
            float v = 0.03f + fiber;
            img.SetPixel(x, y, new Color(v, v * 0.99f, v * 0.95f, 1));
        }
        return img;
    }

    // ── 图元 ────────────────────────────────────────────────────────────

    private static void FillRect(Image img, int x, int y, int w, int h, Color c)
    {
        for (int yy = y; yy < y + h; yy++)
        for (int xx = x; xx < x + w; xx++)
            if (xx >= 0 && yy >= 0 && xx < img.GetWidth() && yy < img.GetHeight())
                img.SetPixel(xx, yy, c);
    }

    private static void FillBlob(Image img, float cx, float cy, float rx, float ry, Color c)
    {
        int x0 = Mathf.RoundToInt(cx - rx), x1 = Mathf.RoundToInt(cx + rx);
        int y0 = Mathf.RoundToInt(cy - ry), y1 = Mathf.RoundToInt(cy + ry);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            if (dx * dx + dy * dy > 1f) continue;
            img.SetPixel(x, y, c);
        }
    }

    private static void StrokeCircle(Image img, float cx, float cy, float r, int thick, Color c)
    {
        int steps = Mathf.RoundToInt(r * 8f);
        for (int i = 0; i < steps; i++)
        {
            float a = i / (float)steps * Mathf.Tau;
            for (int t = 0; t < thick; t++)
                PutChiselClr(img,
                    Mathf.RoundToInt(cx + Mathf.Cos(a) * (r + t - thick / 2f)),
                    Mathf.RoundToInt(cy + Mathf.Sin(a) * (r + t - thick / 2f)), c);
        }
    }

    private static void PutChiselClr(Image img, int x, int y, Color c)
    {
        if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) return;
        img.SetPixel(x, y, c);
    }

    /// <summary>给剪影加一道左上方向的边光，不然人物会糊成一团黑。</summary>
    private static void RimLight(Image img, Color rim)
    {
        var src = img.Duplicate() as Image;
        if (src == null) return;
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            if (src.GetPixel(x, y).A > 0.5f) continue;
            bool up = y > 0 && src.GetPixel(x, y - 1).A > 0.5f;
            bool left = x > 0 && src.GetPixel(x - 1, y).A > 0.5f;
            if (up || left) img.SetPixel(x, y, rim);
        }
    }
}