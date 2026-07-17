using System.ComponentModel;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Generates a complete <c>AGENTS.md</c> (full file) from analysis, wiki, and instruction sources.
/// </summary>
public sealed class AgentsCommand(
    IConfigLoader configLoader,
    IAgentsMdGenerator agentsMdGenerator,
    IReadmeGenerator readmeGenerator) : AsyncCommand<AgentsCommand.Settings>
{
    /// <summary>CLI settings for <c>agent-wiki agents</c>.</summary>
    public sealed class Settings : CommandSettingsBase
    {
        [CommandOption("-o|--output <PATH>")]
        [Description("AGENTS.md path relative to the repo (default: AGENTS.md / config agentMdPath)")]
        public string? OutputPath { get; init; }

        [CommandOption("-m|--model <MODEL>")]
        [Description("LLM model or Azure OpenAI deployment name for optional enrichment")]
        public string? Model { get; init; }

        [CommandOption("--provider <PROVIDER>")]
        [Description("LLM provider: azure-openai | openai | github-models")]
        public string? Provider { get; init; }

        [CommandOption("--force")]
        [Description("Overwrite a substantial existing AGENTS.md")]
        [DefaultValue(false)]
        public bool Force { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show what would be written/deleted without changing the filesystem")]
        [DefaultValue(false)]
        public bool DryRun { get; init; }

        [CommandOption("--with-readme")]
        [Description("Also create/replace README.md when missing or generic")]
        [DefaultValue(false)]
        public bool WithReadme { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — full AGENTS.md generation");
        CliConsole.WriteLogHint();

        var config = await configLoader
            .LoadAsync(settings.RepoPath, settings.ConfigPath)
            .ConfigureAwait(false);

        config = configLoader.ApplyCliOverrides(
            config,
            repoPath: settings.RepoPath,
            model: settings.Model,
            provider: settings.Provider);

        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            config.AgentMdPath = settings.OutputPath.Trim();
        }

        var repoPath = PathUtility.ExpandAndResolve(config.RepoPath);
        var wikiAbs = Path.IsPathRooted(PathUtility.ExpandHome(config.OutputPath))
            ? PathUtility.ExpandAndResolve(config.OutputPath)
            : PathUtility.ExpandAndResolve(Path.Combine(repoPath, config.OutputPath));

        AnsiConsole.MarkupLine($"[grey]Repo:[/] {Markup.Escape(repoPath)}");
        AnsiConsole.MarkupLine($"[grey]AGENTS path:[/] {Markup.Escape(config.AgentMdPath)}");
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Dry-run mode[/] — no files will be written or deleted.");
        }

        AgentsMdGenerationResult result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Generating AGENTS.md…", async ctx =>
            {
                var progress = new Progress<string>(msg => ctx.Status(Markup.Escape(msg)));
                result = await agentsMdGenerator
                    .GenerateAsync(
                        new AgentsMdGenerationRequest
                        {
                            Config = config,
                            RepoPath = repoPath,
                            WikiOutputPath = wikiAbs,
                            Force = settings.Force,
                            DryRun = settings.DryRun,
                            ModelOverride = settings.Model,
                            ProviderOverride = settings.Provider,
                            Progress = progress
                        })
                    .ConfigureAwait(false);
            });

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? result.Message)}");
            return 1;
        }

        if (result.Action == AgentsMdAction.Skipped)
        {
            AnsiConsole.MarkupLine($"[yellow]Skipped:[/] {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine("[grey]Hint:[/] pass [cyan]--force[/] to overwrite a substantial AGENTS.md.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");
        }

        if (result.MigratedFrom.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Migrated sources:[/] {Markup.Escape(string.Join(", ", result.MigratedFrom))}");
        }

        if (result.DeletedFiles.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[green]Removed after migration:[/] {Markup.Escape(string.Join(", ", result.DeletedFiles))}");
        }

        if (result.WouldDeleteFiles.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Would delete:[/] {Markup.Escape(string.Join(", ", result.WouldDeleteFiles))}");
        }

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }

        if (settings.WithReadme)
        {
            var readme = await readmeGenerator
                .GenerateAsync(
                    new ReadmeGenerationRequest
                    {
                        Config = config,
                        RepoPath = repoPath,
                        WikiOutputPath = wikiAbs,
                        Force = false,
                        DryRun = settings.DryRun
                    })
                .ConfigureAwait(false);

            if (!readme.Success)
            {
                AnsiConsole.MarkupLine($"[yellow]README:[/] {Markup.Escape(readme.Error ?? readme.Message)}");
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]README:[/] {Markup.Escape(readme.Message)}");
            }
        }

        AnsiConsole.MarkupLine(
            $"[grey]Offline template:[/] {(result.UsedOfflineFallback ? "yes" : "no (LLM enriched)")}");
        return 0;
    }
}
