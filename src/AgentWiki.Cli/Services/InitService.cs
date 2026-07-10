using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Scaffolds <c>.agentwiki/</c> configuration, sample prompts, and <c>.env.example</c>.
/// </summary>
public sealed class InitService(ILogger<InitService> logger) : IInitService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep empty strings so scaffolded keys (openAI.apiKey, etc.) are visible placeholders.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <inheritdoc />
    public async Task<InitResult> InitializeAsync(
        string repoPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedRepo = Path.GetFullPath(repoPath);
            if (!Directory.Exists(resolvedRepo))
            {
                return InitResult.Fail($"Repository path does not exist: {resolvedRepo}");
            }

            var created = new List<string>();
            var agentWikiDir = Path.Combine(resolvedRepo, AgentWikiConstants.ConfigDirectoryName);
            var promptsDir = Path.Combine(agentWikiDir, "prompts");
            Directory.CreateDirectory(promptsDir);

            var configPath = Path.Combine(agentWikiDir, AgentWikiConstants.ConfigFileName);
            if (File.Exists(configPath) && !force)
            {
                logger.LogInformation("Config already exists at {Path} (use --force to overwrite)", configPath);
            }
            else
            {
                var defaultConfig = CreateDefaultConfig();
                var json = JsonSerializer.Serialize(defaultConfig, JsonOptions);
                await File.WriteAllTextAsync(configPath, json + Environment.NewLine, cancellationToken)
                    .ConfigureAwait(false);
                created.Add(Rel(resolvedRepo, configPath));
                logger.LogInformation("Wrote {Path}", configPath);
            }

            var envExample = Path.Combine(resolvedRepo, ".env.example");
            if (!File.Exists(envExample) || force)
            {
                await File.WriteAllTextAsync(envExample, BuildEnvExample(), cancellationToken)
                    .ConfigureAwait(false);
                created.Add(Rel(resolvedRepo, envExample));
            }

            foreach (var (name, content) in SamplePrompts())
            {
                var promptPath = Path.Combine(promptsDir, name);
                if (File.Exists(promptPath) && !force)
                {
                    continue;
                }

                await File.WriteAllTextAsync(promptPath, content, cancellationToken).ConfigureAwait(false);
                created.Add(Rel(resolvedRepo, promptPath));
            }

            var gitignorePath = Path.Combine(agentWikiDir, ".gitignore");
            if (!File.Exists(gitignorePath) || force)
            {
                await File.WriteAllTextAsync(
                        gitignorePath,
                        """
                        # Local run state (commit config + prompts; keep secrets out of git)
                        last-run.json
                        *.local.json
                        """,
                        cancellationToken)
                    .ConfigureAwait(false);
                created.Add(Rel(resolvedRepo, gitignorePath));
            }

            var message = created.Count > 0
                ? $"Initialized AgentWiki in {resolvedRepo}"
                : $"AgentWiki already initialized in {resolvedRepo} (nothing to create; pass --force to overwrite)";

            return InitResult.Ok(message, created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Init failed for {RepoPath}", repoPath);
            return InitResult.Fail(ex.Message);
        }
    }

    private static AgentWikiConfig CreateDefaultConfig() => new()
    {
        // RepoPath is resolved at runtime; omit a real path from scaffold.
        RepoPath = ".",
        OutputPath = AgentWikiConstants.DefaultOutputPath,
        DefaultModel = AgentWikiConstants.DefaultModel,
        Provider = AgentWikiConstants.DefaultProvider,
        AgentMdPath = AgentWikiConstants.DefaultAgentMdPath,
        MaxFilesToAnalyze = 500,
        EnableIncrementalUpdates = true,
        // Defaults already include bin/obj/node_modules/docs/wiki/.agentwiki.
        AzureOpenAI = new AzureOpenAiOptions
        {
            Endpoint = "https://YOUR_RESOURCE.openai.azure.com/",
            DeploymentName = "gpt-4o",
            ApiKey = "",
            UseManagedIdentity = false
        },
        OpenAI = new OpenAiOptions
        {
            Endpoint = "",
            ApiKey = "",
            Model = "gpt-4o"
        }
    };

    private static string BuildEnvExample() =>
        """
        # AgentWiki environment variables
        # --------------------------------
        # Optional. Prefer putting non-secret settings in .agentwiki/config.json and
        # secrets (API keys) here or in CI secret stores.
        #
        # How to use:
        #   1. Copy this file to .env (never commit .env)
        #   2. Fill in values — agent-wiki loads .env from the repo root automatically
        #      (does not override variables already set in the process environment)
        #   3. Or export the same keys in your shell / CI pipeline
        #
        # Prefix: AGENTWIKI_   Nested keys use double underscore (__)

        AGENTWIKI_Provider=openai
        AGENTWIKI_DefaultModel=gpt-4o
        AGENTWIKI_OutputPath=docs/wiki

        # Azure OpenAI
        AGENTWIKI_AzureOpenAI__Endpoint=https://YOUR_RESOURCE.openai.azure.com/
        AGENTWIKI_AzureOpenAI__DeploymentName=gpt-4o
        AGENTWIKI_AzureOpenAI__ApiKey=
        AGENTWIKI_AzureOpenAI__UseManagedIdentity=false

        # OpenAI (public API) or OpenAI-compatible endpoint
        # Leave Endpoint empty for https://api.openai.com
        AGENTWIKI_OpenAI__Endpoint=
        AGENTWIKI_OpenAI__ApiKey=
        AGENTWIKI_OpenAI__Model=gpt-4o
        """;

    private static IEnumerable<(string Name, string Content)> SamplePrompts()
    {
        yield return ("SystemPrompt.txt",
            """
            You are an expert senior software architect and technical writer specializing in creating documentation optimized for AI coding agents.

            Your goal is to produce clear, structured, actionable Markdown that helps coding agents (GitHub Copilot, Claude, Cursor, custom agents) quickly understand the codebase structure, architecture, patterns, and how to make changes safely.

            Key principles:
            - Be concise but complete.
            - Use hierarchical structure with clear headings.
            - Prefer bullet points, tables, and short code examples over long prose.
            - Always reference actual file paths.
            - Highlight important patterns, conventions, and "gotchas".
            - Make cross-references explicit (use relative Markdown links).
            - Focus on what an agent needs to know to implement features or fix bugs correctly.
            """);

        yield return ("ArchitectureOverviewPrompt.txt",
            """
            Analyze the repository summary and file inventory below.
            Produce a high-level architecture overview in Markdown for coding agents.

            Repository: {{RepoName}}
            Summary:
            {{RepoSummary}}

            Include: system context, major layers, key dependencies, and how to navigate the code.
            """);

        yield return ("ModulePlanPrompt.txt",
            """
            Identify the major modules / bounded contexts in this repository for an agent-optimized wiki.

            Repository: {{RepoName}}
            Summary:
            {{RepoSummary}}

            Return JSON with a modules array (id, name, summary, rootPaths, relatedFiles). Max 8 modules.
            """);

        yield return ("ModuleAnalysisPrompt.txt",
            """
            Document the module "{{ModuleName}}" for AI coding agents.

            Related files:
            {{RelatedFiles}}

            Cover: purpose, public entry points, dependencies, extension points, and gotchas.
            """);

        yield return ("CrossCuttingPrompt.txt",
            """
            Identify cross-cutting concerns for {{RepoName}} (configuration, logging, errors, testing).

            Summary:
            {{RepoSummary}}

            Return JSON items with id, title, summary, patterns, keyFiles, guidance.
            """);

        yield return ("CrossLinkValidationPrompt.txt",
            """
            Review the following wiki pages and improve cross-links and consistency.

            Pages:
            {{WikiPages}}

            Return corrected Markdown where links are broken or sections should reference each other.
            """);
    }

    private static string Rel(string root, string absolute) =>
        Path.GetRelativePath(root, absolute).Replace('\\', '/');
}
