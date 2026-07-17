using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Cleans LLM module fields and backfills empty entry points / key types from related files.
/// </summary>
public static class ModuleDocumentEnricher
{
    public static void Enrich(ModuleDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        doc.Purpose = LlmTextCleanup.CleanProse(doc.Purpose);

        doc.Dependencies = LlmTextCleanup.CleanList(doc.Dependencies);
        doc.Gotchas = LlmTextCleanup.CleanList(doc.Gotchas);
        doc.HowToExtend = LlmTextCleanup.CleanList(doc.HowToExtend);
        doc.KeyTypes = LlmTextCleanup.CleanList(doc.KeyTypes);
        doc.EntryPoints = LlmTextCleanup.CleanList(doc.EntryPoints);

        // Normalize related files to clean paths (preserve role in parentheses when present)
        doc.RelatedFiles = NormalizeRelatedFiles(doc.RelatedFiles);

        BackfillFromRelatedFiles(doc);
    }

    private static List<string> NormalizeRelatedFiles(IEnumerable<string> related)
    {
        var list = new List<string>();
        foreach (var raw in related)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var path = LlmTextCleanup.ExtractPathFromRelatedFile(raw);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // Keep optional role if present
            var roleMatch = System.Text.RegularExpressions.Regex.Match(
                raw,
                @"role\s*:\s*(?<role>[^;]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (roleMatch.Success)
            {
                list.Add($"{path} — {roleMatch.Groups["role"].Value.Trim()}");
            }
            else
            {
                list.Add(path);
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void BackfillFromRelatedFiles(ModuleDocument doc)
    {
        var paths = doc.RelatedFiles
            .Select(r => r.Split('—', 2)[0].Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (doc.EntryPoints.Count == 0)
        {
            var entryHints = paths
                .Where(p =>
                {
                    var name = Path.GetFileName(p);
                    return name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
                           || name.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Middleware", StringComparison.OrdinalIgnoreCase);
                })
                .Take(8)
                .ToList();
            if (entryHints.Count > 0)
            {
                doc.EntryPoints = entryHints;
            }
        }

        if (doc.KeyTypes.Count == 0)
        {
            var types = paths
                .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            if (types.Count > 0)
            {
                doc.KeyTypes = types!;
            }
        }
    }
}
