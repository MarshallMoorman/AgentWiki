using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Renders module and cross-cutting documents to Markdown.
/// </summary>
public static class ModuleMarkdownRenderer
{
    public static string RenderModule(ModuleDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();
        if (doc.UsedOfflineFallback)
        {
            sb.AppendLine("> Module map derived from the current file inventory.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> Current module documentation for coding agents (AI-assisted).");
            sb.AppendLine();
        }

        sb.AppendLine("## Purpose");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(doc.Purpose) ? "_Not specified._" : doc.Purpose.Trim());
        sb.AppendLine();

        AppendList(sb, "Entry points", doc.EntryPoints, ordered: false, asCode: true);
        AppendList(sb, "Dependencies / roots", doc.Dependencies, ordered: false, asCode: true);
        AppendList(sb, "Key types / files", doc.KeyTypes, ordered: false, asCode: false);

        sb.AppendLine("## Endpoints / Public API");
        sb.AppendLine();
        if (doc.Endpoints.Count == 0)
        {
            sb.AppendLine("_No HTTP or Function endpoints discovered for this module._");
            sb.AppendLine();
        }
        else
        {
            ApiEndpointsMarkdownRenderer.AppendEndpointTable(sb, doc.Endpoints, maxRows: 25);
            foreach (var ep in doc.Endpoints.Take(12))
            {
                if (string.IsNullOrWhiteSpace(ep.Description) && ep.Parameters.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"- `{ep.HttpMethod} {ep.Route}` — {ep.Description ?? ep.HandlerName}");
                if (ep.Parameters.Count > 0)
                {
                    sb.AppendLine($"  - Params: {string.Join(", ", ep.Parameters.Select(p => $"`{p}`"))}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("See also [API Endpoints](../api-endpoints.md).");
            sb.AppendLine();
        }

        AppendList(sb, "How to extend", doc.HowToExtend, ordered: false, asCode: false);
        AppendList(sb, "Gotchas", doc.Gotchas, ordered: false, asCode: false);
        AppendList(sb, "Related files", doc.RelatedFiles, ordered: false, asCode: true);

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("- [Wiki index](../index.md)");
        sb.AppendLine("- [Architecture](../architecture.md)");
        sb.AppendLine("- [API Endpoints](../api-endpoints.md)");
        sb.AppendLine();

        return sb.ToString().TrimEnd() + "\n";
    }

    public static string RenderCrossCutting(CrossCuttingDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();
        if (doc.UsedOfflineFallback)
        {
            sb.AppendLine("> Cross-cutting notes derived from the current file inventory.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> Current cross-cutting documentation (AI-assisted).");
            sb.AppendLine();
        }

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(doc.Summary) ? "_Not specified._" : doc.Summary.Trim());
        sb.AppendLine();

        AppendList(sb, "Patterns", doc.Patterns, ordered: false, asCode: false);
        AppendList(sb, "Key files", doc.KeyFiles, ordered: false, asCode: true);
        AppendList(sb, "Guidance for agents", doc.Guidance, ordered: false, asCode: false);

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("- [Wiki index](../index.md)");
        sb.AppendLine("- [Architecture](../architecture.md)");
        sb.AppendLine();

        return sb.ToString().TrimEnd() + "\n";
    }

    public static string RenderIndex(
        string repoName,
        ArchitectureDocument architecture,
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<CrossCuttingDocument> crossCutting,
        RepoStats stats,
        DateTimeOffset generatedAt,
        string correlationId,
        bool offline)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {repoName} — AgentWiki");
        sb.AppendLine();
        sb.AppendLine(offline
            ? "> Agent-optimized documentation generated from the current repository inventory (no live LLM for this run)."
            : "> Agent-optimized documentation for the **current** codebase.");
        sb.AppendLine();

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("| Page | Description |");
        sb.AppendLine("|------|-------------|");
        sb.AppendLine("| [Architecture](architecture.md) | System design, layers, decisions |");
        sb.AppendLine("| [API Endpoints](api-endpoints.md) | HTTP / Function route catalog |");
        sb.AppendLine("| [Key Components](key-components.md) | Component map |");
        sb.AppendLine("| [Data Flows](data-flows.md) | Important request/process flows |");
        sb.AppendLine("| [Repository Inventory](inventory.md) | File inventory summary |");
        sb.AppendLine("| [Glossary](glossary.md) | Terms and abbreviations |");
        sb.AppendLine("| [Getting Started](getting-started.md) | Agent usage guide |");
        sb.AppendLine();

        if (modules.Count > 0)
        {
            sb.AppendLine("### Modules");
            sb.AppendLine();
            sb.AppendLine("| Module | Purpose |");
            sb.AppendLine("|--------|---------|");
            foreach (var module in modules)
            {
                // Full purpose text — agents need complete context; do not truncate.
                sb.AppendLine($"| [{Escape(module.Title)}]({module.RelativePath}) | {Escape(FlattenCell(module.Purpose))} |");
            }

            sb.AppendLine();
        }

        if (crossCutting.Count > 0)
        {
            sb.AppendLine("### Cross-cutting");
            sb.AppendLine();
            sb.AppendLine("| Topic | Summary |");
            sb.AppendLine("|-------|---------|");
            foreach (var item in crossCutting)
            {
                // Full summary — do not truncate for table width.
                sb.AppendLine($"| [{Escape(item.Title)}]({item.RelativePath}) | {Escape(FlattenCell(item.Summary))} |");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Quick facts");
        sb.AppendLine();
        sb.AppendLine($"- **Repository:** `{repoName}`");
        sb.AppendLine($"- **Generated at (UTC):** {generatedAt:O}");
        sb.AppendLine($"- **Files (after ignores):** {stats.TotalFiles}");
        sb.AppendLine($"- **Selected for analysis:** {stats.SelectedFiles}");
        sb.AppendLine($"- **Approx. lines:** {stats.TotalLines:N0}");
        sb.AppendLine($"- **Modules documented:** {modules.Count}");
        sb.AppendLine($"- **Generation mode:** {(architecture.UsedOfflineFallback ? "inventory-based" : "LLM-assisted")}");
        sb.AppendLine($"- **Correlation ID:** `{correlationId}`");
        sb.AppendLine();

        sb.AppendLine("## How to use this wiki");
        sb.AppendLine();
        sb.AppendLine("1. Read [architecture.md](architecture.md) for the current system shape.");
        sb.AppendLine("2. Check [api-endpoints.md](api-endpoints.md) for HTTP / Function routes.");
        sb.AppendLine("3. Open the relevant page under [modules/](modules/) or [cross-cutting/](cross-cutting/).");
        sb.AppendLine("4. Use [inventory.md](inventory.md) when you need exact paths.");
        sb.AppendLine("5. Treat this as a map of the live tree; confirm critical details in source when implementing.");
        sb.AppendLine();

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendList(
        StringBuilder sb,
        string heading,
        IReadOnlyList<string> items,
        bool ordered,
        bool asCode)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        if (items.Count == 0)
        {
            sb.AppendLine("_None listed._");
            sb.AppendLine();
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var body = asCode ? $"`{item}`" : item;
            if (ordered)
            {
                sb.AppendLine($"{i + 1}. {body}");
            }
            else
            {
                sb.AppendLine($"- {body}");
            }
        }

        sb.AppendLine();
    }

    /// <summary>Collapses whitespace for a single Markdown table cell without truncating content.</summary>
    private static string FlattenCell(string value)
    {
        var trimmed = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (trimmed.Contains("  ", StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
