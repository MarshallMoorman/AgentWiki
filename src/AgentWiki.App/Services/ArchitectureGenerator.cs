using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;
using AgentWiki.Core;

namespace AgentWiki.App.Services;

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

        // Explicit offline/mock → inventory generators. Live providers require a working LLM.
        if (LlmSettings.IsExplicitOfflineMode(providerOverride ?? config.Provider))
        {
            logger.LogInformation(
                "Provider is offline; using offline architecture generator for {Repo}",
                analysis.RepoName);
            return OfflineArchitectureGenerator.Generate(analysis);
        }

        LlmSettings.EnsureLiveLlmConfigured(
            config,
            providerOverride,
            llm.CanUseLiveLlm(config, providerOverride));

        try
        {
            var activePrompts = ResolvePromptManager(analysis.RepoPath);
            var systemPrompt = activePrompts.GetPrompt("SystemPrompt");
            // Repo root display path is always portable ("."); AbsolutePath is never sent to the LLM.
            var summary = RepoSummaryBuilder.BuildForLlm(
                analysis.RepoName,
                analysis.RepoPath,
                analysis.Stats,
                analysis.Files,
                maxChars: config.MaxLlmSummaryChars > 0
                    ? config.MaxLlmSummaryChars
                    : Constants.Config.MaxLlmSummaryChars);
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
            if (!config.AllowOfflineFallback)
            {
                logger.LogError(
                    ex,
                    "LLM architecture generation failed and AllowOfflineFallback=false; not using offline generator");
                throw;
            }

            // Transport failures should already have been retried by LlmResilience; log clearly so
            // users can tell "credentials missing" from "network blip then offline architecture".
            if (LlmResilience.IsRetryableFailure(ex))
            {
                logger.LogError(
                    ex,
                    "LLM architecture generation failed after retries (transient transport/HTTP); falling back to offline generator");
            }
            else
            {
                logger.LogError(
                    ex,
                    "LLM architecture generation failed (often JSON shape mismatch); falling back to offline generator "
                    + "(allowOfflineFallback=true). Default is false so production runs fail loudly.");
            }

            var offline = OfflineArchitectureGenerator.Generate(analysis);
            offline.Gotchas.Insert(
                0,
                $"LLM generation failed after retries and offline fallback was used: {ex.Message}");
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
        var overrideDir = Path.Combine(
            repoPath,
            Constants.Paths.ConfigDirectoryName,
            Constants.Paths.PromptsDirectoryName);
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
        document.Title = FirstNonEmpty(document.Title, LlmJson.ReadStringish(root, "title", "name", "heading", "repository"))
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

        // Many models ignore our schema and return a single markdown document field, e.g.:
        // { "repository": "...", "architecture_overview": "# Title\n\n## System Context\n..." }
        // or ChatGPT-style: { "output": "# Title\n..." } / { "text": "..." }
        var markdownBody = FirstNonEmpty(
            document.FullMarkdown,
            LlmJson.ReadStringish(
                root,
                "architecture_overview",
                "architectureOverview",
                "architectureMarkdown",
                "markdown",
                "document",
                "content",
                "body",
                "architecture",
                "details",
                "wiki",
                "output",
                "text",
                "result",
                "response",
                "message",
                "answer"));

        if (!string.IsNullOrWhiteSpace(markdownBody) && LooksLikeMarkdownDocument(markdownBody))
        {
            document.FullMarkdown = markdownBody.Trim();
            // Keep short fields populated for index/meta when possible.
            if (string.IsNullOrWhiteSpace(document.Summary))
            {
                document.Summary = ExtractLeadParagraph(markdownBody);
            }

            if (string.Equals(document.Title, "Architecture Overview", StringComparison.Ordinal)
                && markdownBody.TrimStart().StartsWith('#'))
            {
                var firstLine = markdownBody.TrimStart().Split('\n', 2)[0].Trim().TrimStart('#').Trim();
                if (!string.IsNullOrWhiteSpace(firstLine))
                {
                    document.Title = firstLine;
                }
            }

            return document;
        }

        // Sparse schema salvage: { "repo", "type", "entrypoints": [...] } — still better than offline dump.
        SalvageSparseArchitecture(document, root);

        // If still empty, salvage any long string fields as summary.
        if (string.IsNullOrWhiteSpace(document.Summary) && string.IsNullOrWhiteSpace(document.SystemContext))
        {
            if (!string.IsNullOrWhiteSpace(markdownBody))
            {
                document.Summary = markdownBody;
            }
        }

        if (string.IsNullOrWhiteSpace(document.Summary)
            && string.IsNullOrWhiteSpace(document.SystemContext)
            && string.IsNullOrWhiteSpace(document.FullMarkdown)
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

    /// <summary>
    /// Accept minimal LLM sketches that name the stack and entry points without full schema.
    /// </summary>
    private static void SalvageSparseArchitecture(ArchitectureDocument document, JsonElement root)
    {
        var repo = LlmJson.ReadStringish(root, "repo", "repository", "name", "project", "projectName");
        var type = LlmJson.ReadStringish(root, "type", "kind", "projectType", "stack", "framework");
        var entrypoints = LlmJson.ReadStringList(root, "entrypoints", "entryPoints", "entry_points", "entryPoints");

        if (string.IsNullOrWhiteSpace(type) && entrypoints.Count == 0 && string.IsNullOrWhiteSpace(repo))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(document.Title) || document.Title == "Architecture Overview")
        {
            document.Title = string.IsNullOrWhiteSpace(repo)
                ? "Architecture Overview"
                : $"{repo} Architecture Overview";
        }

        if (string.IsNullOrWhiteSpace(document.Summary))
        {
            document.Summary = string.IsNullOrWhiteSpace(type)
                ? $"{repo ?? "This repository"} architecture (LLM sketch)."
                : $"{repo ?? "This repository"} is a {type}.";
        }

        if (string.IsNullOrWhiteSpace(document.SystemContext))
        {
            document.SystemContext = document.Summary;
        }

        if (document.KeyComponents.Count == 0 && entrypoints.Count > 0)
        {
            foreach (var ep in entrypoints.Take(12))
            {
                document.KeyComponents.Add(new ArchitectureComponent
                {
                    Name = Path.GetFileNameWithoutExtension(ep.Replace('\\', '/')),
                    Path = ep,
                    Purpose = "Entry point / composition root (from LLM sketch)."
                });
            }
        }
    }

    private static bool LooksLikeMarkdownDocument(string text)
    {
        var t = text.TrimStart();
        // Full architecture write-ups almost always include headings and are not a short phrase.
        return t.Length >= 200
               && (t.Contains('#') || t.Contains("## ") || t.Contains("\n- ") || t.Contains("\n* "));
    }

    private static string ExtractLeadParagraph(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var buf = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (buf.Length > 0)
                {
                    break;
                }

                continue;
            }

            if (buf.Length > 0)
            {
                buf.Append(' ');
            }

            buf.Append(trimmed);
            if (buf.Length > 280)
            {
                break;
            }
        }

        var lead = buf.ToString();
        return lead.Length <= 320 ? lead : lead[..317] + "…";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// Extracts a JSON object from a model response that may include markdown fences or prose.
    /// </summary>
    public static string ExtractJsonObject(string raw) => LlmJson.ExtractPayload(raw);
}
