using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Reuses existing wiki module / cross-cutting ids when the LLM invents new slugs for the same area,
/// so subsequent generates update <c>modules/loans.md</c> instead of creating <c>modules/loan-management.md</c>.
/// </summary>
public static class ModuleIdStabilizer
{
    /// <summary>
    /// Mutates <paramref name="plan"/> module ids to prefer existing filenames under <c>modules/</c>.
    /// </summary>
    public static void StabilizeModuleIds(
        ModulePlan plan,
        string wikiOutputPath,
        IReadOnlyList<string>? lastRunModuleIds = null,
        List<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Modules.Count == 0)
        {
            return;
        }

        var modulesDir = Path.Combine(wikiOutputPath, "modules");
        var existingIds = DiscoverExistingIds(modulesDir);
        if (existingIds.Count == 0 && (lastRunModuleIds is null || lastRunModuleIds.Count == 0))
        {
            return;
        }

        var preferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (lastRunModuleIds is not null)
        {
            foreach (var id in lastRunModuleIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                preferred.Add(Slug(id));
            }
        }

        foreach (var id in existingIds)
        {
            preferred.Add(id);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in plan.Modules)
        {
            var proposed = Slug(string.IsNullOrWhiteSpace(module.Id) ? module.Name : module.Id);
            if (string.IsNullOrWhiteSpace(proposed))
            {
                proposed = "module";
            }

            // Exact hit on existing/last-run id
            if (preferred.Contains(proposed) && used.Add(proposed))
            {
                module.Id = proposed;
                continue;
            }

            var match = FindBestMatch(module, proposed, preferred, used);
            if (match is not null)
            {
                if (!string.Equals(proposed, match, StringComparison.OrdinalIgnoreCase))
                {
                    warnings?.Add(
                        $"Reused existing module page id '{match}' for plan '{proposed}' ({module.Name}) to avoid duplicate files.");
                }

                module.Id = match;
                used.Add(match);
                continue;
            }

            // Genuine new module — ensure unique among this plan
            var unique = proposed;
            var n = 2;
            while (!used.Add(unique))
            {
                unique = $"{proposed}-{n++}";
            }

            module.Id = unique;
        }
    }

    /// <summary>
    /// Mutates cross-cutting document ids to prefer existing <c>cross-cutting/*.md</c> filenames.
    /// </summary>
    public static void StabilizeCrossCuttingIds(
        IList<CrossCuttingDocument> items,
        string wikiOutputPath,
        List<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        var dir = Path.Combine(wikiOutputPath, "cross-cutting");
        var existing = DiscoverExistingIds(dir);
        if (existing.Count == 0)
        {
            foreach (var item in items)
            {
                item.Id = Slug(string.IsNullOrWhiteSpace(item.Id) ? item.Title : item.Id);
            }

            return;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var proposed = Slug(string.IsNullOrWhiteSpace(item.Id) ? item.Title : item.Id);
            if (existing.Contains(proposed) && used.Add(proposed))
            {
                item.Id = proposed;
                continue;
            }

            var match = existing
                .Where(id => !used.Contains(id))
                .Select(id => (Id: id, Score: TokenOverlapScore(proposed, id)
                                              + TokenOverlapScore(Slug(item.Title), id)))
                .Where(x => x.Score >= 2)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Id)
                .FirstOrDefault();

            if (match is not null)
            {
                if (!string.Equals(proposed, match, StringComparison.OrdinalIgnoreCase))
                {
                    warnings?.Add(
                        $"Reused existing cross-cutting page id '{match}' for plan '{proposed}'.");
                }

                item.Id = match;
                used.Add(match);
                continue;
            }

            var unique = proposed;
            var n = 2;
            while (!used.Add(unique))
            {
                unique = $"{proposed}-{n++}";
            }

            item.Id = unique;
        }
    }

    private static string? FindBestMatch(
        ModuleDescriptor module,
        string proposed,
        HashSet<string> preferred,
        HashSet<string> used)
    {
        var candidates = preferred.Where(id => !used.Contains(id)).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var related = module.RelatedFiles
            .Select(LlmTextCleanup.ExtractPathFromRelatedFile)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .ToList();

        var nameSlug = Slug(module.Name);
        var scores = new List<(string Id, int Score)>();
        foreach (var id in candidates)
        {
            var score = 0;
            if (string.Equals(id, proposed, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            if (string.Equals(id, nameSlug, StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }

            score += TokenOverlapScore(proposed, id) * 10;
            score += TokenOverlapScore(nameSlug, id) * 8;

            // Related-file basename tokens (e.g. LoansController → loans)
            foreach (var path in related)
            {
                var file = Path.GetFileNameWithoutExtension(path);
                score += TokenOverlapScore(Slug(file), id) * 6;
                if (file.Contains(id, StringComparison.OrdinalIgnoreCase)
                    || id.Contains(Slug(file).Split('-')[0], StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }
            }

            // Contained slug: loan-management vs loans
            if (proposed.Contains(id, StringComparison.OrdinalIgnoreCase)
                || id.Contains(proposed, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            scores.Add((id, score));
        }

        var best = scores.OrderByDescending(s => s.Score).First();
        // Require a meaningful match — avoid mapping "rewards" → "api-host"
        return best.Score >= 20 ? best.Id : null;
    }

    private static int TokenOverlapScore(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        return ta.Count(t => tb.Contains(t));
    }

    private static HashSet<string> Tokens(string slug) =>
        slug.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Where(t => t is not ("module" or "api" or "and" or "the" or "for" or "with"
                or "management" or "configuration" or "application" or "service" or "services"
                or "host" or "root" or "layer" or "cross" or "cutting"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> DiscoverExistingIds(string directory)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return set;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!string.IsNullOrWhiteSpace(id))
            {
                set.Add(Slug(id));
            }
        }

        return set;
    }

    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var s = value.Trim().ToLowerInvariant();
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal))
        {
            s = s.Replace("--", "-", StringComparison.Ordinal);
        }

        return s.Trim('-');
    }
}
