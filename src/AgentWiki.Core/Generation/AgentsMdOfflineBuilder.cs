using System.Text;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Builds a complete offline <c>AGENTS.md</c> from inventory, optional wiki excerpts,
/// and migrated instruction sources. Always includes the self-updating section.
/// </summary>
public static class AgentsMdOfflineBuilder
{
    public static string Build(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        string wikiRelativePath,
        IReadOnlyList<InstructionSource> instructionSources,
        IReadOnlyDictionary<string, string>? wikiExcerpts = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        var wiki = NormalizeWiki(wikiRelativePath);
        var languages = analysis.Stats.DetectedLanguages.Count > 0
            ? string.Join(", ", analysis.Stats.DetectedLanguages.Take(8))
            : "unknown";
        var projects = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        var topFolders = analysis.Stats.TopFolders
            .Take(12)
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# AGENTS.md — {analysis.RepoName}");
        sb.AppendLine();
        sb.AppendLine("## Start here");
        sb.AppendLine();
        sb.AppendLine("1. Read this file end-to-end for agent workflow and guardrails.");
        if (FileLikelyExists(analysis, Constants.Paths.DefaultReadmePath))
        {
            sb.AppendLine($"2. Read `{Constants.Paths.DefaultReadmePath}` for user-facing setup and commands.");
        }
        else
        {
            sb.AppendLine("2. Check for a README (or generate one) for user-facing setup and commands.");
        }

        if (wikiExcerpts is not null && wikiExcerpts.Count > 0)
        {
            sb.AppendLine($"3. Read `{wiki}index.md` and `{wiki}architecture.md` for the agent-optimized map of this repo.");
            sb.AppendLine($"4. Drill into `{wiki}modules/` and `{wiki}cross-cutting/` as needed.");
        }
        else
        {
            sb.AppendLine($"3. If `{wiki}` exists, start with `{wiki}index.md` and `{wiki}architecture.md`.");
            sb.AppendLine("4. Prefer source of truth in code when wiki and code disagree.");
        }

        if (FileLikelyExists(analysis, "docs/HANDOFF.md") || FileLikelyExists(analysis, "HANDOFF.md"))
        {
            sb.AppendLine("5. Review `docs/HANDOFF.md` (or root HANDOFF) for session continuity when present.");
        }

        sb.AppendLine();
        sb.AppendLine("## Project snapshot");
        sb.AppendLine();
        sb.AppendLine(
            $"**{analysis.RepoName}** is a software repository analyzed by AgentWiki " +
            $"({analysis.Stats.TotalFiles} files after ignores; ~{analysis.Stats.SelectedFiles} selected for LLM/summary).");
        sb.AppendLine();
        sb.AppendLine($"- **Languages / stacks (detected):** {languages}");
        sb.AppendLine($"- **Wiki output:** `{wiki}`");
        sb.AppendLine($"- **Config:** `{Constants.Paths.ConfigDirectoryName}/{Constants.Paths.ConfigFileName}` (secrets in `.env`, not committed)");
        sb.AppendLine($"- **Default LLM model (config):** `{config.DefaultModel}` · provider `{config.Provider}`");

        if (projects.Count > 0)
        {
            sb.AppendLine("- **Notable project / solution files:**");
            foreach (var p in projects)
            {
                sb.AppendLine($"  - `{p}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### Key commands (inferred — verify in repo)");
        sb.AppendLine();
        AppendInferredCommands(sb, analysis);

        sb.AppendLine();
        sb.AppendLine("## Architecture & layout");
        sb.AppendLine();
        if (topFolders.Count > 0)
        {
            sb.AppendLine("Top folders by file count:");
            foreach (var folder in topFolders)
            {
                sb.AppendLine($"- `{folder}/`");
            }
        }
        else
        {
            sb.AppendLine("See repository root layout and solution/project files for structure.");
        }

        if (wikiExcerpts is not null)
        {
            if (wikiExcerpts.TryGetValue("architecture", out var arch) && !string.IsNullOrWhiteSpace(arch))
            {
                sb.AppendLine();
                sb.AppendLine("### From wiki architecture (excerpt)");
                sb.AppendLine();
                sb.AppendLine(TrimExcerpt(arch, 1200));
            }

            if (wikiExcerpts.TryGetValue("index", out var index) && !string.IsNullOrWhiteSpace(index))
            {
                sb.AppendLine();
                sb.AppendLine("### From wiki index (excerpt)");
                sb.AppendLine();
                sb.AppendLine(TrimExcerpt(index, 800));
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(
                $"When the wiki is generated, treat `{wiki}` as the preferred orientation map, " +
                "then verify against source before editing.");
        }

        if (analysis.StaticAnalysis is { Projects.Count: > 0 } staticAnalysis)
        {
            sb.AppendLine();
            sb.AppendLine("### Static analysis highlights");
            sb.AppendLine();
            foreach (var project in staticAnalysis.Projects.Take(10))
            {
                sb.AppendLine(
                    $"- `{project.RelativePath}` — {project.Kind} · {project.PublicTypeCount} public type(s)"
                    + (project.EndpointCount > 0 ? $" · {project.EndpointCount} endpoint(s)" : ""));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Coding conventions");
        sb.AppendLine();
        AppendConventions(sb, analysis, instructionSources);

        sb.AppendLine();
        sb.AppendLine(AgentBootstrapperBlock(wiki).TrimEnd());
        sb.AppendLine();

        AppendSelfUpdateSection(sb);

        sb.AppendLine();
        sb.AppendLine("## Do not / Guardrails");
        sb.AppendLine();
        sb.AppendLine("- Do not commit secrets, API keys, or `.env` files with credentials.");
        sb.AppendLine("- Do not force-push shared branches or rewrite published history without explicit human request.");
        sb.AppendLine("- Do not log API keys or full LLM prompt/response bodies by default.");
        sb.AppendLine("- Prefer reversible local changes; confirm before destructive shared operations.");
        if (instructionSources.Count > 0)
        {
            sb.AppendLine("- Honor project-specific rules in the migrated instructions section below.");
        }

        sb.AppendLine();
        sb.AppendLine("## Common tasks");
        sb.AppendLine();
        sb.AppendLine("| Task | Where to look / what to run |");
        sb.AppendLine("|------|-----------------------------|");
        sb.AppendLine($"| Understand the system | `{wiki}index.md`, `{wiki}architecture.md`, this file |");
        sb.AppendLine("| Change a module | Matching project under inventory; wiki `modules/` when present |");
        sb.AppendLine("| Fix build/tests | Solution/project files; test projects; CI workflows under `.github/` |");
        sb.AppendLine($"| Refresh agent docs | `agent-wiki generate` / `update` / `agents` |");
        sb.AppendLine($"| Config / secrets | `{Constants.Paths.ConfigDirectoryName}/`, `.env` (local only) |");

        if (instructionSources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Project-specific instructions (migrated)");
            sb.AppendLine();
            sb.AppendLine(
                "The following content was migrated from discovered instruction files " +
                "(e.g. GitHub Copilot instructions). Prefer these project rules when they conflict with generic guidance.");
            sb.AppendLine();
            foreach (var source in instructionSources)
            {
                sb.AppendLine($"### From `{source.RelativePath}`");
                sb.AppendLine();
                sb.AppendLine(source.Content.Trim());
                sb.AppendLine();
            }
        }

        return EnsureTrailingNewline(sb.ToString());
    }

    /// <summary>Mandatory self-updating instructions (offline + LLM paths must include this).</summary>
    public static void AppendSelfUpdateSection(StringBuilder sb)
    {
        sb.AppendLine(Constants.AgentsMd.SelfUpdateSectionHeading);
        sb.AppendLine();
        sb.AppendLine(
            "When you make changes that affect **how agents should work on this repository**, " +
            "update **AGENTS.md** in the **same change**. When you change user-facing setup, quick start, " +
            "or primary commands, update **README.md** in the same change.");
        sb.AppendLine();
        sb.AppendLine("Examples of changes that require an AGENTS.md update:");
        sb.AppendLine();
        sb.AppendLine("- New or removed major modules / projects / entry points");
        sb.AppendLine("- Changed build, test, run, or pack commands");
        sb.AppendLine("- New coding conventions, guardrails, or “do not” rules");
        sb.AppendLine("- New required tools, env vars, or secrets handling");
        sb.AppendLine("- Changes to the preferred workflow for agents");
        sb.AppendLine();
        sb.AppendLine("Guidance:");
        sb.AppendLine();
        sb.AppendLine("- Prefer small, precise edits to existing sections rather than rewriting the whole file.");
        sb.AppendLine(
            "- If AgentWiki is in use, re-run `agent-wiki generate` / `update` (or `agent-wiki agents`) " +
            "when structural changes make the wiki or AGENTS.md stale.");
        sb.AppendLine(
            "- Keep the AgentWiki marker block (`<!-- BEGIN AGENTWIKI -->` … `<!-- END AGENTWIKI -->`) intact " +
            "so tooling can refresh wiki pointers.");
    }

    public static string BuildSelfUpdateSectionMarkdown()
    {
        var sb = new StringBuilder();
        AppendSelfUpdateSection(sb);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string AgentBootstrapperBlock(string wikiRelativePath)
    {
        var wiki = NormalizeWiki(wikiRelativePath);
        var sb = new StringBuilder();
        sb.AppendLine(Constants.AgentsMd.MarkerBegin);
        sb.AppendLine("## AgentWiki Documentation");
        sb.AppendLine($"This repository maintains an **agent-optimized wiki** at `{wiki}`.");
        sb.AppendLine();
        sb.AppendLine("**For any task involving this codebase:**");
        sb.AppendLine($"1. Start by reading `{wiki}index.md` and `{wiki}architecture.md`");
        sb.AppendLine($"2. Drill into specific modules under `{wiki}modules/`");
        sb.AppendLine($"3. Review cross-cutting concerns under `{wiki}cross-cutting/` when relevant");
        sb.AppendLine(
            "4. The wiki is kept up-to-date via `agent-wiki generate` / `update` (and CI when configured). Do not ignore it.");
        sb.AppendLine("5. Prefer wiki paths as a starting map, but always verify against source before making changes.");
        sb.AppendLine(Constants.AgentsMd.MarkerEnd);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendInferredCommands(StringBuilder sb, RepoAnalysisResult analysis)
    {
        var hasDotnet = analysis.Files.Any(f =>
            f.RelativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasNode = analysis.Files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "package.json", StringComparison.OrdinalIgnoreCase));
        var hasPython = analysis.Files.Any(f =>
            string.Equals(Path.GetFileName(f.RelativePath), "pyproject.toml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(f.RelativePath), "requirements.txt", StringComparison.OrdinalIgnoreCase));

        if (hasDotnet)
        {
            sb.AppendLine("```bash");
            sb.AppendLine("dotnet build");
            sb.AppendLine("dotnet test");
            sb.AppendLine("```");
        }

        if (hasNode)
        {
            sb.AppendLine("```bash");
            sb.AppendLine("npm install");
            sb.AppendLine("npm test   # or npm run build — verify package.json scripts");
            sb.AppendLine("```");
        }

        if (hasPython)
        {
            sb.AppendLine("```bash");
            sb.AppendLine("# Python — verify local docs for exact tooling");
            sb.AppendLine("pytest");
            sb.AppendLine("```");
        }

        if (!hasDotnet && !hasNode && !hasPython)
        {
            sb.AppendLine("- Inspect README / CI workflows for the authoritative build and test commands.");
        }
    }

    private static void AppendConventions(
        StringBuilder sb,
        RepoAnalysisResult analysis,
        IReadOnlyList<InstructionSource> instructionSources)
    {
        var hasCs = analysis.Files.Any(f =>
            string.Equals(f.Extension, ".cs", StringComparison.OrdinalIgnoreCase));
        if (hasCs)
        {
            sb.AppendLine("- Prefer idiomatic modern C# when the repo already uses it (nullable, file-scoped namespaces, primary constructors when present).");
            sb.AppendLine("- Match existing test frameworks and project layout under `tests/` or `*Tests`.");
        }

        sb.AppendLine("- Follow existing formatting and naming in neighboring files.");
        sb.AppendLine("- Keep changes focused; avoid drive-by refactors.");

        if (instructionSources.Count > 0)
        {
            sb.AppendLine(
                $"- Additional project rules were migrated from {instructionSources.Count} instruction file(s); see **Project-specific instructions**.");
        }
    }

    private static bool FileLikelyExists(RepoAnalysisResult analysis, string relativePath) =>
        analysis.Files.Any(f =>
            string.Equals(f.RelativePath.Replace('\\', '/'), relativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
        || File.Exists(Path.Combine(analysis.RepoPath, relativePath));

    private static string NormalizeWiki(string wikiRelativePath) =>
        wikiRelativePath.Replace('\\', '/').TrimEnd('/') + "/";

    private static string TrimExcerpt(string text, int maxChars)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        return trimmed[..(maxChars - 1)].TrimEnd() + "…";
    }

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + Environment.NewLine;
}
