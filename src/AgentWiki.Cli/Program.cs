using AgentWiki.App;
using AgentWiki.App.Infrastructure;
using AgentWiki.Cli.Commands;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

// Note: Spectre reserves -v for --version at the app level; use --verbose for debug logs.
var verbose = args.Any(a =>
    string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
    || string.Equals(a, "/verbose", StringComparison.OrdinalIgnoreCase));

// File logging always; console sink only when verbose (Spectre owns the terminal otherwise).
AgentWikiLogging.Configure(verbose, enableConsoleSink: true);

try
{
    var services = new ServiceCollection();
    ConfigureServices(services);

    var registrar = new TypeRegistrar(services);
    var app = new CommandApp(registrar);

    app.Configure(config =>
    {
        config.SetApplicationName(Constants.Product.ToolName);
        config.SetApplicationVersion(Constants.Product.Version);
        config.ValidateExamples();
        config.SetExceptionHandler((ex, _) =>
        {
            // Full exception always goes to the log file; console stays readable.
            CliConsole.WriteError(ex.Message, ex);
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

        config.AddCommand<AgentsCommand>("agents")
            .WithDescription("Generate a complete AGENTS.md from analysis, wiki, and instruction files")
            .WithExample("agents")
            .WithExample("agents", "--repo-path", ".", "--force")
            .WithExample("agents", "--dry-run")
            .WithExample("agents", "--with-readme");

        config.AddBranch("workspace", workspace =>
        {
            workspace.SetDescription("Multi-repo workspace commands (file-based system knowledge base)");

            workspace.AddCommand<WorkspaceInitCommand>("init")
                .WithDescription("Scaffold .agentwiki/workspace.json with sample members")
                .WithExample("workspace", "init")
                .WithExample("workspace", "init", "Lending Core")
                .WithExample("workspace", "init", "--force");

            workspace.AddCommand<WorkspaceGenerateCommand>("generate")
                .WithDescription("Generate system knowledge base + ensure member wikis")
                .WithExample("workspace", "generate")
                .WithExample("workspace", "generate", "--repo-path", ".", "--force")
                .WithExample("workspace", "generate", "--dry-run");

            workspace.AddCommand<WorkspaceUpdateCommand>("update")
                .WithDescription("Incrementally update the system wiki from member changes")
                .WithExample("workspace", "update")
                .WithExample("workspace", "update", "--dry-run");

            workspace.AddCommand<WorkspaceStatusCommand>("status")
                .WithDescription("Show workspace members, last-run, and member wiki health")
                .WithExample("workspace", "status")
                .WithExample("workspace", "status", "--repo-path", ".");

            workspace.AddCommand<WorkspaceAddCommand>("add")
                .WithDescription("Add a member (local path or git remote) to workspace.json; id is optional")
                .WithExample("workspace", "add", "../LoanService")
                .WithExample("workspace", "add", "../LoanService", "--id", "loan-service")
                .WithExample("workspace", "add", "loan-service", "../LoanService")
                .WithExample("workspace", "add", "https://github.com/org/Shared.git", "--branch", "main");
        });
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    CliConsole.WriteError(ex.Message, ex);
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

    services.AddAgentWikiServices();

    services.AddSingleton<InitCommand>();
    services.AddSingleton<GenerateCommand>();
    services.AddSingleton<UpdateCommand>();
    services.AddSingleton<StatusCommand>();
    services.AddSingleton<TestProviderCommand>();
    services.AddSingleton<AgentsCommand>();
    services.AddSingleton<WorkspaceInitCommand>();
    services.AddSingleton<WorkspaceGenerateCommand>();
    services.AddSingleton<WorkspaceUpdateCommand>();
    services.AddSingleton<WorkspaceStatusCommand>();
    services.AddSingleton<WorkspaceAddCommand>();
}
