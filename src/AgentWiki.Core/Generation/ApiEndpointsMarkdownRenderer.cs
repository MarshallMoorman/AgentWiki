using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Renders the top-level <c>api-endpoints.md</c> catalog for agents.
/// </summary>
public static class ApiEndpointsMarkdownRenderer
{
    public static string Render(
        string repoName,
        IReadOnlyList<EndpointInfo> endpoints,
        bool usedRoslyn,
        bool llmEnriched)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# API Endpoints");
        sb.AppendLine();
        sb.AppendLine(
            $"> Public HTTP and Azure Function endpoints discovered for **{repoName}**. "
            + (usedRoslyn
                ? "Extracted via Roslyn syntax analysis"
                : "Extracted from inventory heuristics")
            + (llmEnriched ? " with LLM description enrichment." : ".")
            + " Verify against source before calling in production.");
        sb.AppendLine();

        if (endpoints.Count == 0)
        {
            sb.AppendLine("_No HTTP or Function endpoints were discovered._");
            sb.AppendLine();
            sb.AppendLine("This usually means the repository is not an ASP.NET / Azure Functions host, ");
            sb.AppendLine("or Roslyn analysis was disabled / found no matching attributes.");
            sb.AppendLine();
            sb.AppendLine("## Navigation");
            sb.AppendLine();
            sb.AppendLine("- [Wiki index](index.md)");
            sb.AppendLine("- [Architecture](architecture.md)");
            sb.AppendLine("- [Key components](key-components.md)");
            sb.AppendLine();
            return sb.ToString().TrimEnd() + "\n";
        }

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total endpoints:** {endpoints.Count}");
        foreach (var group in endpoints.GroupBy(e => e.Kind).OrderBy(g => g.Key))
        {
            sb.AppendLine($"- **{group.Key}:** {group.Count()}");
        }

        var authCount = endpoints.Count(e => e.AuthHints.Count > 0);
        if (authCount > 0)
        {
            sb.AppendLine($"- **With auth hints:** {authCount}");
        }

        sb.AppendLine();
        sb.AppendLine("## Catalog");
        sb.AppendLine();
        sb.AppendLine("| Method | Route | Handler | Kind | Auth | Source |");
        sb.AppendLine("|--------|-------|---------|------|------|--------|");
        foreach (var ep in endpoints)
        {
            var auth = ep.AuthHints.Count == 0 ? "—" : string.Join(", ", ep.AuthHints);
            sb.AppendLine(
                $"| `{Escape(ep.HttpMethod)}` | `{Escape(ep.Route)}` | {Escape(ep.HandlerName)} | {Escape(ep.Kind)} | {Escape(auth)} | `{Escape(ep.RelativePath)}` |");
        }

        sb.AppendLine();

        // Detail sections by kind
        foreach (var group in endpoints.GroupBy(e => e.Kind).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {TitleCaseKind(group.Key)}");
            sb.AppendLine();
            foreach (var ep in group)
            {
                sb.AppendLine($"### `{ep.HttpMethod}` `{ep.Route}`");
                sb.AppendLine();
                sb.AppendLine($"- **Handler:** `{ep.HandlerName}`");
                sb.AppendLine($"- **Source:** `{ep.RelativePath}`");
                if (!string.IsNullOrWhiteSpace(ep.ProjectName))
                {
                    sb.AppendLine($"- **Project:** `{ep.ProjectName}`");
                }

                if (ep.AuthHints.Count > 0)
                {
                    sb.AppendLine($"- **Auth:** {string.Join(", ", ep.AuthHints.Select(a => $"`{a}`"))}");
                }

                if (ep.Parameters.Count > 0)
                {
                    sb.AppendLine($"- **Parameters:** {string.Join(", ", ep.Parameters.Select(p => $"`{p}`"))}");
                }

                if (!string.IsNullOrWhiteSpace(ep.Description))
                {
                    sb.AppendLine($"- **Purpose:** {ep.Description.Trim()}");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("- [Wiki index](index.md)");
        sb.AppendLine("- [Architecture](architecture.md)");
        sb.AppendLine("- [Key components](key-components.md)");
        sb.AppendLine("- Module pages under [modules/](modules/) list endpoints scoped to each area.");
        sb.AppendLine();

        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>Compact markdown table for embedding in key-components or modules.</summary>
    public static void AppendEndpointTable(StringBuilder sb, IReadOnlyList<EndpointInfo> endpoints, int maxRows = 40)
    {
        if (endpoints.Count == 0)
        {
            sb.AppendLine("_None discovered._");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Method | Route | Handler | Auth |");
        sb.AppendLine("|--------|-------|---------|------|");
        foreach (var ep in endpoints.Take(maxRows))
        {
            var auth = ep.AuthHints.Count == 0 ? "—" : string.Join(", ", ep.AuthHints);
            sb.AppendLine(
                $"| `{Escape(ep.HttpMethod)}` | `{Escape(ep.Route)}` | {Escape(ep.HandlerName)} | {Escape(auth)} |");
        }

        if (endpoints.Count > maxRows)
        {
            sb.AppendLine();
            sb.AppendLine($"_…and {endpoints.Count - maxRows} more. See [api-endpoints.md](api-endpoints.md)._");
        }

        sb.AppendLine();
    }

    private static string TitleCaseKind(string kind) => kind switch
    {
        "minimal-api" => "Minimal APIs",
        "controller" => "Controllers",
        "function" => "Azure Functions",
        _ => kind
    };

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
