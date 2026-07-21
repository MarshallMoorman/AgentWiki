using System.Text;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Builds file-based system wiki pages for a multi-repo workspace (offline / Phase 1).
/// No embeddings or vector retrieval — Markdown only.
/// </summary>
public static class WorkspaceOfflineBuilder
{
    /// <summary>Builds all system wiki sections for the workspace analysis (Step 02b corpus).</summary>
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
            new("ownership", "Ownership Map", "ownership.md", BuildOwnership(analysis)),
            new(
                "routing-guide",
                "Routing Guide",
                Constants.Workspace.RoutingGuideFileName,
                BuildRoutingGuide(analysis, wikiRel))
        };

        foreach (var member in analysis.Members.Where(m => m.Resolved.Success))
        {
            var id = member.Resolved.Definition.Id;
            sections.Add(new WikiSection(
                $"member-{id}",
                member.Resolved.Definition.DisplayName,
                $"{Constants.Workspace.MembersFolderName}/{id}/{Constants.Workspace.MemberRoutingCardFileName}",
                BuildRoutingCard(member, analysis)));
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
        sb.AppendLine($"2. Start at `{wiki}index.md` and `{wiki}routing-guide.md` for estate orientation and story routing.");
        sb.AppendLine($"3. Use `{wiki}architecture.md`, dependency graph, data-flows, and ownership for system context.");
        sb.AppendLine($"4. Open member **routing cards** under `{wiki}members/<id>/` (layer, brands, apps, route-when).");
        sb.AppendLine("5. Follow **web deep links** into the member repo wiki for implementation detail; use that clone’s AGENTS.md + workspace-manifest.md.");
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
        sb.AppendLine("| Id | Label | Layer | Brands | Apps | Card |");
        sb.AppendLine("|----|-------|-------|--------|------|------|");
        foreach (var m in analysis.Members)
        {
            var def = m.Resolved.Definition;
            var layer = m.Manifest?.Layer ?? def.Role ?? "—";
            var brands = m.Manifest?.Brands is { Count: > 0 } b
                ? string.Join(", ", b)
                : "—";
            var apps = m.Manifest?.Applications is { Count: > 0 } a
                ? string.Join(", ", a.Select(x => x.Name).Take(3))
                : "—";
            var card = m.Resolved.Success
                ? $"[card]({wiki}members/{def.Id}/index.md)"
                : "unresolved";
            sb.AppendLine(
                $"| `{def.Id}` | {EscapeCell(def.DisplayName)} | {EscapeCell(layer)} | {EscapeCell(brands)} | {EscapeCell(apps)} | {card} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Locating work");
        sb.AppendLine();
        sb.AppendLine("- Filter by **layer**, **brand**, **application/service**, and keywords on routing cards.");
        sb.AppendLine("- Prefer member deep wiki (via web link) for modules/APIs; do not mirror full member wikis here.");
        sb.AppendLine("- Human-owned routing authority is each member’s `docs/wiki/workspace-manifest.md`.");
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
        sb.AppendLine("- Prefer web links to member wikis over copying their architecture pages.");
        sb.AppendLine("- Never invent layer/brands/apps — only human manifest fields are authoritative.");

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
        sb.AppendLine($"1. Start by reading `{wiki}index.md` and `{wiki}routing-guide.md`");
        sb.AppendLine($"2. Review `{wiki}architecture.md`, dependency graph, data-flows, and ownership");
        sb.AppendLine($"3. Open the matching routing card under `{wiki}members/<id>/` (layer, brands, apps)");
        sb.AppendLine("4. Follow web deep links into the member repo wiki + AGENTS.md + workspace-manifest.md");
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
        sb.AppendLine("| [Routing guide](routing-guide.md) | Which repo / layer / brand / app for a story |");
        sb.AppendLine("| [Architecture](architecture.md) | System design, layers, key decisions |");
        sb.AppendLine("| [Dependency graph](dependency-graph.md) | Repo + package relationships |");
        sb.AppendLine("| [Data flows](data-flows.md) | Contracts, APIs, messaging hints |");
        sb.AppendLine("| [Ownership](ownership.md) | CODEOWNERS and team hints |");
        sb.AppendLine();
        sb.AppendLine("## Members (routing cards)");
        sb.AppendLine();
        sb.AppendLine("| Member | Layer | Brands | Applications | Wiki | Card |");
        sb.AppendLine("|--------|-------|--------|--------------|------|------|");
        foreach (var m in analysis.Members)
        {
            var def = m.Resolved.Definition;
            var cardLink = m.Resolved.Success
                ? $"[routing card](members/{def.Id}/index.md)"
                : "unresolved";
            var wikiLink = FormatMemberWikiLink(m);
            var layer = m.Manifest?.Layer ?? def.Role ?? "—";
            var brands = m.Manifest?.Brands is { Count: > 0 } b ? string.Join(", ", b) : "—";
            var apps = m.Manifest?.Applications is { Count: > 0 } a
                ? string.Join(", ", a.Select(x => x.Name).Take(4))
                : "—";
            sb.AppendLine(
                $"| **{EscapeCell(def.DisplayName)}** (`{def.Id}`) | {EscapeCell(layer)} | {EscapeCell(brands)} | {EscapeCell(apps)} | {wikiLink} | {cardLink} |");
        }

        sb.AppendLine();
        sb.AppendLine("## How agents should use this");
        sb.AppendLine();
        sb.AppendLine("1. Read [routing-guide.md](routing-guide.md) to filter by layer, brand, app, and keywords.");
        sb.AppendLine("2. Open only the member routing cards that are in scope.");
        sb.AppendLine("3. Follow **web deep links** into each member’s wiki for modules and APIs.");
        sb.AppendLine("4. Prefer member source of truth (and workspace-manifest.md) when wiki and code disagree.");
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

            sb.AppendLine($"- **Routing card:** [members/{def.Id}/index.md](members/{def.Id}/index.md)");
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

    private static string BuildRoutingGuide(WorkspaceAnalysisResult analysis, string wikiRel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Routing guide — which repos should this story touch?");
        sb.AppendLine();
        sb.AppendLine("Use this page **before** opening member clones. The workspace corpus is optimized for estate-scale routing.");
        sb.AppendLine();
        sb.AppendLine("## Agent workflow");
        sb.AppendLine();
        sb.AppendLine($"1. Start at [`index.md`](index.md) and this guide under `{wikiRel}`.");
        sb.AppendLine("2. Filter candidates by **layer**, **brand**, **application/service**, and **keywords** (table below + routing cards).");
        sb.AppendLine("3. Open the matching **routing card** under `members/<id>/index.md`.");
        sb.AppendLine("4. Follow **web deep links** into the member wiki for modules/APIs; use member `AGENTS.md` + `workspace-manifest.md` for implementation.");
        sb.AppendLine("5. Prefer human-owned manifest fields over inferred inventory when they conflict.");
        sb.AppendLine();
        sb.AppendLine("## Candidate matrix");
        sb.AppendLine();
        sb.AppendLine("| Member | Layer | Team | Brands | Applications | Keywords | Card |");
        sb.AppendLine("|--------|-------|------|--------|--------------|----------|------|");
        foreach (var m in analysis.Members.Where(x => x.Resolved.Success))
        {
            var def = m.Resolved.Definition;
            var man = m.Manifest;
            var layer = man?.Layer ?? def.Role ?? "—";
            var team = man?.Team ?? "—";
            var brands = man?.Brands is { Count: > 0 } b ? string.Join(", ", b) : "—";
            var apps = man?.Applications is { Count: > 0 } a
                ? string.Join(", ", a.Select(x => x.Name).Take(4))
                : "—";
            var keywords = man?.Keywords is { Count: > 0 } k
                ? string.Join(", ", k.Take(6))
                : "—";
            sb.AppendLine(
                $"| `{def.Id}` | {EscapeCell(layer)} | {EscapeCell(team)} | {EscapeCell(brands)} | {EscapeCell(apps)} | {EscapeCell(keywords)} | [open](members/{def.Id}/index.md) |");
        }

        sb.AppendLine();
        sb.AppendLine("## Brands vocabulary");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", Constants.WorkspaceManifest.KnownBrands)
                      + " (Blueprint = dummy/non-prod templates).");
        sb.AppendLine();
        sb.AppendLine("## Related");
        sb.AppendLine();
        sb.AppendLine("- [Architecture](architecture.md)");
        sb.AppendLine("- [Dependency graph](dependency-graph.md)");
        sb.AppendLine("- [Ownership](ownership.md)");

        return EnsureTrailingNewline(sb.ToString());
    }

    /// <summary>
    /// Per-member routing card (primary corpus page under <c>members/&lt;id&gt;/index.md</c>).
    /// Offline path emits manifest fields verbatim — never invents layer/brands/apps.
    /// </summary>
    public static string BuildRoutingCard(WorkspaceMemberAnalysis member, WorkspaceAnalysisResult analysis)
    {
        var def = member.Resolved.Definition;
        var man = member.Manifest;
        var sb = new StringBuilder();
        sb.AppendLine($"# Routing card — {def.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"**memberId:** `{def.Id}`");
        if (!string.IsNullOrWhiteSpace(def.Label) && !def.Label.Equals(def.Id, StringComparison.Ordinal))
        {
            sb.AppendLine($"**label:** {def.Label}");
        }

        sb.AppendLine();

        // Layer / team from manifest (authoritative) else role as non-authoritative
        var layer = man?.Layer;
        if (!string.IsNullOrWhiteSpace(layer))
        {
            sb.AppendLine($"## Layer");
            sb.AppendLine();
            sb.AppendLine(layer);
            sb.AppendLine();
        }
        else if (!string.IsNullOrWhiteSpace(def.Role))
        {
            sb.AppendLine("## Layer");
            sb.AppendLine();
            sb.AppendLine($"{def.Role} _(inferred from workspace member role — not from human manifest)_");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(man?.Team))
        {
            sb.AppendLine("## Team");
            sb.AppendLine();
            sb.AppendLine(man.Team);
            sb.AppendLine();
        }

        sb.AppendLine("## Applications / Services");
        sb.AppendLine();
        if (man?.Applications is { Count: > 0 })
        {
            foreach (var app in man.Applications)
            {
                sb.AppendLine(string.IsNullOrWhiteSpace(app.Description)
                    ? $"- **{app.Name}**"
                    : $"- **{app.Name}** — {app.Description}");
            }
        }
        else
        {
            sb.AppendLine("_Not set in workspace-manifest.md_");
        }

        sb.AppendLine();
        sb.AppendLine("## Brands");
        sb.AppendLine();
        if (man?.Brands is { Count: > 0 })
        {
            sb.AppendLine(string.Join(", ", man.Brands));
        }
        else
        {
            sb.AppendLine("_Not set in workspace-manifest.md_");
        }

        sb.AppendLine();
        AppendBulletSection(sb, "Responsibilities", man?.Responsibilities);
        AppendBulletSection(sb, "Route work here when", man?.RouteWhen);
        AppendBulletSection(sb, "Do not route work here when", man?.DoNotRouteWhen);
        AppendBulletSection(sb, "Related systems", man?.RelatedSystems);

        sb.AppendLine("## Keywords");
        sb.AppendLine();
        if (man?.Keywords is { Count: > 0 })
        {
            sb.AppendLine(string.Join(", ", man.Keywords));
        }
        else
        {
            sb.AppendLine("_None_");
        }

        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(man?.AdditionalContext))
        {
            sb.AppendLine("## Additional context");
            sb.AppendLine();
            sb.AppendLine(man.AdditionalContext.Trim());
            sb.AppendLine();
        }

        // Dependencies / related members
        var related = analysis.Signals.SharedPackages
            .Where(p => p.MemberIds.Contains(def.Id, StringComparer.OrdinalIgnoreCase) && p.MemberIds.Count >= 2)
            .SelectMany(p => p.MemberIds.Where(id => !id.Equals(def.Id, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var projRelated = analysis.Signals.ProjectReferences
            .Where(p => p.FromMemberId.Equals(def.Id, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(p.MatchedMemberId))
            .Select(p => p.MatchedMemberId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var depMembers = related.Concat(projRelated).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
        sb.AppendLine("## Dependencies / related members");
        sb.AppendLine();
        if (depMembers.Count > 0)
        {
            foreach (var id in depMembers)
            {
                sb.AppendLine($"- [`{id}`](../{id}/index.md)");
            }
        }
        else
        {
            sb.AppendLine("_No cross-repo dependency signals detected._");
        }

        sb.AppendLine();
        sb.AppendLine("## Web links");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(member.RepoWebUrl))
        {
            sb.AppendLine($"- **Repository:** {member.RepoWebUrl}");
        }
        else if (!string.IsNullOrWhiteSpace(def.Remote))
        {
            sb.AppendLine($"- **Remote:** `{def.Remote}`");
        }
        else if (!string.IsNullOrWhiteSpace(def.Path))
        {
            sb.AppendLine($"- **Local path:** `{def.Path}` _(no remote URL for web links)_");
        }

        if (!string.IsNullOrWhiteSpace(member.WikiWebUrl))
        {
            sb.AppendLine($"- **Member wiki (web):** {member.WikiWebUrl}");
        }
        else
        {
            var wikiRel = def.WikiPath.Replace('\\', '/').TrimEnd('/');
            sb.AppendLine($"- **Member wiki (path):** `{wikiRel}/index.md`");
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine();
        sb.AppendLine($"- **Manifest present:** {(man?.Present == true ? "yes" : "no")}");
        sb.AppendLine($"- **Wiki present:** {(member.WikiStatus?.Exists == true ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(member.Resolved.HeadSha))
        {
            var sha = member.Resolved.HeadSha;
            sb.AppendLine($"- **HEAD:** `{sha[..Math.Min(12, sha.Length)]}`");
        }

        if (member.WikiStatus is { IsStale: true })
        {
            sb.AppendLine("- **Freshness:** git-stale (source changed since last member wiki baseline)");
        }
        else if (member.WikiStatus is not null)
        {
            sb.AppendLine($"- **Freshness:** {member.WikiStatus.Summary}");
        }

        if (member.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var w in member.Warnings.Take(15))
            {
                sb.AppendLine($"- {w}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Related system pages");
        sb.AppendLine();
        sb.AppendLine("- [Index](../../index.md)");
        sb.AppendLine("- [Routing guide](../../routing-guide.md)");
        sb.AppendLine("- [Architecture](../../architecture.md)");
        sb.AppendLine("- [Dependency graph](../../dependency-graph.md)");

        return EnsureTrailingNewline(sb.ToString());
    }

    private static void AppendBulletSection(StringBuilder sb, string heading, IReadOnlyList<string>? items)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        if (items is { Count: > 0 })
        {
            foreach (var item in items)
            {
                sb.AppendLine($"- {item}");
            }
        }
        else
        {
            sb.AppendLine("_Not set_");
        }

        sb.AppendLine();
    }

    private static string FormatMemberWikiLink(WorkspaceMemberAnalysis member)
    {
        if (!string.IsNullOrWhiteSpace(member.WikiWebUrl))
        {
            return $"[wiki]({member.WikiWebUrl})";
        }

        var def = member.Resolved.Definition;
        var wikiRel = def.WikiPath.Replace('\\', '/').TrimEnd('/');
        if (member.WikiStatus is not { Exists: true })
        {
            return $"_missing_ (`{wikiRel}/`)";
        }

        return $"`{wikiRel}/index.md`";
    }

    private static string NormalizeRel(string path) =>
        path.Replace('\\', '/').TrimEnd('/') + "/";

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + Environment.NewLine;
}
