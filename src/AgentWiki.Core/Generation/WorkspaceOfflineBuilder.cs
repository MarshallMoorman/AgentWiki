using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Builds file-based system wiki pages for a multi-repo workspace (offline / Phase 1).
/// No embeddings or vector retrieval — Markdown only.
/// </summary>
public static class WorkspaceOfflineBuilder
{
    /// <summary>Builds all system wiki sections for the workspace analysis.</summary>
    public static IReadOnlyList<WikiSection> BuildSections(WorkspaceAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var config = analysis.Config;
        var wikiRel = NormalizeRel(config.OutputPath);
        var sections = new List<WikiSection>
        {
            new("index", "Workspace Index", "index.md", BuildIndex(analysis, wikiRel)),
            new("architecture", "System Architecture", "architecture.md", BuildArchitecture(analysis)),
            new("dependency-graph", "Dependency Graph", "dependency-graph.md", BuildDependencyGraph(analysis)),
            new("data-flows", "Data Flows & Contracts", "data-flows.md", BuildDataFlows(analysis)),
            new("ownership", "Ownership Map", "ownership.md", BuildOwnership(analysis))
        };

        foreach (var member in analysis.Members.Where(m => m.Resolved.Success))
        {
            var id = member.Resolved.Definition.Id;
            sections.Add(new WikiSection(
                $"member-{id}",
                member.Resolved.Definition.DisplayName,
                $"members/{id}.md",
                BuildMemberPage(member, analysis)));
        }

        return sections;
    }

    /// <summary>
    /// Builds a full workspace-level AGENTS.md guiding agents to start at the root
    /// knowledge base and drill into member wikis.
    /// </summary>
    public static string BuildAgentsMd(WorkspaceAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var config = analysis.Config;
        var wiki = NormalizeRel(config.OutputPath);
        var sb = new StringBuilder();

        sb.AppendLine($"# AGENTS.md — {config.Name} (workspace)");
        sb.AppendLine();
        sb.AppendLine("## Start here (workspace)");
        sb.AppendLine();
        sb.AppendLine("This is a **multi-repo workspace**. Prefer the system knowledge base before diving into a single repository.");
        sb.AppendLine();
        sb.AppendLine("1. Read this file for workspace-level workflow and guardrails.");
        sb.AppendLine($"2. Start at `{wiki}index.md` and `{wiki}architecture.md` for the system map.");
        sb.AppendLine($"3. Use `{wiki}dependency-graph.md`, `{wiki}data-flows.md`, and `{wiki}ownership.md` for cross-repo orientation.");
        sb.AppendLine($"4. Open the relevant member summary under `{wiki}members/` for deep links into that repo’s own wiki.");
        sb.AppendLine("5. Only then edit code inside a member repository; verify against that member’s source and `AGENTS.md` when present.");
        sb.AppendLine();
        sb.AppendLine("## Workspace snapshot");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            sb.AppendLine(config.Description.Trim());
            sb.AppendLine();
        }

        sb.AppendLine($"- **Name:** {config.Name}");
        sb.AppendLine($"- **System wiki:** `{wiki}`");
        sb.AppendLine($"- **Members:** {analysis.Members.Count}");
        sb.AppendLine($"- **Refresh:** `agent-wiki workspace generate` / `workspace update`");
        sb.AppendLine();
        sb.AppendLine("### Member repositories");
        sb.AppendLine();
        sb.AppendLine("| Id | Label | Role | Member wiki |");
        sb.AppendLine("|----|-------|------|-------------|");
        foreach (var m in analysis.Members)
        {
            var def = m.Resolved.Definition;
            var wikiStatus = m.WikiStatus;
            var wikiNote = wikiStatus is null
                ? "unknown"
                : wikiStatus.Exists
                    ? (wikiStatus.IsStale ? "stale" : "ok")
                    : "missing";
            var memberWikiLink = wikiStatus is { Exists: true }
                ? $"`{def.WikiPath.TrimEnd('/')}/index.md` ({wikiNote})"
                : wikiNote;
            sb.AppendLine(
                $"| `{def.Id}` | {EscapeCell(def.DisplayName)} | {EscapeCell(def.Role ?? "—")} | {memberWikiLink} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Locating work");
        sb.AppendLine();
        sb.AppendLine("- Use system pages to decide **which member repos** are in scope for a story.");
        sb.AppendLine("- Prefer member `docs/wiki/` for detailed modules; do not duplicate that content here.");
        sb.AppendLine("- When a member already has a rich `AGENTS.md`, follow that file for in-repo conventions.");
        sb.AppendLine();

        sb.AppendLine(BuildWorkspaceBootstrapBlock(wiki).TrimEnd());
        sb.AppendLine();
        AppendWorkspaceSelfUpdateSection(sb);
        sb.AppendLine();
        sb.AppendLine("## Guardrails");
        sb.AppendLine();
        sb.AppendLine("- Do not commit secrets, API keys, or `.env` files with credentials.");
        sb.AppendLine("- Do not force-push shared branches without explicit human request.");
        sb.AppendLine("- Keep workspace and member docs file-based and reviewable (no vector DB required for Phase 1).");
        sb.AppendLine("- Prefer linking to member wikis over copying their architecture pages.");

        return EnsureTrailingNewline(sb.ToString());
    }

