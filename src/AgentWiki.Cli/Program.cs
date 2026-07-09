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

// Configure Serilog early so startup failures are captured.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", AgentWikiConstants.ProductName)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

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
            .WithDescription("Incrementally update the wiki based on recent changes (Phase 5: git-aware)")
            .WithExample("update")
            .WithExample("update", "--repo-path", ".", "--output", "docs/wiki");

        config.AddCommand<StatusCommand>("status")
            .WithDescription("Show current configuration and wiki status")
            .WithExample("status")
            .WithExample("status", "--repo-path", ".");
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
    services.AddSingleton<IWikiGenerator, SemanticWikiGenerator>();

    // Spectre resolves command types from the container.
    services.AddSingleton<InitCommand>();
    services.AddSingleton<GenerateCommand>();
    services.AddSingleton<UpdateCommand>();
    services.AddSingleton<StatusCommand>();
}
