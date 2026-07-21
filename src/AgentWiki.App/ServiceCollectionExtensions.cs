using AgentWiki.App.Infrastructure;
using AgentWiki.App.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Generation;
using Microsoft.Extensions.DependencyInjection;

namespace AgentWiki.App;

/// <summary>
/// Dependency injection registration for AgentWiki application services.
/// Used by both the CLI host and the Desktop companion.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all AgentWiki core application services (config, analysis, LLM, generation).
    /// Hosts should also register logging (<c>ILogger&lt;T&gt;</c>) before resolving services.
    /// </summary>
    public static IServiceCollection AddAgentWikiServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IConfigLoader, ConfigLoader>();
        services.AddSingleton<IInitService, InitService>();
        services.AddSingleton<IRepoAnalyzer, RepoAnalyzer>();
        services.AddSingleton<IStaticAnalyzer, RoslynStaticAnalyzer>();
        services.AddSingleton<IOutputWriter, MarkdownOutputWriter>();
        services.AddSingleton<IPromptManager, PromptManager>();
        services.AddSingleton<ILlmCompletionService, SemanticKernelLlmCompletionService>();
        services.AddSingleton<IArchitectureGenerator, ArchitectureGenerator>();
        services.AddSingleton<IWikiPostProcessor, WikiPostProcessor>();
        services.AddSingleton<IWikiGenerationOrchestrator, WikiGenerationOrchestrator>();
        services.AddSingleton<IAgentBootstrapper, AgentBootstrapper>();
        services.AddSingleton<IAgentsMdGenerator, AgentsMdGenerator>();
        services.AddSingleton<IReadmeGenerator, ReadmeGenerator>();
        services.AddSingleton<ILastRunStore, LastRunStore>();
        services.AddSingleton<IChangeDetector, GitChangeDetector>();
        services.AddSingleton<IRunTelemetry, ApplicationInsightsRunTelemetry>();
        services.AddSingleton<IWikiGenerator, SemanticWikiGenerator>();

        // Multi-repo workspace (Phase 1 — file-based only; 02b corpus/routing extensions)
        services.AddSingleton<IWorkspaceConfigLoader, WorkspaceConfigLoader>();
        services.AddSingleton<IWorkspaceMemberResolver, WorkspaceMemberResolver>();
        services.AddSingleton<IWorkspaceInitService, WorkspaceInitService>();
        services.AddSingleton<IMemberConfigApplier, MemberConfigApplier>();
        services.AddSingleton<IMemberWikiInspector, MemberWikiInspector>();
        services.AddSingleton<ICrossRepoSignalCollector, CrossRepoSignalCollector>();
        services.AddSingleton<IWorkspaceLastRunStore, WorkspaceLastRunStore>();
        services.AddSingleton<IWorkspaceOrchestrator, WorkspaceOrchestrator>();

        return services;
    }
}
