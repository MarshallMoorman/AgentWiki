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
            sb.AppendLine("> Offline / inventory-derived module page. Verify against source.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("> AI-generated module documentation optimized for coding agents.");
            sb.AppendLine();
        }

        sb.AppendLine("## Purpose");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(doc.Purpose) ? "_Not specified._" : doc.Purpose.Trim());
        sb.AppendLine();

        AppendList(sb, "Entry points", doc.EntryPoints, ordered: false, asCode: true);
        AppendList(sb, "Dependencies / roots", doc.Dependencies, ordered: false, asCode: true);
        AppendList(sb, "Key types / files", doc.KeyTypes, ordered: false, asCode: false);
        AppendList(sb, "How to extend", doc.HowToExtend, ordered: false, asCode: false);
        AppendList(sb, "Gotchas", doc.Gotchas, ordered: false, asCode: false);
        AppendList(sb, "Related files", doc.RelatedFiles, ordered: false, asCode: true);

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("- [Wiki index](../index.md)");
        sb.AppendLine("- [Architecture](../architecture.md)");
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
            sb.AppendLine("> Offline / inventory-derived cross-cutting notes. Verify against source.");
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
            ? "> **Agent-optimized documentation** (offline multi-step generation). Review before relying on it."
            : "> **Agent-optimized documentation** generated via multi-step Semantic Kernel pipeline.");
        sb.AppendLine();

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("| Page | Description |");
        sb.AppendLine("|------|-------------|");
        sb.AppendLine("| [Architecture](architecture.md) | System design, layers, decisions |");
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
                var purpose = Truncate(module.Purpose, 100);
                sb.AppendLine($"| [{Escape(module.Title)}]({module.RelativePath}) | {Escape(purpose)} |");
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
                sb.AppendLine($"| [{Escape(item.Title)}]({item.RelativePath}) | {Escape(Truncate(item.Summary, 100))} |");
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
        sb.AppendLine($"- **Architecture source:** {(architecture.UsedOfflineFallback ? "offline" : "LLM")}");
        sb.AppendLine($"- **Correlation ID:** `{correlationId}`");
        sb.AppendLine();

        sb.AppendLine("## How to use this wiki");
        sb.AppendLine();
        sb.AppendLine("1. Read [architecture.md](architecture.md) first.");
        sb.AppendLine("2. Drill into relevant [modules](modules/) and [cross-cutting](cross-cutting/) pages.");
        sb.AppendLine("3. Use [inventory.md](inventory.md) for concrete paths.");
        sb.AppendLine("4. Verify AI-generated guidance against source before large changes.");
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

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Replace('\n', ' ').Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
