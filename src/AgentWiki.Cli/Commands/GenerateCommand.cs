using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Full one-shot wiki generation.
/// </summary>
public sealed class GenerateCommand(
    IConfigLoader configLoader,
    IWikiGenerator wikiGenerator) : AsyncCommand<GenerationSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GenerationSettings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — full generation");

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

        if (!settings.Force && !settings.DryRun && Directory.Exists(outputPath) && Directory.EnumerateFileSystemEntries(outputPath).Any())
        {
            if (!AnsiConsole.Confirm($"Output directory [cyan]{outputPath}[/] is not empty. Overwrite?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 2;
            }
        }

        var request = new WikiGenerationRequest
        {
            Config = config,
            RepoPath = repoPath,
            OutputPath = outputPath,
            Force = settings.Force,
            DryRun = settings.DryRun,
            Incremental = false,
            ModelOverride = settings.Model,
            ProviderOverride = settings.Provider
        };

        GenerationResult result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Analyzing repository and running multi-step wiki generation…", async _ =>
            {
                result = await wikiGenerator.GenerateAsync(request).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return RenderResult(result);
    }

    internal static int RenderResult(GenerationResult result)
    {
        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? result.Message)}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Property")
            .AddColumn("Value");

        table.AddRow("Output", Markup.Escape(result.OutputPath ?? "—"));
        table.AddRow("Files written", result.FilesWritten.Count.ToString());
        table.AddRow("Duration", result.Duration.TotalSeconds.ToString("F2") + "s");
        table.AddRow("Input tokens", result.InputTokens.ToString());
        table.AddRow("Output tokens", result.OutputTokens.ToString());

        if (result.Analysis is { } analysis)
        {
            table.AddRow("Discovery", Markup.Escape(analysis.DiscoveryMethod));
            table.AddRow("Files analyzed", analysis.Stats.TotalFiles.ToString());
            table.AddRow("Selected for LLM", analysis.Stats.SelectedFiles.ToString());
            table.AddRow("Approx. lines", analysis.Stats.TotalLines.ToString("N0"));
            table.AddRow(
                "Languages",
                analysis.Stats.DetectedLanguages.Count == 0
                    ? "—"
                    : Markup.Escape(string.Join(", ", analysis.Stats.DetectedLanguages.Take(8))));
        }

        if (result.ChangeDetection is { } changes)
        {
            table.AddRow("Change detection", Markup.Escape(changes.DetectionMethod));
            table.AddRow("Baseline SHA", Markup.Escape(ShortSha(changes.BaselineCommitSha)));
            table.AddRow("Current SHA", Markup.Escape(ShortSha(changes.CurrentCommitSha)));
            table.AddRow("Changed files", changes.ChangedFiles.Count.ToString());
            table.AddRow("No changes", changes.NoChanges ? "yes" : "no");
            table.AddRow("Full regen", changes.RequiresFullRegeneration ? "yes" : "no");
        }

        AnsiConsole.Write(table);

        if (result.ChangeDetection is { ChangedFiles: { Count: > 0 } files })
        {
            var display = files.Count <= 20 ? files : files.Take(20).ToList();
            AnsiConsole.MarkupLine(
                files.Count <= 20
                    ? "[bold]Changed files:[/]"
                    : $"[bold]Changed files:[/] {files.Count} (showing first 20)");
            foreach (var file in display)
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(file)}");
            }
        }

        if (result.Analysis is { Stats.FilesByCategory: var byCategory })
        {
            var catTable = new Table()
                .Border(TableBorder.Simple)
                .Title("[bold]Files by category[/]")
                .AddColumn("Category")
                .AddColumn("Count");

            foreach (var category in Enum.GetValues<FileCategory>())
            {
                byCategory.TryGetValue(category, out var count);
                catTable.AddRow(category.ToString(), count.ToString());
            }

            AnsiConsole.Write(catTable);
        }

        if (result.FilesWritten.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Files:[/]");
            foreach (var file in result.FilesWritten)
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(file)}");
            }
        }

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }

        return 0;
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "—" : sha.Length <= 7 ? sha : sha[..7];
}