    /// <summary>AgentWiki marker block for workspace AGENTS.md.</summary>
    public static string BuildWorkspaceBootstrapBlock(string wikiRelativePath)
    {
        var wiki = NormalizeRel(wikiRelativePath);
        var sb = new StringBuilder();
        sb.AppendLine(Constants.AgentsMd.MarkerBegin);
        sb.AppendLine("## AgentWiki Workspace Documentation");
        sb.AppendLine($"This workspace maintains a **system knowledge base** at `{wiki}`.");
        sb.AppendLine();
        sb.AppendLine("**For any multi-repo task:**");
        sb.AppendLine($"1. Start by reading `{wiki}index.md` and `{wiki}architecture.md`");
        sb.AppendLine($"2. Review `{wiki}dependency-graph.md`, `{wiki}data-flows.md`, and `{wiki}ownership.md`");
        sb.AppendLine($"3. Open the matching page under `{wiki}members/` for deep links into member repos");
        sb.AppendLine("4. Drill into each member’s own `docs/wiki/` (and `AGENTS.md`) for implementation detail");
        sb.AppendLine(
            "5. Refresh with `agent-wiki workspace generate` / `workspace update`. Prefer wiki maps, then verify against source.");
        sb.AppendLine(Constants.AgentsMd.MarkerEnd);
        return EnsureTrailingNewline(sb.ToString());
    }

    public static void AppendWorkspaceSelfUpdateSection(StringBuilder sb)
    {
        sb.AppendLine(Constants.AgentsMd.SelfUpdateSectionHeading);
        sb.AppendLine();
        sb.AppendLine(
            "When you change **how agents should work across this workspace** (members, system boundaries, ownership, " +
            "or preferred entry points), update this **workspace AGENTS.md** in the **same change**.");
        sb.AppendLine();
        sb.AppendLine("Examples:");
        sb.AppendLine();
        sb.AppendLine("- Adding/removing workspace members");
        sb.AppendLine("- New cross-repo dependencies or contracts");
        sb.AppendLine("- Changed ownership or on-call maps");
        sb.AppendLine("- New system-level workflow for agents");
        sb.AppendLine();
        sb.AppendLine("Guidance:");
        sb.AppendLine();
        sb.AppendLine("- Re-run `agent-wiki workspace generate` or `workspace update` when the system map is stale.");
        sb.AppendLine(
            "- Keep the AgentWiki marker block (`<!-- BEGIN AGENTWIKI -->` … `<!-- END AGENTWIKI -->`) intact.");
        sb.AppendLine("- Prefer small precise edits; do not delete member-specific AGENTS.md guidance.");
    }

