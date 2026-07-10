using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Renders an <see cref="ArchitectureDocument"/> to agent-friendly Markdown.
/// </summary>
public static class ArchitectureMarkdownRenderer
{
    public static string Render(ArchitectureDocument doc, string repoName, bool includeDisclaimer = true)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var sb = new StringBuilder();

        // Prefer free-form markdown documents returned by models that ignore our schema
        // (e.g. { "architecture_overview": "# Title\n..." }).
        if (!string.IsNullOrWhiteSpace(doc.FullMarkdown))
        {
            if (includeDisclaimer)
            {
                sb.AppendLine(doc.UsedOfflineFallback
                    ? "> Architecture derived from the current repository inventory (no live LLM for this page)."
                    : "> Architecture documentation for the **current** codebase (AI-assisted).");
                sb.AppendLine();
            }

            var body = doc.FullMarkdown.Trim();
            // Avoid duplicating a top-level H1 if the model already included one.
            sb.AppendLine(body);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"_Repository: `{repoName}`_");
            return sb.ToString().TrimEnd() + "\n";
        }

        sb.AppendLine($"# {NullIfEmpty(doc.Title) ?? "Architecture Overview"}");
        sb.AppendLine();

        if (includeDisclaimer)
        {
            if (doc.UsedOfflineFallback)
            {
                sb.AppendLine("> Architecture derived from the current repository inventory (no live LLM for this page).");
            }
            else
            {
                sb.AppendLine("> Architecture documentation for the **current** codebase (AI-assisted).");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(doc.Summary))
        {
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(doc.Summary.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(doc.SystemContext))
        {
            sb.AppendLine("## System context");
            sb.AppendLine();
            sb.AppendLine(doc.SystemContext.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(doc.MermaidDiagram))
        {
            sb.AppendLine("## Diagram");
            sb.AppendLine();
            sb.AppendLine("```mermaid");
            sb.AppendLine(doc.MermaidDiagram.Trim());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (doc.Layers.Count > 0)
        {
            sb.AppendLine("## Layers");
            sb.AppendLine();
            sb.AppendLine("| Layer | Responsibility | Key paths |");
            sb.AppendLine("|-------|----------------|-----------|");
            foreach (var layer in doc.Layers)
            {
                var paths = layer.KeyPaths.Count == 0
                    ? "—"
                    : string.Join(", ", layer.KeyPaths.Select(p => $"`{p}`"));
                sb.AppendLine($"| {EscapeCell(layer.Name)} | {EscapeCell(layer.Responsibility)} | {paths} |");
            }

            sb.AppendLine();
        }

        if (doc.KeyComponents.Count > 0)
        {
            sb.AppendLine("## Key components");
            sb.AppendLine();
            foreach (var component in doc.KeyComponents)
            {
                var path = string.IsNullOrWhiteSpace(component.Path) ? "" : $" (`{component.Path}`)";
                sb.AppendLine($"- **{component.Name}**{path}: {component.Purpose}");
            }

            sb.AppendLine();
        }

        if (doc.DataFlows.Count > 0)
        {
            sb.AppendLine("## Important flows");
            sb.AppendLine();
            for (var i = 0; i < doc.DataFlows.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {doc.DataFlows[i]}");
            }

            sb.AppendLine();
        }

        if (doc.Decisions.Count > 0)
        {
            sb.AppendLine("## Key decisions");
            sb.AppendLine();
            foreach (var decision in doc.Decisions)
            {
                sb.AppendLine($"- {decision}");
            }

            sb.AppendLine();
        }

        if (doc.Gotchas.Count > 0)
        {
            sb.AppendLine("## Gotchas");
            sb.AppendLine();
            foreach (var gotcha in doc.Gotchas)
            {
                sb.AppendLine($"- {gotcha}");
            }

            sb.AppendLine();
        }

        if (doc.HowToExtend.Count > 0)
        {
            sb.AppendLine("## How to extend / modify");
            sb.AppendLine();
            foreach (var tip in doc.HowToExtend)
            {
                sb.AppendLine($"- {tip}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"_Repository: `{repoName}`_");

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
