using System.Text;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Multi-step wiki generation pipeline:
/// architecture → module plan → module pages → cross-cutting → index/supporting pages.
/// </summary>
public sealed class WikiGenerationOrchestrator(
    IArchitectureGenerator architectureGenerator,
    ILlmCompletionService llm,
    IPromptManager promptManager,
    ILogger<WikiGenerationOrchestrator> logger) : IWikiGenerationOrchestrator
{
    private const int MaxModulesForLlm = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <inheritdoc />
    public async Task<WikiBundle> GenerateAsync(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        IncrementalScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(request);

        scope ??= request.Scope ?? IncrementalScope.Full();
        var steps = new List<string>();
        var warnings = new List<string>(analysis.Warnings);
        var usages = new List<TokenUsage?>();
        var anyOffline = false;

        logger.LogInformation(
            "Orchestrator starting for {Repo} (correlationId={CorrelationId}, full={Full})",
            analysis.RepoName,
            request.CorrelationId,
            scope.IsFull);

        // Step 1: Architecture (selective LLM)
        ArchitectureDocument architecture;
        if (scope.IsFull || scope.Architecture)
        {
            architecture = await architectureGenerator
                .GenerateAsync(
                    analysis,
                    request.Config,
                    request.ModelOverride,
                    request.ProviderOverride,
                    cancellationToken)
                .ConfigureAwait(false);
            steps.Add("architecture");
        }
        else
        {
            architecture = OfflineArchitectureGenerator.Generate(analysis);
            steps.Add("architecture:skipped-llm");
            warnings.Add("Architecture left on offline snapshot (not in incremental change scope).");
        }

        usages.Add(architecture.TokenUsage);
        anyOffline |= architecture.UsedOfflineFallback;

        // Step 2: Module plan (always — needed for navigation coherence)
        var modulePlan = await PlanModulesAsync(analysis, request, scope, cancellationToken).ConfigureAwait(false);
        steps.Add("module-plan");
        usages.Add(modulePlan.TokenUsage);
        anyOffline |= modulePlan.UsedOfflineFallback;

        // Step 3: Module details (selective LLM)
        var modules = new List<ModuleDocument>();
        foreach (var descriptor in modulePlan.Modules.Take(MaxModulesForLlm))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regenerate =
                scope.IsFull
                || scope.AllModules
                || scope.ModuleIds.Contains(descriptor.Id);

            ModuleDocument module;
            if (regenerate)
            {
                module = await GenerateModuleAsync(descriptor, analysis, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                module = OfflineModulePlanner.BuildModuleDocument(descriptor, analysis);
                module.Gotchas.Insert(0, "Module not in incremental change scope; refreshed from inventory only.");
            }

            modules.Add(module);
            usages.Add(module.TokenUsage);
            anyOffline |= module.UsedOfflineFallback;
        }

        steps.Add($"modules:{modules.Count}");

        // Step 4: Cross-cutting concerns (selective LLM enrichment)
        var crossCutting = await GenerateCrossCuttingAsync(analysis, request, scope, cancellationToken)
            .ConfigureAwait(false);
        steps.Add($"cross-cutting:{crossCutting.Count}");
        foreach (var item in crossCutting)
        {
            usages.Add(item.TokenUsage);
            anyOffline |= item.UsedOfflineFallback;
        }

        // Step 5: Cross-link / consistency pass (deterministic in v1; LLM optional later)
        ValidateAndNormalizeLinks(modules, crossCutting, warnings);
        steps.Add("cross-link-validation");

        // Step 6: Assemble sections including index
        var generatedAt = DateTimeOffset.UtcNow;
        var sections = BuildSections(
            analysis,
            architecture,
            modules,
            crossCutting,
            request,
            generatedAt);
        steps.Add("index-and-support-pages");

        if (anyOffline)
        {
            warnings.Add(
                "One or more pipeline steps used offline generation. Configure LLM credentials for full Semantic Kernel output.");
        }

        var total = TokenUsageMath.Sum(usages.ToArray());
        logger.LogInformation(
            "Orchestrator complete for {Repo}: steps={Steps}, modules={Modules}, tokens={Tokens}",
            analysis.RepoName,
            string.Join(" → ", steps),
            modules.Count,
            total.TotalTokens);

        return new WikiBundle
        {
            Architecture = architecture,
            ModulePlan = modulePlan,
            Modules = modules,
            CrossCutting = crossCutting,
            Sections = sections,
            UsedOfflineFallback = anyOffline,
            TokenUsage = total,
            Warnings = warnings,
            StepsCompleted = steps
        };
    }

    private async Task<ModulePlan> PlanModulesAsync(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        IncrementalScope scope,
        CancellationToken cancellationToken)
    {
        // Module inventory structure is cheap offline; only spend LLM tokens on full runs.
        if (!scope.IsFull || !llm.CanUseLiveLlm(request.Config, request.ProviderOverride))
        {
            return OfflineModulePlanner.Plan(analysis);
        }

        try
        {
            var prompts = ResolvePrompts(analysis.RepoPath);
            var system = prompts.GetPrompt("SystemPrompt");
            var user = prompts.Render("ModulePlanPrompt", new Dictionary<string, string>
            {
                ["RepoName"] = analysis.RepoName,
                ["RepoSummary"] = SummaryForLlm(analysis, request.Config)
            });

            var completion = await llm.CompleteAsync(
                    request.Config,
                    system,
                    user,
                    request.ModelOverride,
                    request.ProviderOverride,
                    options: LlmRequestOptions.WikiGeneration,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var plan = ParseModulePlan(completion.Content);
            plan.UsedOfflineFallback = false;
            plan.TokenUsage = completion.TokenUsage;

            // Ensure related files exist when possible; fill from inventory if empty.
            EnrichPlanFromInventory(plan, analysis);
            return plan;
        }
        catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Module planning via LLM failed; using offline planner");
            return OfflineModulePlanner.Plan(analysis);
        }
    }

    private async Task<ModuleDocument> GenerateModuleAsync(
        ModuleDescriptor descriptor,
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!llm.CanUseLiveLlm(request.Config, request.ProviderOverride))
        {
            return OfflineModulePlanner.BuildModuleDocument(descriptor, analysis);
        }

        try
        {
            var prompts = ResolvePrompts(analysis.RepoPath);
            var related = string.Join("\n", descriptor.RelatedFiles.Select(f => $"- {f}"));
            var system = prompts.GetPrompt("SystemPrompt");
            var user = prompts.Render("ModuleAnalysisPrompt", new Dictionary<string, string>
            {
                ["ModuleName"] = descriptor.Name,
                ["ModuleId"] = descriptor.Id,
                ["ModuleSummary"] = descriptor.Summary,
                ["RelatedFiles"] = related,
                ["RepoName"] = analysis.RepoName
            });

            var completion = await llm.CompleteAsync(
                    request.Config,
                    system,
                    user,
                    request.ModelOverride,
                    request.ProviderOverride,
                    options: LlmRequestOptions.WikiGeneration,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var document = ParseModuleDocument(completion.Content, descriptor);
            document.UsedOfflineFallback = false;
            document.TokenUsage = completion.TokenUsage;
            if (document.RelatedFiles.Count == 0)
            {
                document.RelatedFiles = descriptor.RelatedFiles.Take(25).ToList();
            }

            return document;
        }
        catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Module generation failed for {Module}; using offline content", descriptor.Id);
            var offline = OfflineModulePlanner.BuildModuleDocument(descriptor, analysis);
            offline.Gotchas.Insert(0, $"LLM module generation failed: {ex.Message}");
            return offline;
        }
    }

    private async Task<IReadOnlyList<CrossCuttingDocument>> GenerateCrossCuttingAsync(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        IncrementalScope scope,
        CancellationToken cancellationToken)
    {
        // Offline heuristics are solid; use LLM only to enrich when available and in scope.
        var offline = OfflineModulePlanner.BuildCrossCutting(analysis).ToList();
        var allowLlm = (scope.IsFull || scope.AllCrossCutting || scope.CrossCuttingIds.Count > 0)
                       && llm.CanUseLiveLlm(request.Config, request.ProviderOverride);

        if (!allowLlm)
        {
            return offline;
        }

        try
        {
            var prompts = ResolvePrompts(analysis.RepoPath);
            var system = prompts.GetPrompt("SystemPrompt");
            var user = prompts.Render("CrossCuttingPrompt", new Dictionary<string, string>
            {
                ["RepoName"] = analysis.RepoName,
                ["RepoSummary"] = SummaryForLlm(analysis, request.Config)
            });

            var completion = await llm.CompleteAsync(
                    request.Config,
                    system,
                    user,
                    request.ModelOverride,
                    request.ProviderOverride,
                    options: LlmRequestOptions.WikiGeneration,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var parsed = ParseCrossCuttingList(completion.Content);
            if (parsed.Count == 0)
            {
                return offline;
            }

            // On selective runs, keep LLM pages only for affected ids; others stay offline.
            if (!scope.IsFull && !scope.AllCrossCutting)
            {
                var byId = parsed.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var item in offline)
                {
                    if (scope.CrossCuttingIds.Contains(item.Id) && byId.TryGetValue(item.Id, out var live))
                    {
                        live.UsedOfflineFallback = false;
                        live.TokenUsage = completion.TokenUsage;
                        var index = offline.FindIndex(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
                        if (index >= 0)
                        {
                            offline[index] = live;
                        }
                    }
                }

                return offline;
            }

            foreach (var item in parsed)
            {
                item.UsedOfflineFallback = false;
                item.TokenUsage = completion.TokenUsage;
            }

            return parsed;
        }
        catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Cross-cutting LLM generation failed; using offline content");
            return offline;
        }
    }

    private static string SummaryForLlm(RepoAnalysisResult analysis, AgentWikiConfig config) =>
        RepoSummaryBuilder.BuildForLlm(
            analysis.RepoName,
            analysis.RepoPath,
            analysis.Stats,
            analysis.Files,
            maxChars: config.MaxLlmSummaryChars > 0 ? config.MaxLlmSummaryChars : 16_000);

    private IPromptManager ResolvePrompts(string repoPath)
    {
        var overrideDir = Path.Combine(repoPath, ".agentwiki", "prompts");
        if (Directory.Exists(overrideDir))
        {
            return PromptManager.ForRepository(
                repoPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptManager>.Instance);
        }

        return promptManager;
    }

    private static void EnrichPlanFromInventory(ModulePlan plan, RepoAnalysisResult analysis)
    {
        foreach (var module in plan.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id))
            {
                module.Id = OfflineModulePlanner.Plan(analysis).Modules
                    .FirstOrDefault()?.Id ?? "module";
            }

            module.Id = module.Id.Trim().ToLowerInvariant().Replace(' ', '-');

            if (module.RelatedFiles.Count > 0)
            {
                continue;
            }

            foreach (var root in module.RootPaths)
            {
                var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
                var matches = analysis.Files
                    .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                || prefix == "./" || prefix == "/")
                    .Select(f => f.RelativePath)
                    .Take(25);
                module.RelatedFiles.AddRange(matches);
            }

            module.RelatedFiles = module.RelatedFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(25)
                .ToList();
        }
    }

    private static void ValidateAndNormalizeLinks(
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<CrossCuttingDocument> crossCutting,
        List<string> warnings)
    {
        var ids = modules.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count != modules.Count)
        {
            warnings.Add("Duplicate module ids detected; some module pages may overwrite each other.");
        }

        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.Title))
            {
                module.Title = module.Id;
            }
        }

        foreach (var item in crossCutting)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                item.Title = item.Id;
            }
        }
    }

    private static IReadOnlyList<WikiSection> BuildSections(
        RepoAnalysisResult analysis,
        ArchitectureDocument architecture,
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<CrossCuttingDocument> crossCutting,
        WikiGenerationRequest request,
        DateTimeOffset generatedAt)
    {
        var sections = new List<WikiSection>
        {
            new(
                "index",
                "Wiki Index",
                "index.md",
                ModuleMarkdownRenderer.RenderIndex(
                    analysis.RepoName,
                    architecture,
                    modules,
                    crossCutting,
                    analysis.Stats,
                    generatedAt,
                    request.CorrelationId,
                    architecture.UsedOfflineFallback)),
            new(
                "architecture",
                architecture.Title,
                "architecture.md",
                ArchitectureMarkdownRenderer.Render(architecture, analysis.RepoName)),
            new(
                "key-components",
                "Key Components",
                "key-components.md",
                BuildKeyComponents(analysis, architecture, modules)),
            new(
                "data-flows",
                "Data Flows",
                "data-flows.md",
                BuildDataFlows(architecture, modules)),
            new(
                "inventory",
                "Repository Inventory",
                "inventory.md",
                "# Repository Inventory\n\n> Machine-generated from RepoAnalyzer.\n\n```text\n"
                + analysis.Summary
                + "\n```\n"),
            new(
                "glossary",
                "Glossary",
                "glossary.md",
                BuildGlossary(analysis, modules)),
            new(
                "getting-started",
                "Getting Started for Agents",
                "getting-started.md",
                BuildGettingStarted(request, modules.Count, crossCutting.Count))
        };

        foreach (var module in modules)
        {
            sections.Add(new WikiSection(
                module.Id,
                module.Title,
                module.RelativePath,
                ModuleMarkdownRenderer.RenderModule(module),
                module.RelatedFiles));
        }

        foreach (var item in crossCutting)
        {
            sections.Add(new WikiSection(
                item.Id,
                item.Title,
                item.RelativePath,
                ModuleMarkdownRenderer.RenderCrossCutting(item),
                item.KeyFiles));
        }

        return sections;
    }

    private static string BuildKeyComponents(
        RepoAnalysisResult analysis,
        ArchitectureDocument architecture,
        IReadOnlyList<ModuleDocument> modules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Key Components");
        sb.AppendLine();
        sb.AppendLine("> Combines architecture components, module map, and inventory.");
        sb.AppendLine();

        if (architecture.KeyComponents.Count > 0)
        {
            sb.AppendLine("## Architecture components");
            sb.AppendLine();
            foreach (var component in architecture.KeyComponents)
            {
                var path = string.IsNullOrWhiteSpace(component.Path) ? "" : $" (`{component.Path}`)";
                sb.AppendLine($"- **{component.Name}**{path}: {component.Purpose}");
            }

            sb.AppendLine();
        }

        if (modules.Count > 0)
        {
            sb.AppendLine("## Modules");
            sb.AppendLine();
            foreach (var module in modules)
            {
                sb.AppendLine($"- [{module.Title}]({module.RelativePath}) — {Truncate(module.Purpose, 120)}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Languages");
        sb.AppendLine();
        if (analysis.Stats.DetectedLanguages.Count == 0)
        {
            sb.AppendLine("_No languages detected._");
        }
        else
        {
            sb.AppendLine("| Language | Files |");
            sb.AppendLine("|----------|------:|");
            foreach (var lang in analysis.Stats.DetectedLanguages)
            {
                analysis.Stats.FilesByLanguage.TryGetValue(lang, out var count);
                sb.AppendLine($"| {lang} | {count} |");
            }
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildDataFlows(ArchitectureDocument architecture, IReadOnlyList<ModuleDocument> modules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Data Flows");
        sb.AppendLine();
        sb.AppendLine("> Important flows for agents implementing features or debugging.");
        sb.AppendLine();

        if (architecture.DataFlows.Count > 0)
        {
            sb.AppendLine("## Architecture flows");
            sb.AppendLine();
            for (var i = 0; i < architecture.DataFlows.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {architecture.DataFlows[i]}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Module-oriented flow (recommended)");
        sb.AppendLine();
        sb.AppendLine("1. Read `architecture.md` for system boundaries.");
        sb.AppendLine("2. Open the relevant module page under `modules/`.");
        sb.AppendLine("3. Inspect entry points and related files listed on that page.");
        sb.AppendLine("4. Check `cross-cutting/` for logging, config, and error-handling conventions.");
        sb.AppendLine();

        if (modules.Count > 0)
        {
            sb.AppendLine("## Module entry points");
            sb.AppendLine();
            foreach (var module in modules)
            {
                sb.AppendLine($"### {module.Title}");
                sb.AppendLine();
                if (module.EntryPoints.Count == 0)
                {
                    sb.AppendLine("_No explicit entry points listed._");
                }
                else
                {
                    foreach (var entry in module.EntryPoints)
                    {
                        sb.AppendLine($"- `{entry}`");
                    }
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string BuildGlossary(RepoAnalysisResult analysis, IReadOnlyList<ModuleDocument> modules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Glossary");
        sb.AppendLine();
        sb.AppendLine("| Term | Meaning |");
        sb.AppendLine("|------|---------|");
        sb.AppendLine("| AgentWiki | Tool that generates agent-optimized repository documentation |");
        sb.AppendLine("| Inventory | Filtered file list produced by RepoAnalyzer |");
        sb.AppendLine("| Module | Bounded project/folder area documented under `modules/` |");
        sb.AppendLine("| Cross-cutting | Concern spanning modules (logging, config, errors) |");
        sb.AppendLine("| Offline generation | Inventory heuristics used when LLM credentials are unavailable |");
        foreach (var module in modules.Take(12))
        {
            sb.AppendLine($"| {module.Title} | Module documented at `{module.RelativePath}` |");
        }

        foreach (var lang in analysis.Stats.DetectedLanguages.Take(8))
        {
            sb.AppendLine($"| {lang} | Detected language in repository inventory |");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildGettingStarted(WikiGenerationRequest request, int moduleCount, int crossCuttingCount)
    {
        var output = request.Config.OutputPath.Replace('\\', '/').TrimEnd('/') + "/";
        return $$"""
            # Getting Started for Coding Agents

            This repository maintains an **agent-optimized wiki** at `{{output}}`.

            ## Recommended workflow

            1. Read `{{output}}index.md` for navigation.
            2. Read `{{output}}architecture.md` before structural changes.
            3. Open the relevant page under `{{output}}modules/` ({{moduleCount}} modules documented).
            4. Check `{{output}}cross-cutting/` ({{crossCuttingCount}} topics) for shared conventions.
            5. Use `{{output}}inventory.md` when you need exact paths.

            ## Important

            - Content may be AI-generated; verify against source of truth.
            - Do not commit secrets into wiki pages.
            - Re-run `agent-wiki generate` or `update` after meaningful structural changes.
            """;
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Replace('\n', ' ').Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }

    public static ModulePlan ParseModulePlan(string raw)
    {
        var json = ExtractJsonPayload(raw);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("modules", out _))
        {
            var plan = JsonSerializer.Deserialize<ModulePlan>(json, JsonOptions)
                       ?? new ModulePlan();
            return plan;
        }

        // Accept bare array.
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var modules = JsonSerializer.Deserialize<List<ModuleDescriptor>>(json, JsonOptions) ?? [];
            return new ModulePlan { Modules = modules };
        }

        throw new InvalidOperationException("Module plan JSON did not contain a modules array.");
    }

    /// <summary>
    /// Extracts a JSON object or array payload from model output (with optional fences).
    /// </summary>
    public static string ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("LLM returned empty content.");
        }

        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var objStart = text.IndexOf('{');
        var arrStart = text.IndexOf('[');
        if (objStart < 0 && arrStart < 0)
        {
            throw new InvalidOperationException("LLM response did not contain JSON.");
        }

        if (arrStart >= 0 && (objStart < 0 || arrStart < objStart))
        {
            var end = text.LastIndexOf(']');
            if (end <= arrStart)
            {
                throw new InvalidOperationException("LLM response contained an incomplete JSON array.");
            }

            return text[arrStart..(end + 1)];
        }

        return ArchitectureGenerator.ExtractJsonObject(text);
    }

    public static ModuleDocument ParseModuleDocument(string raw, ModuleDescriptor descriptor)
    {
        var json = ArchitectureGenerator.ExtractJsonObject(raw);
        var document = JsonSerializer.Deserialize<ModuleDocument>(json, JsonOptions)
                       ?? new ModuleDocument();
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = descriptor.Id;
        }

        if (string.IsNullOrWhiteSpace(document.Title))
        {
            document.Title = descriptor.Name;
        }

        return document;
    }

    public static List<CrossCuttingDocument> ParseCrossCuttingList(string raw)
    {
        var json = ExtractJsonPayload(raw);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<CrossCuttingDocument>>(items.GetRawText(), JsonOptions) ?? [];
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<CrossCuttingDocument>>(json, JsonOptions) ?? [];
        }

        return [];
    }
}
