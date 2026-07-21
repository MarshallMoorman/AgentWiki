using System.ComponentModel;
using AgentWiki.Cli.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>Shared options for workspace subcommands.</summary>
public class WorkspaceSettingsBase : CommandSettings
{
    [CommandOption("-r|--repo-path <PATH>")]
    [Description("Path to the workspace root (default: current directory)")]
    [DefaultValue(".")]
    public string RepoPath { get; init; } = ".";

    [CommandOption("--workspace-config <PATH>")]
    [Description("Optional explicit path to workspace.json")]
    public string? WorkspaceConfigPath { get; init; }

    [CommandOption("--verbose")]
    [Description("Enable verbose (debug) logging to console and file")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }
}

/// <summary>Settings for workspace generate / update.</summary>
public class WorkspaceGenerationSettings : WorkspaceSettingsBase
{
    [CommandOption("-o|--output <PATH>")]
    [Description("System wiki output path relative to workspace root (default: docs/knowledge-base)")]
    public string? OutputPath { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    [Description("LLM model or Azure OpenAI deployment name (for member wiki generation)")]
    public string? Model { get; init; }

    [CommandOption("--provider <PROVIDER>")]
    [Description("LLM provider: azure-openai | openai | github-models | offline")]
    public string? Provider { get; init; }

    [CommandOption("--force")]
    [Description("Force regeneration of member wikis and system pages")]
    [DefaultValue(false)]
    public bool Force { get; init; }

    [CommandOption("--dry-run")]
    [Description("Analyze and report without writing files")]
    [DefaultValue(false)]
    public bool DryRun { get; init; }
}

/// <summary>Settings for workspace init.</summary>
public class WorkspaceInitSettings : WorkspaceSettingsBase
{
    [CommandArgument(0, "[NAME]")]
    [Description("Optional workspace display name")]
    public string? Name { get; init; }

    [CommandOption("--force")]
    [Description("Overwrite existing workspace.json")]
    [DefaultValue(false)]
    public bool Force { get; init; }
}

/// <summary>Settings for workspace add.</summary>
public class WorkspaceAddSettings : WorkspaceSettingsBase
{
    [CommandArgument(0, "<ID>")]
    [Description("Stable member id (e.g. loan-service)")]
    public string MemberId { get; init; } = "";

    [CommandArgument(1, "<PATH_OR_REMOTE>")]
    [Description("Local path or git remote URL")]
    public string PathOrRemote { get; init; } = "";

    [CommandOption("--label <LABEL>")]
    [Description("Optional display label")]
    public string? Label { get; init; }

    [CommandOption("--branch <BRANCH>")]
    [Description("Optional branch for remote members")]
    public string? Branch { get; init; }
}

/// <summary><c>agent-wiki workspace init</c> — scaffold workspace.json.</summary>
public sealed class WorkspaceInitCommand(IWorkspaceInitService initService)
    : AsyncCommand<WorkspaceInitSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkspaceInitSettings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — workspace init");
        CliConsole.WriteLogHint();

        var root = PathUtility.ExpandAndResolve(settings.RepoPath);
        AnsiConsole.MarkupLine($"[grey]Workspace root:[/] {Markup.Escape(root)}");

        var result = await initService
            .InitializeAsync(root, settings.Name, settings.Force)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            CliConsole.WriteError(result.Error ?? result.Message);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");
        if (result.FilesCreated.Count > 0)
        {
            foreach (var f in result.FilesCreated)
            {
                AnsiConsole.MarkupLine($"  [grey]+[/] {Markup.Escape(f)}");
            }
        }

        return 0;
    }
}

/// <summary><c>agent-wiki workspace add</c> — add a member to workspace.json.</summary>
public sealed class WorkspaceAddCommand(IWorkspaceInitService initService)
    : AsyncCommand<WorkspaceAddSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkspaceAddSettings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — workspace add member");
        var root = PathUtility.ExpandAndResolve(settings.RepoPath);
        var result = await initService
            .AddMemberAsync(
                root,
                settings.MemberId,
                settings.PathOrRemote,
                settings.Label,
                settings.Branch,
                settings.WorkspaceConfigPath)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            CliConsole.WriteError(result.Error ?? result.Message);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");
        return 0;
    }
}

