using System.Text.Json;
using AgentWiki.Core.Abstractions;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
            var userPrompt = activePrompts.Render("ArchitectureOverviewPrompt", new Dictionary<string, string>
            {
                ["RepoName"] = analysis.RepoName,
                ["RepoSummary"] = analysis.Summary,
                ["Provider"] = providerOverride ?? config.Provider,
                ["Model"] = modelOverride ?? config.DefaultModel
            });

            var completion = await llm.CompleteAsync(
                    config,
                    systemPrompt,
                    userPrompt,
                    modelOverride,
                    providerOverride,
                    cancellationToken)
                .ConfigureAwait(false);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "LLM architecture generation failed; falling back to offline generator");
            var offline = OfflineArchitectureGenerator.Generate(analysis);
            offline.Gotchas.Insert(0, $"LLM generation failed and offline fallback was used: {ex.Message}");
            return offline;
        }
    }

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
        var document = JsonSerializer.Deserialize<ArchitectureDocument>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Architecture JSON deserialized to null.");

        if (string.IsNullOrWhiteSpace(document.Summary) && string.IsNullOrWhiteSpace(document.SystemContext))
        {
            throw new InvalidOperationException("Architecture JSON missing required summary/systemContext content.");
        }

        return document;
    }

    /// <summary>
    /// Extracts a JSON object from a model response that may include markdown fences or prose.
    /// </summary>
    public static string ExtractJsonObject(string raw)
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

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("LLM response did not contain a JSON object.");
        }

        return text[start..(end + 1)];
    }
}
