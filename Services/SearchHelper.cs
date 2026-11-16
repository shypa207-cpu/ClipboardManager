using System;
using System.Collections.Generic;

namespace ClipboardManager.Services;

/// <summary>Пошук по частині слова: підрядок, ранжування, розкладка EN↔UA, нечіткий збіг.</summary>
public static class SearchHelper
{
    private static readonly Dictionary<char, char> LayoutMap = BuildLayoutMap();

    private static Dictionary<char, char> BuildLayoutMap()
    {
        const string en = "qwertyuiop[]asdfghjkl;'zxcvbnm,";
        const string ua = "йцукенгшщзхїфівапролджєячсмитьб";
        var map = new Dictionary<char, char>();
        for (int i = 0; i < en.Length; i++)
        {
            map[en[i]] = ua[i];
            map[ua[i]] = en[i];
        }
        return map;
    }

    public static string[] Tokenize(string query) =>
        query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Всі слова запиту мають бути підрядками тексту (в будь-якому місці слова, без урахування регістру).</summary>
    public static double ScoreSubstring(string text, string[] tokens, out int firstIndex)
    {
        firstIndex = int.MaxValue;
        double score = 0;
        foreach (var t in tokens)
        {
            int idx = text.IndexOf(t, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { firstIndex = 0; return -1; }
            firstIndex = Math.Min(firstIndex, idx);
            bool wordStart = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            score += (wordStart ? 3.0 : 1.0) + 1.0 / (1 + idx);
        }
        return score;
    }

    /// <summary>Нечіткий збіг: літери слова запиту йдуть у тексті по порядку (для слів від 3 літер).</summary>
    public static double ScoreFuzzy(string text, string[] tokens, out int firstIndex)
    {
        firstIndex = int.MaxValue;
        foreach (var t in tokens)
        {
            if (t.Length < 3) { firstIndex = 0; return -1; }
            int pos = 0, first = -1;
            foreach (char c in t)
            {
                int found = text.IndexOf(c.ToString(), pos, StringComparison.OrdinalIgnoreCase);
                if (found < 0) { firstIndex = 0; return -1; }
                if (first < 0) first = found;
                pos = found + 1;
            }
            firstIndex = Math.Min(firstIndex, first);
        }
        return 0.1;
    }

    /// <summary>Конвертує запит, набраний у «неправильній» розкладці (ghbdsn ↔ привіт).</summary>
    public static string SwapLayout(string query)
    {
        var chars = query.ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (LayoutMap.TryGetValue(chars[i], out var m)) chars[i] = m;
        return new string(chars);
    }
}
