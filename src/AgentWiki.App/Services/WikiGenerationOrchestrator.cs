using System.Text;
using System.Text.Json;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Multi-step wiki generation pipeline:
/// architecture → module plan → module pages → cross-cutting → index/supporting pages.
/// </summary>
public sealed class WikiGenerationOrchestrator(
    IArchitectureGenerator architectureGenerator,
    ILlmCompletionService llm,
    IPromptManager promptManager,
    IWikiPostProcessor postProcessor,
    ILogger<WikiGenerationOrchestrator> logger) : IWikiGenerationOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = LlmJson.CreateOptions();

    private static int ResolveMaxModules(AgentWikiConfig config) =>
        config.MaxModules > 0 ? config.MaxModules : Constants.Config.MaxModules;

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
        var postCorrections = new List<WikiPostProcessCorrection>();

        logger.LogInformation(
            "Orchestrator starting for {Repo} (correlationId={CorrelationId}, full={Full})",
            analysis.RepoName,
            request.CorrelationId,
            scope.IsFull);

        Report(request, "Generating architecture overview…");

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
        Report(request, "Planning modules…");
        var modulePlan = await PlanModulesAsync(analysis, request, scope, cancellationToken).ConfigureAwait(false);
        steps.Add("module-plan");
        usages.Add(modulePlan.TokenUsage);
        anyOffline |= modulePlan.UsedOfflineFallback;

        // Step 3: Module details (selective LLM)
        var modules = new List<ModuleDocument>();
        var maxModules = ResolveMaxModules(request.Config);
        var moduleDescriptors = modulePlan.Modules.Take(maxModules).ToList();
        var moduleIndex = 0;
        foreach (var descriptor in moduleDescriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            moduleIndex++;
            Report(request, $"Documenting module {moduleIndex}/{moduleDescriptors.Count}: {descriptor.Name}…");

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
                module = OfflineModulePlanner.BuildModuleDocument(descriptor, analysis, request.Config);
                module.Gotchas.Insert(0, "Module not in incremental change scope; refreshed from inventory only.");
            }

            modules.Add(module);
            usages.Add(module.TokenUsage);
            anyOffline |= module.UsedOfflineFallback;
        }

        steps.Add($"modules:{modules.Count}");

        // Step 4: Cross-cutting concerns (selective LLM enrichment)
        Report(request, "Documenting cross-cutting concerns…");
        var crossCutting = (await GenerateCrossCuttingAsync(analysis, request, scope, cancellationToken)
            .ConfigureAwait(false)).ToList();
        // Prefer existing cross-cutting/*.md ids unless this is a full clear/regenerate.
        if (request.Incremental || !scope.IsFull)
        {
            ModuleIdStabilizer.StabilizeCrossCuttingIds(crossCutting, request.OutputPath, warnings);
        }
        else
        {
            foreach (var item in crossCutting)
            {
                item.Id = ModuleIdStabilizer.Slug(
                    string.IsNullOrWhiteSpace(item.Id) ? item.Title : item.Id);
            }
        }

        steps.Add($"cross-cutting:{crossCutting.Count}");
        foreach (var item in crossCutting)
        {
            usages.Add(item.TokenUsage);
            anyOffline |= item.UsedOfflineFallback;
        }

        // Step 4b: API endpoint catalog (static analysis → modules + api-endpoints.md)
        IReadOnlyList<EndpointInfo> endpointCatalog = [];
        var endpointsLlmEnriched = false;
        if (request.Config.EnableApiEndpointDocs)
        {
            Report(request, "Cataloging API endpoints…");
            var filtered = EndpointCatalog.Filter(
                analysis.StaticAnalysis?.Endpoints,
                request.Config).ToList();
            EndpointCatalog.EnsureDefaultDescriptions(filtered);
            EndpointCatalog.AttachToModules(modules, modulePlan.Modules, filtered);
            endpointCatalog = filtered;

            if (request.Config.EnableEndpointLlmEnrichment
                && filtered.Count > 0
                && llm.CanUseLiveLlm(request.Config, request.ProviderOverride))
            {
                try
                {
                    endpointsLlmEnriched = await EnrichEndpointDescriptionsAsync(
                            filtered,
                            analysis,
                            request,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (endpointsLlmEnriched)
                    {
                        // Re-attach so modules see enriched descriptions
                        EndpointCatalog.AttachToModules(modules, modulePlan.Modules, filtered);
                        steps.Add("endpoint-llm-enrichment");
                    }
                }
                catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
                {
                    if (!request.Config.AllowOfflineFallback)
                    {
                        logger.LogError(ex, "Endpoint LLM enrichment failed and AllowOfflineFallback=false");
                        throw;
                    }

                    logger.LogWarning(ex, "Endpoint LLM enrichment failed; keeping heuristic descriptions");
                    warnings.Add("Endpoint LLM enrichment failed; using heuristic descriptions.");
                }
            }

            steps.Add($"api-endpoints:{filtered.Count}");
        }

        // Step 5: Cross-link / consistency pass (deterministic in v1; LLM optional later)
        Report(request, "Validating cross-links…");
        ValidateAndNormalizeLinks(modules, crossCutting, warnings);
        steps.Add("cross-link-validation");

        // Step 5b: Guardrails on structured docs (LLM + offline)
        if (request.Config.EnablePostProcessing)
        {
            Report(request, "Post-processing wiki content…");
            var processContext = BuildPostProcessContext(analysis, request, knownWikiPages: null);
            postCorrections.AddRange(postProcessor.ProcessArchitecture(architecture, processContext).Corrections);
            postCorrections.AddRange(postProcessor.ProcessModulePlan(modulePlan, processContext).Corrections);
            foreach (var module in modules)
            {
                postCorrections.AddRange(postProcessor.ProcessModule(module, processContext).Corrections);
            }

            foreach (var item in crossCutting)
            {
                postCorrections.AddRange(postProcessor.ProcessCrossCutting(item, processContext).Corrections);
            }

            steps.Add("post-process-structured");
        }

        // Step 6: Assemble sections including index
        Report(request, "Assembling index and support pages…");
        var generatedAt = DateTimeOffset.UtcNow;
        var sections = BuildSections(
            analysis,
            architecture,
            modules,
            crossCutting,
            request,
            generatedAt,
            endpointCatalog,
            endpointsLlmEnriched);
        steps.Add("index-and-support-pages");

        // Step 6b: Guardrails on rendered Markdown
        if (request.Config.EnablePostProcessing)
        {
            var knownPages = sections
                .Select(s => s.RelativePath.Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var processContext = BuildPostProcessContext(analysis, request, knownPages);
            var (cleanedSections, sectionResult) = postProcessor.ProcessSections(sections, processContext);
            sections = cleanedSections.ToList();
            postCorrections.AddRange(sectionResult.Corrections);
            steps.Add("post-process-markdown");

            if (postCorrections.Count > 0)
            {
                logger.LogInformation(
                    "Wiki post-processor applied {Count} correction(s) for {Repo}",
                    postCorrections.Count,
                    analysis.RepoName);
                foreach (var group in postCorrections.GroupBy(c => c.RuleId).OrderByDescending(g => g.Count()))
                {
                    logger.LogDebug(
                        "Post-process rule {Rule}: {Count} correction(s)",
                        group.Key,
                        group.Count());
                }

                warnings.Add(
                    $"Post-processor applied {postCorrections.Count} correction(s) " +
                    $"(paths/dependencies/deprecation/links). See logs for details.");
            }
        }

        if (anyOffline)
        {
            // Distinguish credential-less offline from partial LLM runs that fell back mid-pipeline.
            if (!llm.CanUseLiveLlm(request.Config, request.ProviderOverride))
            {
                warnings.Add(
                    "One or more pipeline steps used offline generation because LLM credentials are not configured.");
            }
            else
            {
                warnings.Add(
                    "One or more pipeline steps fell back to offline generation after LLM errors "
                    + "(see logs for transport/timeout/parse failures). Other steps may still be LLM-assisted.");
            }
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

    private static WikiPostProcessContext BuildPostProcessContext(
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        IReadOnlySet<string>? knownWikiPages)
    {
        var knownRepoPaths = analysis.Files
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var wikiRoot = !string.IsNullOrWhiteSpace(request.OutputPath)
            ? request.OutputPath
            : Path.Combine(analysis.RepoPath, request.Config.OutputPath);

        return new WikiPostProcessContext
        {
            RepoRoot = analysis.RepoPath,
            WikiOutputRoot = wikiRoot,
            Mode = WikiPostProcessor.ParseMode(request.Config.PostProcessingMode),
            SourceHasObsoleteMarkers =
                (analysis.StaticAnalysis?.ObsoleteSymbols.Count > 0)
                || WikiPostProcessor.DetectObsoleteMarkers(analysis.RepoPath, analysis.Files),
            KnownWikiPages = knownWikiPages ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            KnownRepoPaths = knownRepoPaths
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
            var offlineOnly = OfflineModulePlanner.Plan(analysis, request.Config);
            StabilizeModulePlanIds(offlineOnly, request);
            return offlineOnly;
        }

        try
        {
            var maxModules = ResolveMaxModules(request.Config);
            var prompts = ResolvePrompts(analysis.RepoPath);
            var system = prompts.GetPrompt("SystemPrompt");
            var user = prompts.Render("ModulePlanPrompt", new Dictionary<string, string>
            {
                ["RepoName"] = analysis.RepoName,
                ["RepoSummary"] = SummaryForLlm(analysis, request.Config),
                ["MaxModules"] = maxModules.ToString()
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

            // Cap + enrich related files from inventory.
            if (plan.Modules.Count > maxModules)
            {
                plan.Modules = plan.Modules.Take(maxModules).ToList();
            }

            EnrichPlanFromInventory(plan, analysis, request.Config);
            StabilizeModulePlanIds(plan, request);
            return plan;
        }
        catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
        {
            if (!request.Config.AllowOfflineFallback)
            {
                logger.LogError(ex, "Module planning via LLM failed and AllowOfflineFallback=false");
                throw;
            }

            logger.LogWarning(ex, "Module planning via LLM failed; using offline planner");
            var offlinePlan = OfflineModulePlanner.Plan(analysis, request.Config);
            StabilizeModulePlanIds(offlinePlan, request);
            return offlinePlan;
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
            return OfflineModulePlanner.BuildModuleDocument(descriptor, analysis, request.Config);
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

            // Endpoints always come from static analysis (LLM schemas rarely include them).
            if (document.Endpoints.Count == 0 && analysis.StaticAnalysis is not null)
            {
                document.Endpoints = EndpointCatalog.ForModule(
                    descriptor,
                    EndpointCatalog.Filter(analysis.StaticAnalysis.Endpoints, request.Config)).ToList();
            }

            return document;
        }
        catch (Exception ex) when (ArchitectureGenerator.ShouldFallbackToOffline(ex, cancellationToken))
        {
            if (!request.Config.AllowOfflineFallback)
            {
                logger.LogError(
                    ex,
                    "Module generation failed for {Module} and AllowOfflineFallback=false",
                    descriptor.Id);
                throw;
            }

            logger.LogWarning(ex, "Module generation failed for {Module}; using offline content", descriptor.Id);
            var offline = OfflineModulePlanner.BuildModuleDocument(descriptor, analysis, request.Config);
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
            if (!request.Config.AllowOfflineFallback)
            {
                logger.LogError(ex, "Cross-cutting LLM generation failed and AllowOfflineFallback=false");
                throw;
            }

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
            maxChars: config.MaxLlmSummaryChars > 0
                ? config.MaxLlmSummaryChars
                : Constants.Config.MaxLlmSummaryChars);

    private IPromptManager ResolvePrompts(string repoPath)
    {
        var overrideDir = Path.Combine(
            repoPath,
            Constants.Paths.ConfigDirectoryName,
            Constants.Paths.PromptsDirectoryName);
        if (Directory.Exists(overrideDir))
        {
            return PromptManager.ForRepository(
                repoPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptManager>.Instance);
        }

        return promptManager;
    }

    private static void EnrichPlanFromInventory(
        ModulePlan plan,
        RepoAnalysisResult analysis,
        AgentWikiConfig config)
    {
        var maxFiles = config.MaxFilesPerModule > 0 ? config.MaxFilesPerModule : 40;
        foreach (var module in plan.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id))
            {
                module.Id = OfflineModulePlanner.Plan(analysis, config).Modules
                    .FirstOrDefault()?.Id ?? "module";
            }

            module.Id = ModuleIdStabilizer.Slug(module.Id);

            if (module.RelatedFiles.Count > 0)
            {
                module.RelatedFiles = module.RelatedFiles
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(maxFiles)
                    .ToList();
                continue;
            }

            foreach (var root in module.RootPaths)
            {
                var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
                var matches = analysis.Files
                    .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                                || prefix == "./" || prefix == "/")
                    .Select(f => f.RelativePath)
                    .Take(maxFiles);
                module.RelatedFiles.AddRange(matches);
            }

            module.RelatedFiles = module.RelatedFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxFiles)
                .ToList();
        }
    }

    /// <summary>
    /// On incremental / non-cleared runs, reuse existing module filenames.
    /// Full generate clears modules/ first, so new LLM slugs are fine.
    /// </summary>
    private void StabilizeModulePlanIds(ModulePlan plan, WikiGenerationRequest request)
    {
        // Full generate clears area folders before write — no need to force old ids.
        if (!request.Incremental)
        {
            return;
        }

        try
        {
            ModuleIdStabilizer.StabilizeModuleIds(
                plan,
                request.OutputPath,
                lastRunModuleIds: null,
                warnings: null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Module id stabilization skipped");
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

    private async Task<bool> EnrichEndpointDescriptionsAsync(
        IReadOnlyList<EndpointInfo> endpoints,
        RepoAnalysisResult analysis,
        WikiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        // Cap enrichment payload for cost/latency.
        var batch = endpoints.Take(40).ToList();
        if (batch.Count == 0)
        {
            return false;
        }

        var catalogJson = JsonSerializer.Serialize(
            batch.Select(e => new
            {
                method = e.HttpMethod,
                route = e.Route,
                handler = e.HandlerName,
                kind = e.Kind
            }),
            JsonOptions);

        var system =
            "You document HTTP APIs for coding agents. Reply with JSON only: " +
            "{ \"items\": [ { \"method\": \"GET\", \"route\": \"/path\", \"description\": \"one sentence\" } ] }. " +
            "Keep each description under 20 words. Do not invent routes that are not listed.";
        var user =
            $"Repository: {analysis.RepoName}\n" +
            "Write a short purpose description for each endpoint:\n" +
            catalogJson;

        var completion = await llm.CompleteAsync(
                request.Config,
                system,
                user,
                request.ModelOverride,
                request.ProviderOverride,
                options: LlmRequestOptions.WikiGeneration,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var json = LlmJson.ExtractPayload(completion.Content);
        using var doc = JsonDocument.Parse(json);
        JsonElement items = default;
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("items", out var itemsProp)
            && itemsProp.ValueKind == JsonValueKind.Array)
        {
            items = itemsProp;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            items = doc.RootElement;
        }
        else
        {
            return false;
        }

        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in items.EnumerateArray())
        {
            var method = LlmJson.ReadStringish(el, "method", "httpMethod", "verb") ?? "";
            var route = LlmJson.ReadStringish(el, "route", "path") ?? "";
            var description = LlmJson.ReadStringish(el, "description", "purpose", "summary");
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            byKey[$"{method.Trim().ToUpperInvariant()} {route.Trim()}"] = description.Trim();
        }

        if (byKey.Count == 0)
        {
            return false;
        }

        var applied = 0;
        foreach (var ep in endpoints)
        {
            var key = $"{ep.HttpMethod.Trim().ToUpperInvariant()} {ep.Route.Trim()}";
            if (byKey.TryGetValue(key, out var desc))
            {
                ep.Description = desc;
                applied++;
            }
        }

        logger.LogInformation(
            "Endpoint LLM enrichment applied {Applied}/{Total} description(s)",
            applied,
            endpoints.Count);
        return applied > 0;
    }

    private static IReadOnlyList<WikiSection> BuildSections(
        RepoAnalysisResult analysis,
        ArchitectureDocument architecture,
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<CrossCuttingDocument> crossCutting,
        WikiGenerationRequest request,
        DateTimeOffset generatedAt,
        IReadOnlyList<EndpointInfo> endpointCatalog,
        bool endpointsLlmEnriched)
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
                BuildKeyComponents(analysis, architecture, modules, endpointCatalog)),
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

        if (request.Config.EnableApiEndpointDocs)
        {
            sections.Insert(2, new WikiSection(
                "api-endpoints",
                "API Endpoints",
                "api-endpoints.md",
                ApiEndpointsMarkdownRenderer.Render(
                    analysis.RepoName,
                    endpointCatalog,
                    usedRoslyn: analysis.StaticAnalysis?.UsedRoslyn == true,
                    llmEnriched: endpointsLlmEnriched)));
        }

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
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<EndpointInfo> endpointCatalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Key Components");
        sb.AppendLine();
        sb.AppendLine("> Combines architecture components, module map, public API endpoints, and inventory.");
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
                var purpose = module.Purpose.Replace('\n', ' ').Replace('\r', ' ').Trim();
                sb.AppendLine($"- [{module.Title}]({module.RelativePath}) — {purpose}");
            }

            sb.AppendLine();
        }

        if (endpointCatalog.Count > 0)
        {
            sb.AppendLine("## Public API endpoints");
            sb.AppendLine();
            sb.AppendLine($"See full catalog: [api-endpoints.md](api-endpoints.md) ({endpointCatalog.Count} endpoint(s)).");
            sb.AppendLine();
            ApiEndpointsMarkdownRenderer.AppendEndpointTable(sb, endpointCatalog, maxRows: 20);
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
            3. Check `{{output}}api-endpoints.md` for HTTP / Function routes.
            4. Open the relevant page under `{{output}}modules/` ({{moduleCount}} modules documented).
            5. Check `{{output}}cross-cutting/` ({{crossCuttingCount}} topics) for shared conventions.
            6. Use `{{output}}inventory.md` when you need exact paths.

            ## Important

            - Content may be AI-generated; verify against source of truth.
            - Do not commit secrets into wiki pages.
            - Re-run `agent-wiki generate` or `update` after meaningful structural changes.
            """;
    }

    private static void Report(WikiGenerationRequest request, string message)
    {
        request.Progress?.Report(message);
        // Progress is also mirrored at Information in the file log via callers; keep this quiet.
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
    public static string ExtractJsonPayload(string raw) => LlmJson.ExtractPayload(raw);

    public static ModuleDocument ParseModuleDocument(string raw, ModuleDescriptor descriptor)
    {
        var json = ArchitectureGenerator.ExtractJsonObject(raw);
        ModuleDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ModuleDocument>(json, JsonOptions)
                       ?? new ModuleDocument();
        }
        catch (JsonException)
        {
            // Manual salvage when the model returns free-form objects for string fields.
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            document = new ModuleDocument
            {
                Id = LlmJson.ReadStringish(root, "id", "moduleId") ?? descriptor.Id,
                Title = LlmJson.ReadStringish(root, "title", "name") ?? descriptor.Name,
                Purpose = LlmJson.ReadStringish(root, "purpose", "summary", "description") ?? descriptor.Summary,
                EntryPoints = LlmJson.ReadStringList(root, "entryPoints", "entry_points", "entries"),
                Dependencies = LlmJson.ReadStringList(root, "dependencies", "dependsOn", "deps"),
                KeyTypes = LlmJson.ReadStringList(root, "keyTypes", "types", "keyClasses"),
                HowToExtend = LlmJson.ReadStringList(root, "howToExtend", "extension", "guidance"),
                Gotchas = LlmJson.ReadStringList(root, "gotchas", "warnings", "risks"),
                RelatedFiles = LlmJson.ReadStringList(root, "relatedFiles", "files")
            };
        }

        // Normalize free-form fields when deserialization partially succeeded.
        using (var doc = JsonDocument.Parse(json))
        {
            var root = doc.RootElement;
            if (string.IsNullOrWhiteSpace(document.Purpose))
            {
                document.Purpose = LlmJson.ReadStringish(root, "purpose", "summary", "description")
                                   ?? descriptor.Summary;
            }

            if (document.Dependencies.Count == 0)
            {
                document.Dependencies = LlmJson.ReadStringList(root, "dependencies", "dependsOn", "deps");
            }

            if (document.EntryPoints.Count == 0)
            {
                document.EntryPoints = LlmJson.ReadStringList(root, "entryPoints", "entry_points");
            }

            if (document.RelatedFiles.Count == 0)
            {
                document.RelatedFiles = LlmJson.ReadStringList(root, "relatedFiles", "files");
            }
        }

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            document.Id = descriptor.Id;
        }

        if (string.IsNullOrWhiteSpace(document.Title))
        {
            document.Title = descriptor.Name;
        }

        if (string.IsNullOrWhiteSpace(document.Purpose))
        {
            document.Purpose = descriptor.Summary;
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