    private static string BuildIndex(WorkspaceAnalysisResult analysis, string wikiRel)
    {
        var config = analysis.Config;
        var sb = new StringBuilder();
        sb.AppendLine($"# {config.Name} — System Knowledge Base");
        sb.AppendLine();
        sb.AppendLine("> Generated by AgentWiki workspace mode (file-based). Start here for multi-repo orientation.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            sb.AppendLine(config.Description.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Navigation");
        sb.AppendLine();
        sb.AppendLine("| Page | Purpose |");
        sb.AppendLine("|------|---------|");
        sb.AppendLine("| [Architecture](architecture.md) | System design, layers, key decisions |");
        sb.AppendLine("| [Dependency graph](dependency-graph.md) | Repo + package relationships |");
        sb.AppendLine("| [Data flows](data-flows.md) | Contracts, APIs, messaging hints |");
        sb.AppendLine("| [Ownership](ownership.md) | CODEOWNERS and team hints |");
        sb.AppendLine();
        sb.AppendLine("## Members");
        sb.AppendLine();
        sb.AppendLine("| Member | Role | Wiki | Summary |");
        sb.AppendLine("|--------|------|------|---------|");
        foreach (var m in analysis.Members)
        {
            var def = m.Resolved.Definition;
            var summaryLink = m.Resolved.Success
                ? $"[summary](members/{def.Id}.md)"
                : "unresolved";
            var wikiLink = FormatMemberWikiLink(m);
            var role = string.IsNullOrWhiteSpace(def.Role) ? "—" : def.Role;
            sb.AppendLine($"| **{EscapeCell(def.DisplayName)}** (`{def.Id}`) | {EscapeCell(role)} | {wikiLink} | {summaryLink} |");
        }

        sb.AppendLine();
        sb.AppendLine("## How agents should use this");
        sb.AppendLine();
        sb.AppendLine("1. Read architecture + dependency graph to scope the change.");
        sb.AppendLine("2. Open only the member summary pages that are in scope.");
        sb.AppendLine("3. Follow deep links into each member’s `docs/wiki/` for modules and APIs.");
        sb.AppendLine("4. Prefer member source of truth when wiki and code disagree.");
        sb.AppendLine();
        sb.AppendLine($"Workspace root AGENTS.md points agents at `{wikiRel}`.");
        if (analysis.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings from last generation");
            sb.AppendLine();
            foreach (var w in analysis.Warnings.Take(20))
            {
                sb.AppendLine($"- {w}");
            }
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string BuildArchitecture(WorkspaceAnalysisResult analysis)
    {
        var config = analysis.Config;
        var sb = new StringBuilder();
        sb.AppendLine($"# System architecture — {config.Name}");
        sb.AppendLine();
        sb.AppendLine("Offline system map derived from member inventories and cross-repo signals.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            sb.AppendLine("## Intent");
            sb.AppendLine();
            sb.AppendLine(config.Description.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Member landscape");
        sb.AppendLine();
        foreach (var m in analysis.Members.Where(x => x.Resolved.Success))
        {
            var def = m.Resolved.Definition;
            sb.AppendLine($"### {def.DisplayName} (`{def.Id}`)");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(def.Role))
            {
                sb.AppendLine($"- **Role:** {def.Role}");
            }

            if (!string.IsNullOrWhiteSpace(def.Notes))
            {
                sb.AppendLine($"- **Notes:** {def.Notes.Trim()}");
            }

            if (m.Analysis is { } a)
            {
                var langs = a.Stats.DetectedLanguages.Count > 0
                    ? string.Join(", ", a.Stats.DetectedLanguages.Take(6))
                    : "unknown";
                sb.AppendLine($"- **Inventory:** {a.Stats.TotalFiles} files · languages: {langs}");
                if (a.Stats.TopFolders.Count > 0)
                {
                    sb.AppendLine(
                        "- **Top folders:** "
                        + string.Join(", ", a.Stats.TopFolders.Take(6).Select(f => $"`{f.RelativePath}/`")));
                }
            }

            sb.AppendLine($"- **Member summary:** [members/{def.Id}.md](members/{def.Id}.md)");
            sb.AppendLine($"- **Member wiki:** {FormatMemberWikiLink(m)}");
            sb.AppendLine();
        }

        sb.AppendLine("## Suggested layering (heuristic)");
        sb.AppendLine();
        sb.AppendLine("Roles declared in workspace config are used when present:");
        sb.AppendLine();
        var byRole = analysis.Members
            .Where(m => m.Resolved.Success)
            .GroupBy(m => string.IsNullOrWhiteSpace(m.Resolved.Definition.Role)
                ? "unspecified"
                : m.Resolved.Definition.Role!.Trim().ToLowerInvariant())
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var g in byRole)
        {
            sb.AppendLine($"- **{g.Key}:** {string.Join(", ", g.Select(m => $"`{m.Resolved.Definition.Id}`"))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Key decisions");
        sb.AppendLine();
        sb.AppendLine("- System context lives at the workspace root; implementation detail stays in member wikis.");
        sb.AppendLine("- Prefer package and contract signals (see dependency graph / data flows) over guessing call paths.");
        sb.AppendLine("- Refresh with `agent-wiki workspace generate` after structural member changes.");

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string BuildDependencyGraph(WorkspaceAnalysisResult analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dependency graph");
        sb.AppendLine();
        sb.AppendLine("Cross-repo package and project-reference signals (file heuristics).");
        sb.AppendLine();

        sb.AppendLine("## Members");
        sb.AppendLine();
        foreach (var m in analysis.Members.Where(x => x.Resolved.Success))
        {
            sb.AppendLine($"- `{m.Resolved.Definition.Id}` — {m.Resolved.Definition.DisplayName}");
        }

        sb.AppendLine();
        sb.AppendLine("## Shared packages");
        sb.AppendLine();
        var packages = analysis.Signals.SharedPackages
            .Where(p => p.MemberIds.Count >= 2)
            .OrderByDescending(p => p.MemberIds.Count)
            .ThenBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
        if (packages.Count == 0)
        {
            sb.AppendLine("_No shared packages detected across members (or members are not .NET/npm)._");
        }
        else
        {
            sb.AppendLine("| Package | Ecosystem | Members | Versions |");
            sb.AppendLine("|---------|-----------|---------|----------|");
            foreach (var p in packages)
            {
                sb.AppendLine(
                    $"| `{EscapeCell(p.PackageId)}` | {p.Ecosystem} | {string.Join(", ", p.MemberIds.Select(id => $"`{id}`"))} | {string.Join(", ", p.Versions.Take(5))} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Project references (with possible cross-member matches)");
        sb.AppendLine();
        var refs = analysis.Signals.ProjectReferences.Take(50).ToList();
        if (refs.Count == 0)
        {
            sb.AppendLine("_No project references discovered._");
        }
        else
        {
            sb.AppendLine("| From member | From project | Reference | Matched member |");
            sb.AppendLine("|-------------|--------------|-----------|----------------|");
            foreach (var r in refs)
            {
                sb.AppendLine(
                    $"| `{r.FromMemberId}` | `{EscapeCell(r.FromProject)}` | `{EscapeCell(r.ToReference)}` | {(r.MatchedMemberId is null ? "—" : $"`{r.MatchedMemberId}`")} |");
            }
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string BuildDataFlows(WorkspaceAnalysisResult analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Data flows & contracts");
        sb.AppendLine();
        sb.AppendLine("Discovered OpenAPI / contract / messaging schema files and high-level notes.");
        sb.AppendLine();
        sb.AppendLine("## Contract artifacts");
        sb.AppendLine();
        if (analysis.Signals.Contracts.Count == 0)
        {
            sb.AppendLine("_No OpenAPI/AsyncAPI/proto/schema files detected in member inventories._");
        }
        else
        {
            sb.AppendLine("| Member | Kind | Path |");
            sb.AppendLine("|--------|------|------|");
            foreach (var c in analysis.Signals.Contracts.Take(60))
            {
                sb.AppendLine($"| `{c.MemberId}` | {c.Kind} | `{EscapeCell(c.RelativePath)}` |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- Treat these paths as starting points for integration work; verify runtime wiring in code.");
        sb.AppendLine("- HTTP endpoint catalogs (when present) live under each member’s `docs/wiki/` (`api-endpoints.md`).");
        foreach (var note in analysis.Signals.Notes.Take(10))
        {
            sb.AppendLine($"- {note}");
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string BuildOwnership(WorkspaceAnalysisResult analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Ownership map");
        sb.AppendLine();
        sb.AppendLine("Ownership hints from CODEOWNERS and related files.");
        sb.AppendLine();
        if (analysis.Signals.Ownership.Count == 0)
        {
            sb.AppendLine("_No CODEOWNERS (or similar) files found in members._");
            sb.AppendLine();
            sb.AppendLine("Add `CODEOWNERS` or document owners in member `AGENTS.md` / README for richer maps.");
        }
        else
        {
            foreach (var o in analysis.Signals.Ownership)
            {
                sb.AppendLine($"## `{o.MemberId}` — `{o.SourcePath}`");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(o.Excerpt.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string BuildMemberPage(WorkspaceMemberAnalysis member, WorkspaceAnalysisResult analysis)
    {
        var def = member.Resolved.Definition;
        var sb = new StringBuilder();
        sb.AppendLine($"# {def.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"Workspace member id: `{def.Id}`");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(def.Role))
        {
            sb.AppendLine($"**Role:** {def.Role}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(def.Notes))
        {
            sb.AppendLine(def.Notes.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("## Location");
        sb.AppendLine();
        if (member.Resolved.IsRemote)
        {
            sb.AppendLine($"- **Remote:** `{def.Remote}`");
            if (!string.IsNullOrWhiteSpace(member.Resolved.ResolvedBranch))
            {
                sb.AppendLine($"- **Branch:** `{member.Resolved.ResolvedBranch}`");
            }

            if (!string.IsNullOrWhiteSpace(member.Resolved.CachePath))
            {
                sb.AppendLine($"- **Local cache:** `{member.Resolved.CachePath}`");
            }
        }
        else if (!string.IsNullOrWhiteSpace(def.Path))
        {
            sb.AppendLine($"- **Configured path:** `{def.Path}`");
        }

        if (!string.IsNullOrWhiteSpace(member.Resolved.HeadSha))
        {
            sb.AppendLine($"- **HEAD:** `{member.Resolved.HeadSha[..Math.Min(12, member.Resolved.HeadSha.Length)]}`");
        }

        sb.AppendLine();
        sb.AppendLine("## Member wiki (deep links)");
        sb.AppendLine();
        var wikiRel = def.WikiPath.Replace('\\', '/').TrimEnd('/');
        if (member.WikiStatus is { Exists: true })
        {
            sb.AppendLine($"Member wiki root: `{wikiRel}/`");
            sb.AppendLine();
            sb.AppendLine("| Page | Path |");
            sb.AppendLine("|------|------|");
            sb.AppendLine($"| Index | `{wikiRel}/index.md` |");
            sb.AppendLine($"| Architecture | `{wikiRel}/architecture.md` |");
            sb.AppendLine($"| Modules | `{wikiRel}/modules/` |");
            sb.AppendLine($"| Cross-cutting | `{wikiRel}/cross-cutting/` |");
            if (member.WikiStatus.IsStale)
            {
                sb.AppendLine();
                sb.AppendLine(
                    $"> **Stale:** member wiki may be outdated. Run `agent-wiki generate` or `agent-wiki update` in the member repo (`{def.Id}`).");
            }
        }
        else
        {
            sb.AppendLine(
                $"> **Missing wiki:** no `{wikiRel}/index.md` found. "
                + $"Run `agent-wiki generate --repo-path <path-to-{def.Id}> --force` first, "
                + "or re-run `agent-wiki workspace generate` with member wiki ensure enabled.");
        }

        sb.AppendLine();
        sb.AppendLine("## Inventory snapshot");
        sb.AppendLine();
        if (member.Analysis is { } a)
        {
            sb.AppendLine($"- **Repo name:** {a.RepoName}");
            sb.AppendLine($"- **Files:** {a.Stats.TotalFiles} (selected {a.Stats.SelectedFiles})");
            if (a.Stats.DetectedLanguages.Count > 0)
            {
                sb.AppendLine($"- **Languages:** {string.Join(", ", a.Stats.DetectedLanguages)}");
            }

            var projects = a.Files
                .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                            || f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                            || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.RelativePath.Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();
            if (projects.Count > 0)
            {
                sb.AppendLine("- **Projects / solutions:**");
                foreach (var p in projects)
                {
                    sb.AppendLine($"  - `{p}`");
                }
            }
        }
        else
        {
            sb.AppendLine("_Inventory not available for this member._");
        }

        if (member.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var w in member.Warnings)
            {
                sb.AppendLine($"- {w}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Related system pages");
        sb.AppendLine();
        sb.AppendLine("- [Index](../index.md)");
        sb.AppendLine("- [Architecture](../architecture.md)");
        sb.AppendLine("- [Dependency graph](../dependency-graph.md)");

        // Cross-links: packages / contracts for this member
        var memberPackages = analysis.Signals.SharedPackages
            .Where(p => p.MemberIds.Contains(def.Id, StringComparer.OrdinalIgnoreCase) && p.MemberIds.Count >= 2)
            .Take(10)
            .ToList();
        if (memberPackages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Shared packages involving this member");
            sb.AppendLine();
            foreach (var p in memberPackages)
            {
                sb.AppendLine($"- `{p.PackageId}` with {string.Join(", ", p.MemberIds.Where(id => !id.Equals(def.Id, StringComparison.OrdinalIgnoreCase)).Select(id => $"`{id}`"))}");
            }
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    private static string FormatMemberWikiLink(WorkspaceMemberAnalysis member)
    {
        var def = member.Resolved.Definition;
        var wikiRel = def.WikiPath.Replace('\\', '/').TrimEnd('/');
        if (member.WikiStatus is not { Exists: true })
        {
            return $"_missing_ (`{wikiRel}/`)";
        }

        // Document path only — relative links across sibling repos are not stable on disk.
        return $"`{wikiRel}/index.md`";
    }

    private static string NormalizeRel(string path) =>
        path.Replace('\\', '/').TrimEnd('/') + "/";

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + Environment.NewLine;
}
