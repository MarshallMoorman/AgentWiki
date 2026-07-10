using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Generates structured architecture documents via Semantic Kernel, with offline fallback.
/// </summary>
public sealed class ArchitectureGenerator(
    ILlmCompletionService llm,
    IPromptManager promptManager,
    ILogger<ArchitectureGenerator> logger) : IArchitectureGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = LlmJson.CreateOptions();

    /// <inheritdoc />
    public async Task<ArchitectureDocument> GenerateAsync(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        string? modelOverride = null,
        string? providerOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        if (!llm.CanUseLiveLlm(config, providerOverride))
        {
            logger.LogWarning(
                "LLM credentials not configured (provider={Provider}); using offline architecture generator",
                providerOverride ?? config.Provider);
            return OfflineArchitectureGenerator.Generate(analysis);
        }

        try
        {
            var activePrompts = ResolvePromptManager(analysis.RepoPath);
            var systemPrompt = activePrompts.GetPrompt("SystemPrompt");
            var summary = RepoSummaryBuilder.BuildForLlm(
                analysis.RepoName,
                analysis.RepoPath,
                analysis.Stats,
                analysis.Files,
                maxChars: config.MaxLlmSummaryChars > 0 ? config.MaxLlmSummaryChars : 16_000);
            var userPrompt = activePrompts.Render("ArchitectureOverviewPrompt", new Dictionary<string, string>
            {
                ["RepoName"] = analysis.RepoName,
                ["RepoSummary"] = summary,
                ["Provider"] = providerOverride ?? config.Provider,
                ["Model"] = modelOverride ?? config.DefaultModel
            });

            logger.LogInformation(
                "Architecture LLM prompt size: system={SystemChars} user={UserChars} chars",
                systemPrompt.Length,
                userPrompt.Length);

            var completion = await llm.CompleteAsync(
                    config,
                    systemPrompt,
                    userPrompt,
                    modelOverride,
                    providerOverride,
                    options: LlmRequestOptions.WikiGeneration,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var document = ParseArchitectureJson(completion.Content);
                document.UsedOfflineFallback = false;
                document.TokenUsage = completion.TokenUsage;

                logger.LogInformation(
                    "Architecture generated via LLM for {Repo} (tokens in/out: {In}/{Out})",
                    analysis.RepoName,
                    completion.TokenUsage?.InputTokens ?? 0,
                    completion.TokenUsage?.OutputTokens ?? 0);

                return document;
            }
            catch (Exception parseEx) when (parseEx is not OperationCanceledException)
            {
                logger.LogError(
                    parseEx,
                    "Failed to parse architecture JSON. Raw content (truncated): {Content}",
                    LlmJson.Preview(completion.Content, 800));
                throw;
            }
        }
        catch (Exception ex) when (ShouldFallbackToOffline(ex, cancellationToken))
        {
            logger.LogError(ex, "LLM architecture generation failed; falling back to offline generator");
            var offline = OfflineArchitectureGenerator.Generate(analysis);
            offline.Gotchas.Insert(0, $"LLM generation failed and offline fallback was used: {ex.Message}");
            return offline;
        }
    }

    /// <summary>
    /// Fall back for timeouts and provider errors; rethrow only true user cancellations.
    /// </summary>
    internal static bool ShouldFallbackToOffline(Exception ex, CancellationToken cancellationToken) =>
        LlmResilience.IsTimeoutFailure(ex, cancellationToken)
        || ex is not OperationCanceledException;

    private IPromptManager ResolvePromptManager(string repoPath)
    {
        var overrideDir = Path.Combine(repoPath, ".agentwiki", "prompts");
        if (Directory.Exists(overrideDir) && promptManager is PromptManager)
        {
            // Rebuild with repo overrides; embedded resources remain fallback.
            return PromptManager.ForRepository(
                repoPath,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptManager>.Instance);
        }

        return promptManager;
    }

    public static ArchitectureDocument ParseArchitectureJson(string raw)
    {
        var json = ExtractJsonObject(raw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        ArchitectureDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ArchitectureDocument>(json, JsonOptions)
                       ?? new ArchitectureDocument();
        }
        catch (JsonException)
        {
            // Fully manual fallback when shapes are too free-form.
            document = new ArchitectureDocument();
        }

        // Accept common alternate field names from chatty models.
        document.Title = FirstNonEmpty(document.Title, LlmJson.ReadStringish(root, "title", "name", "heading"))
                         ?? "Architecture Overview";
        document.Summary = FirstNonEmpty(
                               document.Summary,
                               LlmJson.ReadStringish(root, "summary", "overview", "description", "executiveSummary", "abstract"))
                           ?? "";
        document.SystemContext = FirstNonEmpty(
                                     document.SystemContext,
                                     LlmJson.ReadStringish(root, "systemContext", "system_context", "context", "background"))
                                 ?? "";
        document.MermaidDiagram ??= LlmJson.ReadStringish(root, "mermaidDiagram", "mermaid", "diagram");

        if (document.DataFlows.Count == 0)
        {
            document.DataFlows = LlmJson.ReadStringList(root, "dataFlows", "flows", "importantFlows");
        }

        if (document.Decisions.Count == 0)
        {
            document.Decisions = LlmJson.ReadStringList(root, "decisions", "keyDecisions", "architectureDecisions");
        }

        if (document.Gotchas.Count == 0)
        {
            document.Gotchas = LlmJson.ReadStringList(root, "gotchas", "warnings", "risks");
        }

        if (document.HowToExtend.Count == 0)
        {
            document.HowToExtend = LlmJson.ReadStringList(root, "howToExtend", "extensionPoints", "guidance");
        }

        // If still empty, salvage any long string fields as summary.
        if (string.IsNullOrWhiteSpace(document.Summary) && string.IsNullOrWhiteSpace(document.SystemContext))
        {
            var salvage = LlmJson.ReadStringish(root, "content", "body", "architecture", "details");
            if (!string.IsNullOrWhiteSpace(salvage))
            {
                document.Summary = salvage;
            }
        }

        if (string.IsNullOrWhiteSpace(document.Summary)
            && string.IsNullOrWhiteSpace(document.SystemContext)
            && document.Layers.Count == 0
            && document.KeyComponents.Count == 0)
        {
            throw new InvalidOperationException(
                "Architecture JSON missing usable content. Preview: " + LlmJson.Preview(raw, 300));
        }

        // Ensure we have at least a summary line so markdown renders.
        if (string.IsNullOrWhiteSpace(document.Summary))
        {
            document.Summary = document.SystemContext;
        }

        return document;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Extracts a JSON object from a model response that may include markdown fences or prose.
    /// </summary>
    public static string ExtractJsonObject(string raw) => LlmJson.ExtractPayload(raw);
}
