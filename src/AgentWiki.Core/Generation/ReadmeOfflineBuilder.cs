using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>Builds an informative offline README.md from repository analysis and optional wiki excerpts.</summary>
public static class ReadmeOfflineBuilder
{
    public static string Build(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        string wikiRelativePath,
        bool wikiExists,
        IReadOnlyDictionary<string, string>? wikiExcerpts = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        var wiki = wikiRelativePath.Replace('\\', '/').TrimEnd('/') + "/";
        var languages = analysis.Stats.DetectedLanguages.Count > 0
            ? string.Join(", ", analysis.Stats.DetectedLanguages.Take(8))
            : "see repository inventory";

        var solutions = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Length)
            .Take(5)
            .ToList();

        var projects = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {analysis.RepoName}");
        sb.AppendLine();

        // Prefer architecture summary when wiki already exists
        if (wikiExcerpts is not null
            && wikiExcerpts.TryGetValue("architecture", out var arch)
            && !string.IsNullOrWhiteSpace(arch))
        {
            var blurb = ExtractArchitectureBlurb(arch, analysis.RepoName);
            if (!string.IsNullOrWhiteSpace(blurb))
            {
                sb.AppendLine(blurb);
                sb.AppendLine();
            }
        }

        if (sb.Length < 40)
        {
            sb.AppendLine(
                $"{analysis.RepoName} is a software project with approximately **{analysis.Stats.TotalFiles}** "
                + $"tracked files after ignores (**{languages}**).");
            sb.AppendLine();
        }

        sb.AppendLine("## Quick start");
        sb.AppendLine();
        AppendCommands(sb, analysis, solutions);
        sb.AppendLine();

        if (projects.Count > 0 || solutions.Count > 0)
        {
            sb.AppendLine("## Solution layout");
            sb.AppendLine();
            if (solutions.Count > 0)
            {
                sb.AppendLine("Solutions:");
                foreach (var s in solutions)
                {
                    sb.AppendLine($"- `{s}`");
                }

                sb.AppendLine();
            }

            if (projects.Count > 0)
            {
                sb.AppendLine("Notable projects:");
                foreach (var p in projects)
                {
                    sb.AppendLine($"- `{p}`");
                }

                sb.AppendLine();
            }
        }

        var topFolders = analysis.Stats.TopFolders.Take(10).ToList();
        if (topFolders.Count > 0)
        {
            sb.AppendLine("## Repository structure");
            sb.AppendLine();
            foreach (var folder in topFolders)
            {
                sb.AppendLine($"- `{folder.RelativePath.Replace('\\', '/')}/` — {folder.FileCount} file(s)");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Configuration");
        sb.AppendLine();
        sb.AppendLine(
            $"- AgentWiki config (if used): `{Constants.Paths.ConfigDirectoryName}/{Constants.Paths.ConfigFileName}`");
        sb.AppendLine("- Prefer secrets in local `.env` or CI secret stores — never commit API keys.");
        if (!string.IsNullOrWhiteSpace(config.DefaultModel) || !string.IsNullOrWhiteSpace(config.Provider))
        {
            sb.AppendLine(
                $"- AgentWiki default model/provider: `{config.DefaultModel}` / `{config.Provider}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Documentation for coding agents");
        sb.AppendLine();
        sb.AppendLine(
            $"- **[{Constants.Paths.DefaultAgentMdPath}]({Constants.Paths.DefaultAgentMdPath})** — instructions for AI coding agents "
            + "(start here for agent workflows). Keep it updated when build commands, modules, or conventions change.");
        if (wikiExists)
        {
            sb.AppendLine(
                $"- **[{wiki}]({wiki})** — agent-optimized wiki (`index.md`, architecture, modules, API endpoints).");
            sb.AppendLine(
                $"- Refresh with `agent-wiki generate` / `agent-wiki update` when the structure changes significantly.");
        }
        else
        {
            sb.AppendLine(
                $"- Run `agent-wiki generate` to create an agent-optimized wiki under `{wiki}`.");
        }

        sb.AppendLine();
        sb.AppendLine("## License");
        sb.AppendLine();
        var license = DetectLicense(analysis);
        sb.AppendLine(license is null
            ? "Add a LICENSE file if this project is open source."
            : $"See [`{license}`]({license}).");

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string? ExtractArchitectureBlurb(string architectureMarkdown, string repoName)
    {
        // Prefer ## System Context section body; else first meaningful paragraph after title.
        var lines = architectureMarkdown.Replace("\r\n", "\n").Split('\n');
        var inContext = false;
        var buffer = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inContext)
                {
                    break;
                }

                inContext = t.Contains("System Context", StringComparison.OrdinalIgnoreCase)
                            || t.Contains("Overview", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inContext)
            {
                continue;
            }

            if (t.StartsWith('>') || t.StartsWith("```") || t.Length == 0)
            {
                if (buffer.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (t.StartsWith('#') || t.StartsWith('|') || t.StartsWith('-'))
            {
                if (buffer.Count > 0)
                {
                    break;
                }

                continue;
            }

            buffer.Add(t);
            if (string.Join(' ', buffer).Length > 400)
            {
                break;
            }
        }

        if (buffer.Count > 0)
        {
            return string.Join(' ', buffer);
        }

        // Fallback: first non-heading sentence mentioning the repo or "is a"
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length < 40 || t.StartsWith('#') || t.StartsWith('>') || t.StartsWith('|'))
            {
                continue;
            }

            if (t.Contains(repoName, StringComparison.OrdinalIgnoreCase)
                || t.Contains(" is a ", StringComparison.OrdinalIgnoreCase)
                || t.Contains(" is an ", StringComparison.OrdinalIgnoreCase))
            {
                return t.Length > 500 ? t[..497] + "…" : t;
            }
        }

        return null;
    }

    private static void AppendCommands(
        StringBuilder sb,
        RepoAnalysisResult analysis,
        IReadOnlyList<string> solutions)
    {
        var hasDotnet = solutions.Count > 0
                        || analysis.Files.Any(f =>
                            f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasNode = analysis.Files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "package.json", StringComparison.OrdinalIgnoreCase));

        if (hasDotnet)
        {
            var sln = solutions.FirstOrDefault();
            sb.AppendLine("```bash");
            if (sln is not null)
            {
                sb.AppendLine($"dotnet build {sln}");
                sb.AppendLine($"dotnet test {sln}");
            }
            else
            {
                sb.AppendLine("dotnet build");
                sb.AppendLine("dotnet test");
            }

            sb.AppendLine("```");
            return;
        }

        if (hasNode)
        {
            sb.AppendLine("```bash");
            sb.AppendLine("npm install");
            sb.AppendLine("npm test");
            sb.AppendLine("```");
            return;
        }

        sb.AppendLine("Consult CI workflows and project files for the authoritative build and test commands.");
    }

    private static string? DetectLicense(RepoAnalysisResult analysis)
    {
        var license = analysis.Files.FirstOrDefault(f =>
        {
            var name = Path.GetFileName(f.RelativePath);
            return name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("LICENSE.md", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase);
        });
        return license?.RelativePath.Replace('\\', '/');
    }
}
