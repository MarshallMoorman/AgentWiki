using AgentWiki.Cli.Commands;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Cli.Services;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Cli;

// Note: Spectre reserves -v for --version at the app level; use --verbose for debug logs.
var verbose = args.Any(a =>
    string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
    || string.Equals(a, "/verbose", StringComparison.OrdinalIgnoreCase));

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentWiki", "logs");
Directory.CreateDirectory(logDir);
var logFile = Path.Combine(logDir, "agent-wiki-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", AgentWikiConstants.ProductName)
    .Enrich.WithProperty("Version", AgentWikiConstants.Version)
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: verbose
            ? "[{Timestamp:HH:mm:ss} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"
            : "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        logFile,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

if (verbose)
{
    Log.Debug("Verbose logging enabled. File log: {LogFile}", logFile);
}

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
            Log.Error(ex, "Command failed");
            if (verbose)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[grey]Re-run with --verbose for full details.[/]");
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
    });

    return await app.RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
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
}
