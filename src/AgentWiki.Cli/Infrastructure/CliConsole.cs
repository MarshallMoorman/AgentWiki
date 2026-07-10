using AgentWiki.App.Infrastructure;
using Spectre.Console;

namespace AgentWiki.Cli.Infrastructure;

/// <summary>
/// Spectre.Console helpers for CLI-only user-facing messages (not used by Desktop).
/// </summary>
public static class CliConsole
{
    /// <summary>Prints a short user-facing pointer to the log file.</summary>
    public static void WriteLogHint(string? prefix = null)
    {
        var path = AgentWikiLogging.TodayLogFilePath;
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            AnsiConsole.MarkupLine($"{prefix} [grey]Log:[/] [cyan]{Markup.Escape(path)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[grey]Log file:[/] [cyan]{Markup.Escape(path)}[/]");
        }
    }

    /// <summary>Prints error to the console and points at the detailed log file.</summary>
    public static void WriteError(string message, Exception? ex = null)
    {
        AgentWikiLogging.LogError(message, ex);
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        WriteLogHint("Details were written to the log.");
        if (!AgentWikiLogging.IsVerbose)
        {
            AnsiConsole.MarkupLine("[grey]Re-run with --verbose to also stream diagnostics to the console.[/]");
        }
    }
}
