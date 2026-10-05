using System;
using System.Collections.Generic;
using Godot;

namespace MoShi.Core;

/// <summary>账本上的一行。</summary>
public sealed class LedgerLine
{
    public string Text { get; set; } = "";

    /// <summary>玩家写下这一行时的"场景"，用来在账本 UI 上分组。</summary>
    public string Source { get; set; } = "";

    /// <summary>是否是他爸的字（第一页背面那行）。</summary>
    public bool ByFather { get; set; }
}

/// <summary>
/// 手抄日期的宽松匹配。
///
/// 第二章要求玩家把碑面那句抄下来作为过关条件，但自由输入是 GameJam 里
/// 最容易翻车的地方：玩家会打 2021/5/16、16号、五月十六日……
/// 所以只要求**月和日**对得上，年份可省。全角半角、空格、破折号全忽略。
/// </summary>
public static class DateNormalizer
{
    private static readonly Dictionary<char, int> HanDigits = new()
    {
        ['〇'] = 0, ['零'] = 0,
        ['一'] = 1, ['壹'] = 1, ['幺'] = 1,
        ['二'] = 2, ['两'] = 2, ['贰'] = 2, ['俩'] = 2,
        ['三'] = 3, ['叁'] = 3,
        ['四'] = 4, ['肆'] = 4,
        ['五'] = 5, ['伍'] = 5,
        ['六'] = 6, ['陆'] = 6,
        ['七'] = 7, ['柒'] = 7,
        ['八'] = 8, ['捌'] = 8,
        ['九'] = 9, ['玖'] = 9,
    };

    /// <summary>归一化后的 (年, 月, 日)。任何一项解析不出来就是 null。</summary>
    public readonly record struct Date(int? Year, int? Month, int? Day)
    {
        public bool HasMonthDay => Month.HasValue && Day.HasValue;
    }

    public static Date Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Date(null, null, null);

        // 全角 → 半角，去掉所有空白和常见分隔符里的噪音
        var s = NormalizeWidth(raw);
        s = StripSpaces(s);

        var year = ExtractYear(s);
        var rest = StripYear(s);

        // 「五月十六日」这类汉字读法优先整体解析
        var (hm, hd) = ExtractHanMonthDay(rest);
        if (hm.HasValue && hd.HasValue)
            return new Date(year, hm, hd);

        var nums = ExtractNumbers(rest);
        int? month = null, day = null;

        // 2021-05-16 / 2021.5.16 → 已经去掉年份后是 5.16 或 5-16
        var dot = SplitOnSeparators(rest);
        if (dot.Count >= 2)
        {
            month = ToInt(dot[0]);
            day = ToInt(dot[1]);
        }
        else if (nums.Count >= 2)
        {
            // 5月16日 / 5 16
            month = nums[0];
            day = nums[1];
        }
        else if (nums.Count == 1)
        {
            // 「16号」/「十六」——只给了一个数，当成日
            day = nums[0];
        }

