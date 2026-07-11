using AgentWiki.Cli.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
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
        CliConsole.WriteLogHint();

        var config = await configLoader
            .LoadAsync(settings.RepoPath, settings.ConfigPath)
            .ConfigureAwait(false);

        config = configLoader.ApplyCliOverrides(
            config,
            repoPath: settings.RepoPath,
            outputPath: settings.OutputPath,
            model: settings.Model,
            provider: settings.Provider);

        var repoPath = PathUtility.ExpandAndResolve(config.RepoPath);
        var outputPath = Path.IsPathRooted(PathUtility.ExpandHome(config.OutputPath))
            ? PathUtility.ExpandAndResolve(config.OutputPath)
            : PathUtility.ExpandAndResolve(Path.Combine(repoPath, config.OutputPath));

        AnsiConsole.MarkupLine($"[grey]Repo:[/] {Markup.Escape(repoPath)}");
        AnsiConsole.MarkupLine($"[grey]Output:[/] {Markup.Escape(outputPath)}");
        AnsiConsole.MarkupLine(
            $"[grey]Provider:[/] {Markup.Escape(config.Provider)}  [grey]Model:[/] {Markup.Escape(config.DefaultModel)}  [grey]Timeout:[/] {config.LlmTimeoutSeconds}s");
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Dry-run mode[/] — no files will be written.");
        }

        if (!settings.Force && !settings.DryRun && Directory.Exists(outputPath) && Directory.EnumerateFileSystemEntries(outputPath).Any())
        {
            if (!AnsiConsole.Confirm($"Output directory [cyan]{Markup.Escape(outputPath)}[/] is not empty. Overwrite?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 2;
            }
        }

        GenerationResult result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Starting…", async ctx =>
            {
                var progress = new Progress<string>(msg =>
                {
                    // Spectre status supports markup; escape user/repo-derived text.
                    ctx.Status(Markup.Escape(msg));
                });

                var request = new WikiGenerationRequest
                {
                    Config = config,
                    RepoPath = repoPath,
                    OutputPath = outputPath,
                    Force = settings.Force,
                    DryRun = settings.DryRun,
                    Incremental = false,
                    ModelOverride = settings.Model,
                    ProviderOverride = settings.Provider,
                    Progress = progress
                };

                result = await wikiGenerator.GenerateAsync(request).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return RenderResult(result);
    }

    internal static int RenderResult(GenerationResult result)
    {
        if (!result.Success)
        {
            CliConsole.WriteError(result.Error ?? result.Message);
            if (!string.IsNullOrWhiteSpace(result.CorrelationId))
            {
                AnsiConsole.MarkupLine($"[grey]Correlation ID:[/] {Markup.Escape(result.CorrelationId)}");
            }

            CliConsole.WriteLogHint();
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(result.DryRun ? "[bold yellow]Run summary (dry-run)[/]" : "[bold]Run summary[/]")
            .AddColumn("Property")
            .AddColumn("Value");

        if (!string.IsNullOrWhiteSpace(result.CorrelationId))
        {
            table.AddRow("Correlation ID", $"[cyan]{Markup.Escape(result.CorrelationId)}[/]");
        }

        table.AddRow("Output", Markup.Escape(result.OutputPath ?? "—"));
        table.AddRow("Dry-run", result.DryRun ? "[yellow]yes[/]" : "no");
        table.AddRow("Offline fallback", result.UsedOfflineFallback ? "yes" : "no");
        table.AddRow("Modules", result.ModuleCount.ToString());
        table.AddRow(
            result.DryRun ? "Files planned" : "Files written",
            result.FilesWritten.Count.ToString());
        if (result.DryRun)
        {
            table.AddRow("Would create", $"[green]{result.FilesWouldCreate.Count}[/]");
            table.AddRow("Would update", $"[yellow]{result.FilesWouldUpdate.Count}[/]");
            table.AddRow("Unchanged", result.FilesUnchanged.Count.ToString());
        }

        table.AddRow("Duration", result.Duration.TotalSeconds.ToString("F2") + "s");
        table.AddRow("Input tokens", result.InputTokens.ToString("N0"));
        table.AddRow("Output tokens", result.OutputTokens.ToString("N0"));
        if (result.CostEstimate is { } cost)
        {
            if (result.InputTokens > 0 || result.OutputTokens > 0)
            {
                table.AddRow(
                    "Est. cost (USD)",
                    $"[bold]{Markup.Escape(cost.FormatUsd())}[/] [grey]({Markup.Escape(cost.Model)} @ "
                    + $"{cost.InputUsdPerMillion:F2}/{cost.OutputUsdPerMillion:F2} per 1M)[/]");
            }
            else
            {
                table.AddRow("Est. cost (USD)", "[grey]n/a (no LLM tokens)[/]");
            }
        }

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
            if (analysis.StaticAnalysis is { Succeeded: true } sa)
            {
                table.AddRow("Static analysis", Markup.Escape(sa.Summary));
            }
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

        if (result.StepsCompleted.Count > 0)
        {
            table.AddRow(
                "Steps",
                Markup.Escape(string.Join(" → ", result.StepsCompleted.Take(12))
                              + (result.StepsCompleted.Count > 12 ? " …" : "")));
        }

        AnsiConsole.Write(table);

        if (result.DryRun && (result.FilesWouldCreate.Count > 0 || result.FilesWouldUpdate.Count > 0))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]Dry-run impact[/]");
            WriteFileList("Would create", result.FilesWouldCreate, "green");
            WriteFileList("Would update", result.FilesWouldUpdate, "yellow");
            if (result.FilesUnchanged.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    $"[grey]Unchanged:[/] {result.FilesUnchanged.Count} file(s) already match planned content.");
            }
        }
        else if (!result.DryRun && result.FilesWritten.Count > 0)
        {
            AnsiConsole.MarkupLine("[bold]Files written:[/]");
            foreach (var file in result.FilesWritten.Take(40))
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(file)}");
            }

            if (result.FilesWritten.Count > 40)
            {
                AnsiConsole.MarkupLine($"  [grey]…and {result.FilesWritten.Count - 40} more[/]");
            }
        }

        if (result.ChangeDetection is { ChangedFiles: { Count: > 0 } files })
        {
            var display = files.Count <= 20 ? files : files.Take(20).ToList();
            AnsiConsole.MarkupLine(
                files.Count <= 20
                    ? "[bold]Changed source files:[/]"
                    : $"[bold]Changed source files:[/] {files.Count} (showing first 20)");
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

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }

        CliConsole.WriteLogHint();
        return 0;
    }

    private static void WriteFileList(string title, IReadOnlyList<string> files, string color)
    {
        if (files.Count == 0)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(title)} ({files.Count}):[/]");
        foreach (var file in files.Take(30))
        {
            AnsiConsole.MarkupLine($"  • {Markup.Escape(file)}");
        }

        if (files.Count > 30)
        {
            AnsiConsole.MarkupLine($"  [grey]…and {files.Count - 30} more[/]");
        }
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "—" : sha.Length <= 7 ? sha : sha[..7];
}
