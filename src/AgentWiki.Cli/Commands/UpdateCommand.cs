using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Incremental wiki update (Phase 1: same as generate with incremental flag set).
/// Full git-based change detection arrives in Phase 5.
/// </summary>
public sealed class UpdateCommand(
    IConfigLoader configLoader,
    IWikiGenerator wikiGenerator) : AsyncCommand<GenerationSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GenerationSettings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — incremental update");
        AnsiConsole.MarkupLine("[grey]Phase 1 note: update currently rewrites the placeholder wiki. Git change detection is Phase 5.[/]");

        var config = await configLoader
            .LoadAsync(settings.RepoPath, settings.ConfigPath)
            .ConfigureAwait(false);

        config = configLoader.ApplyCliOverrides(
            config,
            repoPath: settings.RepoPath,
            outputPath: settings.OutputPath,
            model: settings.Model,
            provider: settings.Provider);

        var repoPath = Path.GetFullPath(config.RepoPath);
        var outputPath = Path.IsPathRooted(config.OutputPath)
            ? Path.GetFullPath(config.OutputPath)
            : Path.GetFullPath(Path.Combine(repoPath, config.OutputPath));

        var request = new WikiGenerationRequest
        {
            Config = config,
            RepoPath = repoPath,
            OutputPath = outputPath,
            Force = true, // update is non-interactive by default (CI-friendly)
            DryRun = settings.DryRun,
            Incremental = true,
            ModelOverride = settings.Model,
            ProviderOverride = settings.Provider
        };

        GenerationResult result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Updating wiki (Phase 1 placeholder)…", async _ =>
            {
                result = await wikiGenerator.GenerateAsync(request).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return GenerateCommand.RenderResult(result);
    }
}
