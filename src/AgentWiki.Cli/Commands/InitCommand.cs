using System.ComponentModel;
using AgentWiki.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Scaffolds AgentWiki configuration in a repository.
/// </summary>
public sealed class InitCommand(IInitService initService) : AsyncCommand<InitCommand.Settings>
{
    /// <summary>CLI settings for <c>agent-wiki init</c>.</summary>
    public sealed class Settings : CommandSettingsBase
    {
        [CommandOption("--force")]
        [Description("Overwrite existing config and sample files")]
        [DefaultValue(false)]
        public bool Force { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        AnsiConsole.MarkupLine("[bold blue]AgentWiki[/] — initializing repository…");

        var result = await initService
            .InitializeAsync(settings.RepoPath, settings.Force)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(result.Error ?? result.Message)}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(result.Message)}");

        if (result.FilesCreated.Count > 0)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Created");

            foreach (var file in result.FilesCreated)
            {
                table.AddRow(Markup.Escape(file));
            }

            AnsiConsole.Write(table);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Next steps:");
        AnsiConsole.MarkupLine("  1. Review [cyan].agentwiki/config.json[/]");
        AnsiConsole.MarkupLine("  2. Copy [cyan].env.example[/] → [cyan].env[/] and set credentials");
        AnsiConsole.MarkupLine("  3. Run [cyan]agent-wiki generate[/]");

        return 0;
    }
}
