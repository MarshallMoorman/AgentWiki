using System.Text;
using System.Text.RegularExpressions;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Parses human-owned <c>workspace-manifest.md</c> files.
/// Tolerant of whitespace; prefers exact heading text for data sections.
/// </summary>
public static class WorkspaceManifestParser
{
    /// <summary>Loads and parses a manifest from disk. Missing file ⇒ Present=false + warning.</summary>
    public static async Task<WorkspaceManifestDocument> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (!File.Exists(manifestPath))
        {
            return new WorkspaceManifestDocument
            {
                SourcePath = Path.GetFullPath(manifestPath),
                Present = false,
                Warnings =
                [
                    $"Workspace contribution manifest not found at '{manifestPath}'. "
                    + "Scaffold with single-repo generate or add docs/wiki/workspace-manifest.md manually."
                ]
            };
        }

        var text = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var doc = Parse(text);
        return new WorkspaceManifestDocument
        {
            SourcePath = Path.GetFullPath(manifestPath),
            Present = true,
            Purpose = doc.Purpose,
            MaintenanceRules = doc.MaintenanceRules,
            Layer = doc.Layer,
            Team = doc.Team,
            Applications = doc.Applications,
            Brands = doc.Brands,
            Responsibilities = doc.Responsibilities,
            RouteWhen = doc.RouteWhen,
            DoNotRouteWhen = doc.DoNotRouteWhen,
            RelatedSystems = doc.RelatedSystems,
            Keywords = doc.Keywords,
            AdditionalContext = doc.AdditionalContext,
            Warnings = doc.Warnings
        };
    }

    /// <summary>
    /// Convenience: load from repo + wiki relative path
    /// (e.g. repoPath + <c>docs/wiki</c> → <c>docs/wiki/workspace-manifest.md</c>).
    /// </summary>
    public static Task<WorkspaceManifestDocument> LoadFromWikiAsync(
        string repoPath,
        string wikiPathRelativeOrAbsolute,
        CancellationToken cancellationToken = default)
    {
        var path = WorkspaceManifestScaffold.ResolvePath(repoPath, wikiPathRelativeOrAbsolute);
        return LoadAsync(path, cancellationToken);
    }

    /// <summary>Parses Markdown text into a structured manifest document.</summary>
    public static WorkspaceManifestDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var warnings = new List<string>();
        var sections = SplitSections(markdown);

        string? GetBody(string heading)
        {
            foreach (var (h, body) in sections)
            {
                if (string.Equals(h, heading, StringComparison.Ordinal))
                {
                    return body;
                }
            }

            // Case-insensitive fallback
            foreach (var (h, body) in sections)
            {
                if (string.Equals(h, heading, StringComparison.OrdinalIgnoreCase))
                {
                    return body;
                }
            }

            return null;
        }

        var purpose = NormalizeBlock(GetBody("Purpose"));
        var rules = NormalizeBlock(GetBody("Maintenance rules"));
        var layerBody = GetBody(Constants.WorkspaceManifest.HeadingLayer);
        var teamBody = GetBody(Constants.WorkspaceManifest.HeadingTeam);
        var appsBody = GetBody(Constants.WorkspaceManifest.HeadingApplications);
        var brandsBody = GetBody(Constants.WorkspaceManifest.HeadingBrands);
        var respBody = GetBody(Constants.WorkspaceManifest.HeadingResponsibilities);
        var routeBody = GetBody(Constants.WorkspaceManifest.HeadingRouteWhen);
        var doNotBody = GetBody(Constants.WorkspaceManifest.HeadingDoNotRoute);
        var relatedBody = GetBody(Constants.WorkspaceManifest.HeadingRelatedSystems);
        var keywordsBody = GetBody(Constants.WorkspaceManifest.HeadingKeywords);
        var additionalBody = GetBody(Constants.WorkspaceManifest.HeadingAdditionalContext);

        var layer = ParseLayer(layerBody);
        var team = ParseSingleLine(teamBody);
        var applications = ParseApplications(appsBody);
        var brands = ParseBrands(brandsBody, warnings);
        var responsibilities = ParseBulletOrLines(respBody);
        var routeWhen = ParseBulletOrLines(routeBody);
        var doNotRoute = ParseBulletOrLines(doNotBody);
        var related = ParseBulletOrLines(relatedBody);
        var keywords = ParseKeywords(keywordsBody);
        var additional = NormalizeBlock(additionalBody);

        if (layerBody is null)
        {
            warnings.Add("Manifest missing '## Layer' section.");
        }
        else if (string.IsNullOrWhiteSpace(layer))
        {
            warnings.Add("Manifest Layer section is empty.");
        }

        if (brandsBody is null)
        {
            warnings.Add("Manifest missing '## Brands' section.");
        }
        else if (brands.Count == 0)
        {
            warnings.Add("Manifest Brands section is empty.");
        }

        if (appsBody is null)
        {
            warnings.Add("Manifest missing '## Applications / Services' section.");
        }
        else if (applications.Count == 0)
        {
            warnings.Add("Manifest Applications / Services section is empty.");
        }

        if (teamBody is null)
        {
            warnings.Add("Manifest missing '## Team' section.");
        }

        return new WorkspaceManifestDocument
        {
            Present = true,
            Purpose = purpose,
            MaintenanceRules = rules,
            Layer = layer,
            Team = team,
            Applications = applications,
            Brands = brands,
            Responsibilities = responsibilities,
            RouteWhen = routeWhen,
            DoNotRouteWhen = doNotRoute,
            RelatedSystems = related,
            Keywords = keywords,
            AdditionalContext = additional,
            Warnings = warnings
        };
    }

    private static List<(string Heading, string Body)> SplitSections(string markdown)
    {
        var result = new List<(string, string)>();
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        string? currentHeading = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (currentHeading is null)
            {
                return;
            }

            result.Add((currentHeading, body.ToString().Trim()));
            body.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw;
            // Match ## Heading (level-2 only for data sections)
            if (line.StartsWith("## ", StringComparison.Ordinal) && !line.StartsWith("###", StringComparison.Ordinal))
            {
                Flush();
                currentHeading = line[3..].Trim();
                continue;
            }

            // Also accept # only for title; ignore other # levels as body
            if (currentHeading is not null)
            {
                body.AppendLine(line);
            }
        }

        Flush();
        return result;
    }

    private static string? ParseLayer(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        // First non-empty, non-placeholder line; strip pipes from template "a | b | c"
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim().TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrWhiteSpace(t) || t is "…" or "...")
            {
                continue;
            }

            // Template lists all layers with | — treat as empty if it looks like the scaffold hint
            if (t.Contains('|', StringComparison.Ordinal)
                && Constants.WorkspaceManifest.SuggestedLayers.Count(l =>
                    t.Contains(l, StringComparison.OrdinalIgnoreCase)) >= 3)
            {
                return null;
            }

            // Take first token if space-separated single layer
            var token = t.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }

        return null;
    }

    private static string? ParseSingleLine(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim().TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrWhiteSpace(t) || t is "…" or "..." or "@team-or-name")
            {
                continue;
            }

            return t;
        }

        return null;
    }

    private static IReadOnlyList<WorkspaceManifestApplication> ParseApplications(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var list = new List<WorkspaceManifestApplication>();
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                continue;
            }

            // Bullet
            if (t.StartsWith('-') || t.StartsWith('*') || t.StartsWith('•'))
            {
                t = t.TrimStart('-', '*', '•').Trim();
            }

            if (string.IsNullOrWhiteSpace(t) || t is "…" or "...")
            {
                continue;
            }

            // Skip pure placeholder
            if (t.Contains("ApplicationOrServiceName", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name;
            string? description = null;
            var sep = t.IndexOf(" — ", StringComparison.Ordinal);
            if (sep < 0)
            {
                sep = t.IndexOf(" - ", StringComparison.Ordinal);
            }

            if (sep < 0)
            {
                sep = t.IndexOf('—');
            }

            if (sep > 0)
            {
                name = t[..sep].Trim();
                description = t[(sep + (t[sep] == '—' ? 1 : 3))..].Trim().TrimStart('—', '-').Trim();
            }
            else
            {
                name = t;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            list.Add(new WorkspaceManifestApplication
            {
                Name = name,
                Description = string.IsNullOrWhiteSpace(description) ? null : description
            });
        }

        return list;
    }

    private static IReadOnlyList<string> ParseBrands(string? body, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var tokens = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t)
                || t.StartsWith('(')
                || t.Contains("Comma-separated", StringComparison.OrdinalIgnoreCase)
                || t is "…" or "...")
            {
                continue;
            }

            if (t.StartsWith('-') || t.StartsWith('*') || t.StartsWith('•'))
            {
                t = t.TrimStart('-', '*', '•').Trim();
            }

            // Comma-separated on one line
            foreach (var part in t.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(part) || part is "…" or "...")
                {
                    continue;
                }

                tokens.Add(part);
            }
        }

        // Scaffold lists all known brands as the template — treat full known set alone as empty
        if (tokens.Count >= Constants.WorkspaceManifest.KnownBrands.Count
            && Constants.WorkspaceManifest.KnownBrands.All(kb =>
                tokens.Any(t => string.Equals(t, kb, StringComparison.OrdinalIgnoreCase))))
        {
            // Only if every token is a known brand and we have exactly the full set (scaffold)
            if (tokens.Count == Constants.WorkspaceManifest.KnownBrands.Count)
            {
                return [];
            }
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var known = Constants.WorkspaceManifest.KnownBrands
                .FirstOrDefault(b => string.Equals(b, token, StringComparison.OrdinalIgnoreCase));
            var normalized = known ?? token;
            if (seen.Add(normalized))
            {
                if (known is null)
                {
                    warnings.Add($"Unknown brand token '{token}' (known: {string.Join(", ", Constants.WorkspaceManifest.KnownBrands)}).");
                }

                result.Add(normalized);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ParseBulletOrLines(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var list = new List<string>();
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                continue;
            }

            if (t.StartsWith('-') || t.StartsWith('*') || t.StartsWith('•'))
            {
                t = t.TrimStart('-', '*', '•').Trim();
            }

            if (string.IsNullOrWhiteSpace(t) || t is "…" or "...")
            {
                continue;
            }

            list.Add(t);
        }

        return list;
    }

    private static IReadOnlyList<string> ParseKeywords(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim().TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrWhiteSpace(t) || t is "…" or "..." || t.Contains("keyword-one", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var part in t.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(part))
                {
                    list.Add(part);
                }
            }
        }

        return list;
    }

    private static string? NormalizeBlock(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