/// <summary><c>agent-wiki workspace generate</c> — full system wiki + member ensure.</summary>
public sealed class WorkspaceGenerateCommand(
    IWorkspaceConfigLoader configLoader,
    IWorkspaceOrchestrator orchestrator) : AsyncCommand<WorkspaceGenerationSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkspaceGenerationSettings settings) =>
        await WorkspaceCommandHelpers
            .RunGenerationAsync(configLoader, orchestrator, settings, incremental: false)
            .ConfigureAwait(false);
}

/// <summary><c>agent-wiki workspace update</c> — incremental system wiki refresh.</summary>
public sealed class WorkspaceUpdateCommand(
    IWorkspaceConfigLoader configLoader,
    IWorkspaceOrchestrator orchestrator) : AsyncCommand<WorkspaceGenerationSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkspaceGenerationSettings settings) =>
        await WorkspaceCommandHelpers
            .RunGenerationAsync(configLoader, orchestrator, settings, incremental: true)
            .ConfigureAwait(false);
}

/// <summary><c>agent-wiki workspace status</c> — members, last-run, wiki health.</summary>
public sealed class WorkspaceStatusCommand(IWorkspaceOrchestrator orchestrator)
    : AsyncCommand<WorkspaceSettingsBase>
{
    public override async Task<int> ExecuteAsync(CommandContext context, WorkspaceSettingsBase settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — workspace status");
        CliConsole.WriteLogHint();

        var root = PathUtility.ExpandAndResolve(settings.RepoPath);
        var status = await orchestrator
            .GetStatusAsync(root, settings.WorkspaceConfigPath)
            .ConfigureAwait(false);

        if (!status.Success || status.Config is null)
        {
            CliConsole.WriteError(status.Error ?? "Failed to load workspace.");
            return 1;
        }

        var config = status.Config;
        AnsiConsole.MarkupLine($"[grey]Workspace:[/] {Markup.Escape(config.Name)}");
        AnsiConsole.MarkupLine($"[grey]Root:[/] {Markup.Escape(config.WorkspaceRoot)}");
        AnsiConsole.MarkupLine($"[grey]Output:[/] {Markup.Escape(config.OutputPath)}");
        if (!string.IsNullOrWhiteSpace(config.Description))
        {
            AnsiConsole.MarkupLine($"[grey]Description:[/] {Markup.Escape(config.Description)}");
        }

        if (status.LastRun is { } last)
        {
            AnsiConsole.MarkupLine(
                $"[grey]Last run:[/] {Markup.Escape(last.TimestampUtc.ToString("u"))} "
                + $"(mode={Markup.Escape(last.Mode)}, members={last.Members.Count})");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Last run:[/] (none)");
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Members[/]")
            .AddColumn("Id")
            .AddColumn("Label")
            .AddColumn("Source")
            .AddColumn("Resolved")
            .AddColumn("Wiki");

        var wikiById = status.WikiStatuses.ToDictionary(w => w.MemberId, StringComparer.OrdinalIgnoreCase);
        foreach (var m in status.ResolvedMembers)
        {
            var def = m.Definition;
            var source = !string.IsNullOrWhiteSpace(def.Path)
                ? def.Path!
                : (def.Remote ?? "—");
            var resolved = m.Success ? "[green]ok[/]" : "[red]error[/]";
            var wiki = wikiById.TryGetValue(def.Id, out var ws)
                ? ws.Summary switch
                {
                    "ok" => "[green]ok[/]",
                    "stale" => "[yellow]stale[/]",
                    "missing" => "[red]missing[/]",
                    "incomplete" => "[yellow]incomplete[/]",
                    _ => Markup.Escape(ws.Summary)
                }
                : "—";

            table.AddRow(
                Markup.Escape(def.Id),
                Markup.Escape(def.DisplayName),
                Markup.Escape(source.Length > 48 ? source[..45] + "…" : source),
                resolved,
                wiki);
        }

        AnsiConsole.Write(table);

        if (status.Warnings.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warnings:[/]");
            foreach (var w in status.Warnings.Take(15))
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(w)}");
            }
        }

        return 0;
    }
}

