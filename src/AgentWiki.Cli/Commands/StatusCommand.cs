using System.Text.Json;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Displays current configuration, last-run state, optional live inventory, and wiki status.
/// </summary>
public sealed class StatusCommand(
    IConfigLoader configLoader,
    IRepoAnalyzer repoAnalyzer,
    ILastRunStore lastRunStore) : AsyncCommand<StatusCommand.Settings>
{
    /// <summary>CLI settings for <c>agent-wiki status</c>.</summary>
    public sealed class Settings : CommandSettingsBase
    {
        [CommandOption("--analyze")]
        [System.ComponentModel.Description("Run a live repository analysis and show inventory stats")]
        [System.ComponentModel.DefaultValue(false)]
        public bool Analyze { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine($"[bold blue]AgentWiki[/] v{AgentWikiConstants.Version} — status");

        var config = await configLoader
            .LoadAsync(settings.RepoPath, settings.ConfigPath)
            .ConfigureAwait(false);

        config = configLoader.ApplyCliOverrides(config, repoPath: settings.RepoPath);

        var repoPath = PathUtility.ExpandAndResolve(config.RepoPath);
        var configFile = Path.Combine(repoPath, AgentWikiConstants.ConfigDirectoryName, AgentWikiConstants.ConfigFileName);
        var outputPath = Path.IsPathRooted(PathUtility.ExpandHome(config.OutputPath))
            ? PathUtility.ExpandAndResolve(config.OutputPath)
            : PathUtility.ExpandAndResolve(Path.Combine(repoPath, config.OutputPath));
        var metaPath = Path.Combine(outputPath, AgentWikiConstants.MetaFileName);
        var agentMd = Path.Combine(repoPath, config.AgentMdPath);
        var lastRun = await lastRunStore.LoadAsync(repoPath).ConfigureAwait(false);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Configuration[/]")
            .AddColumn("Key")
            .AddColumn("Value");

        table.AddRow("Tool version", AgentWikiConstants.Version);
        table.AddRow("Repo path", Markup.Escape(repoPath));
        table.AddRow("Config file", File.Exists(configFile) ? $"[green]{Markup.Escape(configFile)}[/]" : "[yellow](not found — run init)[/]");
        table.AddRow("Output path", Markup.Escape(outputPath));
        table.AddRow("Output exists", Directory.Exists(outputPath) ? "[green]yes[/]" : "[yellow]no[/]");
        var provider = LlmSettings.NormalizeProvider(config.Provider);
        var effectiveModel = LlmSettings.ResolveModel(config);
        var notReady = LlmSettings.DescribeNotReadyReason(config);
        var envPath = Path.Combine(repoPath, ".env");
        var hasDotEnv = File.Exists(envPath);

        table.AddRow("Provider", Markup.Escape(config.Provider));
        table.AddRow("defaultModel", Markup.Escape(config.DefaultModel));
        table.AddRow(
            "Effective model",
            $"[cyan]{Markup.Escape(effectiveModel)}[/] [grey](what generate will call)[/]");
        table.AddRow(
            "LLM timeout",
            config.LlmTimeoutSeconds == 300
                ? "300s [grey](built-in default — set llmTimeoutSeconds or AGENTWIKI_LlmTimeoutSeconds to change)[/]"
                : $"{config.LlmTimeoutSeconds}s");
        table.AddRow("Max summary chars", config.MaxLlmSummaryChars.ToString("N0"));
        table.AddRow("Agent MD", File.Exists(agentMd) ? $"[green]{Markup.Escape(agentMd)}[/]" : Markup.Escape(agentMd) + " [grey](missing)[/]");
        table.AddRow("Incremental", config.EnableIncrementalUpdates ? "enabled" : "disabled");
        table.AddRow("Max files", config.MaxFilesToAnalyze.ToString());
        table.AddRow("Ignore patterns", config.IgnorePatterns.Count.ToString());
        table.AddRow("Repo .env", hasDotEnv ? $"[green]{Markup.Escape(envPath)}[/]" : "[grey](not present)[/]");
        table.AddRow("Azure endpoint", string.IsNullOrWhiteSpace(config.AzureOpenAI.Endpoint)
            ? "[grey](not set)[/]"
            : Markup.Escape(RedactEndpoint(config.AzureOpenAI.Endpoint)));
        table.AddRow("Azure deployment", string.IsNullOrWhiteSpace(config.AzureOpenAI.DeploymentName)
            ? "[grey](not set — uses defaultModel)[/]"
            : Markup.Escape(config.AzureOpenAI.DeploymentName));
        table.AddRow("Azure API key", string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) ? "[grey](not set)[/]" : "[green]***[/]");
        table.AddRow("Managed identity", config.AzureOpenAI.UseManagedIdentity ? "yes" : "no");
        table.AddRow("OpenAI endpoint", string.IsNullOrWhiteSpace(config.OpenAI.Endpoint)
            ? "[grey](default/public)[/]"
            : Markup.Escape(RedactEndpoint(config.OpenAI.Endpoint)));
        table.AddRow("OpenAI model", string.IsNullOrWhiteSpace(config.OpenAI.Model)
            ? "[grey](not set — uses defaultModel)[/]"
            : Markup.Escape(config.OpenAI.Model));
        table.AddRow("OpenAI API key", string.IsNullOrWhiteSpace(config.OpenAI.ApiKey) ? "[grey](not set)[/]" : "[green]***[/]");
        table.AddRow(
            "LLM ready",
            notReady is null
                ? "[green]yes[/]"
                : $"[yellow]no — {Markup.Escape(notReady)}[/]");
        table.AddRow("Log directory", Markup.Escape(AgentWikiLogging.LogDirectory));
        table.AddRow("Today's log", Markup.Escape(AgentWikiLogging.TodayLogFilePath));

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "[grey]Config priority (highest wins):[/] CLI → repo [cyan].env[/] → [cyan].agentwiki/config.json[/] → process [cyan]AGENTWIKI_*[/] → tool defaults.");
        AnsiConsole.MarkupLine(
            "[grey]Commented-out keys in config.json are ignored (JSONC). Built-in timeout default is 300s.[/]");
        if (provider is "openai" or "github-models"
            && string.IsNullOrWhiteSpace(config.OpenAI.ApiKey))
        {
            AnsiConsole.MarkupLine(
                "[yellow]Tip:[/] set [cyan]openAI.apiKey[/] / [cyan]openAI.endpoint[/] in config, or "
                + "[cyan]AGENTWIKI_OpenAI__ApiKey[/] in [cyan].env[/] / shell (your openAI block is currently unset).");
        }

        if (lastRun is not null)
        {
            AnsiConsole.WriteLine();
            var last = new Table()
                .Border(TableBorder.Rounded)
                .Title("[bold]Last successful run[/]")
                .AddColumn("Key")
                .AddColumn("Value");

            last.AddRow("Commit", Markup.Escape(lastRun.CommitSha ?? "—"));
            last.AddRow("Timestamp (UTC)", lastRun.TimestampUtc.ToString("O"));
            last.AddRow("Mode", Markup.Escape(lastRun.Mode));
            last.AddRow("Modules", lastRun.ModuleIds.Count.ToString());
            last.AddRow("Files written", lastRun.FilesWritten.Count.ToString());
            last.AddRow("Tool version", Markup.Escape(lastRun.ToolVersion ?? "—"));
            last.AddRow("Correlation ID", Markup.Escape(lastRun.CorrelationId ?? "—"));
            AnsiConsole.Write(last);
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No .agentwiki/last-run.json yet. Run generate to create a baseline.[/]");
        }

        if (settings.Analyze)
        {
            AnsiConsole.WriteLine();
            RepoAnalysisResult? analysis = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Analyzing repository…", async _ =>
                {
                    analysis = await repoAnalyzer.AnalyzeAsync(repoPath, config).ConfigureAwait(false);
                })
                .ConfigureAwait(false);

            if (analysis is not null)
            {
                RenderAnalysis(analysis);
            }
        }

        if (File.Exists(metaPath))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Wiki metadata[/] ([grey].agentwiki-meta.json[/])");
            try
            {
                var json = await File.ReadAllTextAsync(metaPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var metaTable = new Table()
                    .Border(TableBorder.Simple)
                    .AddColumn("Field")
                    .AddColumn("Value");

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("filesWritten") || prop.NameEquals("languages")
                        || prop.NameEquals("modules") || prop.NameEquals("crossCutting")
                        || prop.NameEquals("steps") || prop.NameEquals("changeDetection"))
                    {
                        var text = prop.Value.ValueKind == JsonValueKind.Array
                            ? prop.Value.GetArrayLength().ToString() + " item(s)"
                            : prop.Value.ValueKind == JsonValueKind.Object
                                ? "(object)"
                                : prop.Value.ToString();
                        metaTable.AddRow(prop.Name, Markup.Escape(text));
                        continue;
                    }

                    metaTable.AddRow(prop.Name, Markup.Escape(prop.Value.ToString()));
                }

                AnsiConsole.Write(metaTable);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Could not parse meta file:[/] {Markup.Escape(ex.Message)}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No wiki metadata found. Run generate to create a wiki.[/]");
        }

        if (!settings.Analyze)
        {
            AnsiConsole.MarkupLine("[grey]Tip: pass --analyze for a live repository inventory.[/]");
        }

        return 0;
    }

    private static void RenderAnalysis(RepoAnalysisResult analysis)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Live inventory[/]")
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Discovery", Markup.Escape(analysis.DiscoveryMethod));
        table.AddRow("Total files", analysis.Stats.TotalFiles.ToString());
        table.AddRow("Selected", analysis.Stats.SelectedFiles.ToString());
        table.AddRow("Approx. lines", analysis.Stats.TotalLines.ToString("N0"));
        table.AddRow("Duration", analysis.Duration.TotalSeconds.ToString("F2") + "s");
        table.AddRow(
            "Languages",
            analysis.Stats.DetectedLanguages.Count == 0
                ? "—"
                : Markup.Escape(string.Join(", ", analysis.Stats.DetectedLanguages)));

        AnsiConsole.Write(table);

        var catTable = new Table()
            .Border(TableBorder.Simple)
            .Title("[bold]By category[/]")
            .AddColumn("Category")
            .AddColumn("Count");

        foreach (var category in Enum.GetValues<FileCategory>())
        {
            analysis.Stats.FilesByCategory.TryGetValue(category, out var count);
            catTable.AddRow(category.ToString(), count.ToString());
        }

        AnsiConsole.Write(catTable);

        if (analysis.Stats.TopFolders.Count > 0)
        {
            var folderTable = new Table()
                .Border(TableBorder.Simple)
                .Title("[bold]Top folders[/]")
                .AddColumn("Folder")
                .AddColumn("Files");

            foreach (var folder in analysis.Stats.TopFolders.Take(10))
            {
                folderTable.AddRow(Markup.Escape(folder.RelativePath), folder.FileCount.ToString());
            }

            AnsiConsole.Write(folderTable);
        }

        foreach (var warning in analysis.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }
    }

    private static string RedactEndpoint(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}/";
        }

        return endpoint;
    }
}
