using System.Text.RegularExpressions;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Normalizes semi-structured LLM output (key: value; key: value blobs) into readable bullets.
/// </summary>
public static partial class LlmTextCleanup
{
    /// <summary>
    /// If text looks like a field dump, expand into multiple clean lines; otherwise return trimmed text.
    /// </summary>
    public static string CleanProse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var trimmed = text.Trim();
        var parts = SplitSemiStructured(trimmed);
        if (parts.Count <= 1)
        {
            return trimmed;
        }

        return string.Join(' ', parts.Select(p => p.EndsWith('.') ? p : p.TrimEnd('.') + "."));
    }

    /// <summary>Split a single blob into bullet-friendly strings.</summary>
    public static IReadOnlyList<string> ToBulletItems(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var trimmed = text.Trim();
        var parts = SplitSemiStructured(trimmed);
        if (parts.Count == 0)
        {
            return [trimmed];
        }

        return parts
            .Select(p => p.Trim().TrimEnd(';').Trim())
            .Where(p => p.Length > 0)
            .Select(StripLeadingKey)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Clean a list of dependency/gotcha/related strings from LLM output.</summary>
    public static List<string> CleanList(IEnumerable<string>? items)
    {
        if (items is null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var bullets = ToBulletItems(item);
            if (bullets.Count == 0)
            {
                result.Add(item.Trim());
            }
            else
            {
                result.AddRange(bullets);
            }
        }

        return result
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extract a filesystem path from related-file strings like
    /// <c>path: Foo/Bar.cs; role: Primary entry</c> or plain paths.
    /// </summary>
    public static string? ExtractPathFromRelatedFile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().Trim('`');
        var pathMatch = PathKeyRegex().Match(trimmed);
        if (pathMatch.Success)
        {
            return pathMatch.Groups["path"].Value.Trim().Replace('\\', '/');
        }

        // Bare path-ish token
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var first = trimmed.Split([';', ',', ' '], 2, StringSplitOptions.RemoveEmptyEntries)[0];
            return first.Trim().Replace('\\', '/');
        }

        return trimmed.Replace('\\', '/');
    }

    private static List<string> SplitSemiStructured(string text)
    {
        // key: value; key: value  OR  primary: …; responsibilities: …
        if (!text.Contains(':') || (!text.Contains(';') && CountKeys(text) < 2))
        {
            // Also split on "; " even without keys if very long multi-clause
            if (text.Contains("; ", StringComparison.Ordinal) && text.Length > 120)
            {
                return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => s.Length > 0)
                    .ToList();
            }

            return [text];
        }

        // Split on "; " boundaries first
        var segments = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count >= 2)
        {
            return segments;
        }

        // Split on " word:" patterns inside a single line
        var keySplits = KeyBoundaryRegex().Split(text)
            .Select(s => s.Trim().TrimStart(':').Trim())
            .Where(s => s.Length > 0)
            .ToList();
        return keySplits.Count >= 2 ? keySplits : [text];
    }

    private static int CountKeys(string text) =>
        KeyBoundaryRegex().Matches(text).Count;

    private static string StripLeadingKey(string text)
    {
        var m = LeadingKeyRegex().Match(text);
        if (m.Success)
        {
            var rest = text[m.Length..].Trim();
            return rest.Length > 0 ? rest : text;
        }

        return text;
    }

    [GeneratedRegex(
        @"\bpath\s*:\s*(?<path>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathKeyRegex();

    [GeneratedRegex(
        @"(?<=^|[;\s])(?<key>[A-Za-z][A-Za-z0-9_/ -]{1,40})\s*:",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyBoundaryRegex();

    [GeneratedRegex(
        @"^(?<key>[A-Za-z][A-Za-z0-9_/ -]{1,40})\s*:\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex LeadingKeyRegex();
}