internal static class WorkspaceCommandHelpers
{
    public static async Task<int> RunGenerationAsync(
        IWorkspaceConfigLoader configLoader,
        IWorkspaceOrchestrator orchestrator,
        WorkspaceGenerationSettings settings,
        bool incremental)
    {
        AnsiConsole.MarkupLine(
            incremental
                ? "[bold blue]AgentWiki[/] — workspace update"
                : "[bold blue]AgentWiki[/] — workspace generate");
        CliConsole.WriteLogHint();

        var root = PathUtility.ExpandAndResolve(settings.RepoPath);
        var load = await configLoader
            .LoadAsync(root, settings.WorkspaceConfigPath)
            .ConfigureAwait(false);

        if (!load.Success || load.Config is null)
        {
            CliConsole.WriteError(load.Error ?? "Failed to load workspace config.");
            return 1;
        }

        var config = load.Config;
        foreach (var w in load.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(w)}");
        }

        var outputRel = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? config.OutputPath
            : settings.OutputPath!;
        var outputPath = Path.IsPathRooted(PathUtility.ExpandHome(outputRel))
            ? PathUtility.ExpandAndResolve(outputRel)
            : PathUtility.ExpandAndResolve(Path.Combine(root, outputRel));

        AnsiConsole.MarkupLine($"[grey]Workspace:[/] {Markup.Escape(config.Name)}");
        AnsiConsole.MarkupLine($"[grey]Root:[/] {Markup.Escape(root)}");
        AnsiConsole.MarkupLine($"[grey]Output:[/] {Markup.Escape(outputPath)}");
        AnsiConsole.MarkupLine($"[grey]Members:[/] {config.Members.Count}");
        if (settings.DryRun)
        {
            AnsiConsole.MarkupLine("[yellow]Dry-run mode[/] — no files will be written.");
        }

        WorkspaceGenerationResult result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Starting…", async ctx =>
            {
                var progress = new Progress<string>(msg => ctx.Status(Markup.Escape(msg)));
                var request = new WorkspaceGenerationRequest
                {
                    Config = config,
                    WorkspaceRoot = root,
                    OutputPath = outputPath,
                    Force = settings.Force,
                    DryRun = settings.DryRun,
                    Incremental = incremental,
                    ModelOverride = settings.Model,
                    ProviderOverride = settings.Provider,
                    Progress = progress
                };
                result = await orchestrator.GenerateAsync(request).ConfigureAwait(false);
            })
            .ConfigureAwait(false);

        return RenderResult(result);
    }

    internal static int RenderResult(WorkspaceGenerationResult result)
    {
        if (!result.Success)
        {
            CliConsole.WriteError(result.Error ?? result.Message);
            if (!string.IsNullOrWhiteSpace(result.CorrelationId))
            {
                AnsiConsole.MarkupLine($"[grey]Correlation ID:[/] {Markup.Escape(result.CorrelationId)}");
            }

            foreach (var w in result.Warnings.Take(10))
            {
                AnsiConsole.MarkupLine($"[yellow]![/] {Markup.Escape(w)}");
            }

            CliConsole.WriteLogHint();
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title(result.DryRun ? "[bold yellow]Workspace run summary (dry-run)[/]" : "[bold]Workspace run summary[/]")
            .AddColumn("Property")
            .AddColumn("Value");

        if (!string.IsNullOrWhiteSpace(result.CorrelationId))
        {
            table.AddRow("Correlation ID", $"[cyan]{Markup.Escape(result.CorrelationId)}[/]");
        }

        table.AddRow("Output", Markup.Escape(result.OutputPath ?? "—"));
        table.AddRow("Dry-run", result.DryRun ? "[yellow]yes[/]" : "no");
        table.AddRow("Members", result.MemberCount.ToString());
        table.AddRow("Member wikis refreshed", result.MembersGenerated.ToString());
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
        if (result.StepsCompleted.Count > 0)
        {
            table.AddRow("Steps", Markup.Escape(string.Join(" → ", result.StepsCompleted)));
        }

        AnsiConsole.Write(table);

        if (result.Warnings.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warnings:[/]");
            foreach (var w in result.Warnings.Take(20))
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(w)}");
            }
        }

        CliConsole.WriteLogHint();
        return 0;
    }
}
