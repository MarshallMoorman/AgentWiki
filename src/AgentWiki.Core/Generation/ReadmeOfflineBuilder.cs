using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>Builds an informative offline README.md from repository analysis.</summary>
public static class ReadmeOfflineBuilder
{
    public static string Build(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        string wikiRelativePath,
        bool wikiExists)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        var wiki = wikiRelativePath.Replace('\\', '/').TrimEnd('/') + "/";
        var languages = analysis.Stats.DetectedLanguages.Count > 0
            ? string.Join(", ", analysis.Stats.DetectedLanguages.Take(8))
            : "see repository inventory";

        var sb = new StringBuilder();
        sb.AppendLine($"# {analysis.RepoName}");
        sb.AppendLine();
        sb.AppendLine(
            $"{analysis.RepoName} is a software project with approximately **{analysis.Stats.TotalFiles}** " +
            $"tracked source/config files after ignores ({languages}).");
        sb.AppendLine();
        sb.AppendLine("## Quick start");
        sb.AppendLine();
        AppendCommands(sb, analysis);
        sb.AppendLine();
        sb.AppendLine("## Configuration");
        sb.AppendLine();
        sb.AppendLine(
            $"- AgentWiki config (if used): `{Constants.Paths.ConfigDirectoryName}/{Constants.Paths.ConfigFileName}`");
        sb.AppendLine("- Prefer secrets in local `.env` or CI secret stores — never commit API keys.");
        sb.AppendLine($"- Default model/provider (AgentWiki): `{config.DefaultModel}` / `{config.Provider}`");
        sb.AppendLine();
        sb.AppendLine("## Documentation for coding agents");
        sb.AppendLine();
        sb.AppendLine(
            $"- **[{Constants.Paths.DefaultAgentMdPath}]({Constants.Paths.DefaultAgentMdPath})** — instructions for AI coding agents " +
            "(start here for agent workflows).");
        if (wikiExists)
        {
            sb.AppendLine(
                $"- **[{wiki}]({wiki})** — agent-optimized wiki (`index.md`, architecture, modules).");
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

    private static void AppendCommands(StringBuilder sb, RepoAnalysisResult analysis)
    {
        var hasDotnet = analysis.Files.Any(f =>
            f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasNode = analysis.Files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "package.json", StringComparison.OrdinalIgnoreCase));

        if (hasDotnet)
        {
            sb.AppendLine("```bash");
            sb.AppendLine("dotnet build");
            sb.AppendLine("dotnet test");
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