        return new Date(year, month, day);
    }

    /// <summary>
    /// 第二章的过关判定：玩家写的日期里，月和日必须是 5 月 16 日。
    /// 年份可省；顺序颠倒（16-5）也接受。
    /// </summary>
    public static bool MatchesMay16(string raw, int month = 5, int day = 16)
    {
        var d = Parse(raw);
        if (!d.HasMonthDay)
            return false;

        bool direct = d.Month == month && d.Day == day;
        bool swapped = d.Month == day && d.Day == month;
        return direct || swapped;
    }

    // ── 内部 ────────────────────────────────────────────────────────────

    private static string StripSpaces(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c) && c != '\u00a0')
                sb.Append(c);
        return sb.ToString();
    }

    private static string NormalizeWidth(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '０' && chars[i] <= '９')
                chars[i] = (char)('0' + (chars[i] - '０'));
            else if (chars[i] >= 'Ａ' && chars[i] <= 'Ｚ')
                chars[i] = (char)('A' + (chars[i] - 'Ａ'));
            else if (chars[i] < ' ' && chars[i] != '\t')
                chars[i] = ' ';
        }
        return new string(chars);
    }

    private static int? ExtractYear(string s)
    {
        // 4 位连续数字，且后面不紧跟"月"字 —— 认作年份
        for (int i = 0; i + 3 < s.Length; i++)
        {
            bool four = true;
            for (int k = 0; k < 4; k++)
                if (!char.IsDigit(s[i + k])) { four = false; break; }
            if (!four) continue;

            bool nextIsMonth = i + 4 < s.Length && (s[i + 4] == '月' || s[i + 4] == '/');
            if (nextIsMonth) continue;

            int y = int.Parse(s.Substring(i, 4));
            if (y is >= 1900 and <= 2999)
                return y;
        }
        return null;
    }

    private static string StripYear(string s)
    {
        for (int i = 0; i + 3 < s.Length; i++)
        {
            bool four = true;
            for (int k = 0; k < 4; k++)
                if (!char.IsDigit(s[i + k])) { four = false; break; }
            if (!four) continue;
            bool nextIsMonth = i + 4 < s.Length && (s[i + 4] == '月' || s[i + 4] == '/');
            if (nextIsMonth) continue;
            int y = int.Parse(s.Substring(i, 4));
            if (y is >= 1900 and <= 2999)
                return s.Remove(i, 4);
        }
        return s;
    }

    /// <summary>「五月十六日」→ (5, 16)。含「十六」这种组合读法。</summary>
    private static (int?, int?) ExtractHanMonthDay(string s)
    {
        int iMonth = s.IndexOf('月');
        if (iMonth < 0) return (null, null);

        var before = s[..iMonth];
        var after = s[(iMonth + 1)..];

        int? m = ReadHanNumber(before, allowSingle: true);
        if (!m.HasValue) return (null, null);

        // 日：截到"日/号"之前
        var cut = after.Length;
        for (int i = 0; i < after.Length; i++)
            if (after[i] is '日' or '号') { cut = i; break; }

        int? d = ReadHanNumber(after[..cut], allowSingle: true);
        if (!d.HasValue) return (null, null);
        return (m, d);
    }

    /// <summary>读汉字数字。支持「十六」=16 这种组合读法。</summary>
    private static int? ReadHanNumber(string s, bool allowSingle)
    {
        var keep = new System.Text.StringBuilder();
        foreach (char ch in s)
            if (HanDigits.ContainsKey(ch) || ch == '十')
                keep.Append(ch);
        s = keep.ToString();
        if (s.Length == 0) return null;

        int total = 0, section = 0;
        bool sawAny = false;
        foreach (char c in s)
        {
            if (c == '十')
            {
                section = section == 0 ? 1 : section;
                total += section * 10;
                section = 0;
                sawAny = true;
            }
            else
            {
                section = HanDigits[c];
                total += section;
                sawAny = true;
            }
        }
        total += section;
        if (!sawAny) return null;
        if (!allowSingle && total > 31) return null;
        return total;
    }

    private static List<string> SplitOnSeparators(string s)
    {
        var parts = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (c is '.' or '-' or '_' or '/' or '\\' or '、')
            {
                if (cur.Length > 0) { parts.Add(cur.ToString()); cur.Clear(); }
            }
            else cur.Append(c);
        }
        if (cur.Length > 0) parts.Add(cur.ToString());
        return parts;
    }

    private static List<int> ExtractNumbers(string s)
    {
        var nums = new List<int>();
        var cur = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (char.IsDigit(c)) cur.Append(c);
            else
            {
                if (cur.Length > 0) { nums.Add(int.Parse(cur.ToString())); cur.Clear(); }
            }
        }
        if (cur.Length > 0) nums.Add(int.Parse(cur.ToString()));

        // 整串都是汉字数字的情况（"十六"）
        if (nums.Count == 0)
        {
            var h = ReadHanNumber(s, allowSingle: true);
            if (h.HasValue) nums.Add(h.Value);
        }
        return nums;
    }

    private static int? ToInt(string s) =>
        int.TryParse(s, out int v) && v is > 0 and <= 9999 ? v : null;
}