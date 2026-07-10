using AgentWiki.Cli.Commands;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

// Note: Spectre reserves -v for --version at the app level; use --verbose for debug logs.
var verbose = args.Any(a =>
    string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
    || string.Equals(a, "/verbose", StringComparison.OrdinalIgnoreCase));

AgentWikiLogging.Configure(verbose);

try
{
    var services = new ServiceCollection();
    ConfigureServices(services);

    var registrar = new TypeRegistrar(services);
    var app = new CommandApp(registrar);

    app.Configure(config =>
    {
        config.SetApplicationName(AgentWikiConstants.ToolName);
        config.SetApplicationVersion(AgentWikiConstants.Version);
        config.ValidateExamples();
        config.SetExceptionHandler((ex, _) =>
        {
            // Full exception always goes to the log file; console stays readable.
            AgentWikiLogging.WriteError(ex.Message, ex);
            if (verbose)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }

            return -1;
        });

        config.AddCommand<InitCommand>("init")
            .WithDescription("Scaffold .agentwiki/config.json, sample prompts, and .env.example")
            .WithExample("init")
            .WithExample("init", "--repo-path", ".")
            .WithExample("init", "--force");

        config.AddCommand<GenerateCommand>("generate")
            .WithDescription("Generate a full agent-optimized wiki for a repository")
            .WithExample("generate")
            .WithExample("generate", "--repo-path", ".", "--output", "docs/wiki")
            .WithExample("generate", "--model", "gpt-4o", "--dry-run");

        config.AddCommand<UpdateCommand>("update")
            .WithDescription("Incrementally update the wiki from git changes since last run")
            .WithExample("update")
            .WithExample("update", "--repo-path", ".", "--output", "docs/wiki")
            .WithExample("update", "--dry-run");

        config.AddCommand<StatusCommand>("status")
            .WithDescription("Show current configuration, last-run state, and optional live inventory")
            .WithExample("status")
            .WithExample("status", "--repo-path", ".")
            .WithExample("status", "--analyze");

        config.AddCommand<TestProviderCommand>("test-provider")
            .WithDescription("Verify LLM provider credentials with a minimal chat completion")
            .WithExample("test-provider")
            .WithExample("test-provider", "--provider", "openai", "--model", "gpt-4o")
            .WithExample("test-provider", "--repo-path", ".");
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    AgentWikiLogging.WriteError(ex.Message, ex);
    if (verbose)
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    }

    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static void ConfigureServices(IServiceCollection services)
{
    services.AddLogging(builder =>
    {
        builder.ClearProviders();
        builder.AddSerilog(dispose: true);
    });

    services.AddSingleton<IConfigLoader, ConfigLoader>();
    services.AddSingleton<IInitService, InitService>();
    services.AddSingleton<IRepoAnalyzer, RepoAnalyzer>();
    services.AddSingleton<IOutputWriter, MarkdownOutputWriter>();
    services.AddSingleton<IPromptManager, PromptManager>();
    services.AddSingleton<ILlmCompletionService, SemanticKernelLlmCompletionService>();
    services.AddSingleton<IArchitectureGenerator, ArchitectureGenerator>();
    services.AddSingleton<IWikiGenerationOrchestrator, WikiGenerationOrchestrator>();
    services.AddSingleton<IAgentBootstrapper, AgentBootstrapper>();
    services.AddSingleton<ILastRunStore, LastRunStore>();
    services.AddSingleton<IChangeDetector, GitChangeDetector>();
    services.AddSingleton<IWikiGenerator, SemanticWikiGenerator>();

    services.AddSingleton<InitCommand>();
    services.AddSingleton<GenerateCommand>();
    services.AddSingleton<UpdateCommand>();
    services.AddSingleton<StatusCommand>();
    services.AddSingleton<TestProviderCommand>();
}
