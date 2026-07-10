using AgentWiki.Core.Constants;
using Serilog;
using Serilog.Events;
using Spectre.Console;

namespace AgentWiki.Cli.Infrastructure;

/// <summary>
/// Configures Serilog so diagnostics go to a discoverable log file while the CLI
/// surface stays clean for Spectre.Console (spinners/tables).
/// </summary>
public static class AgentWikiLogging
{
    /// <summary>Directory for rolling log files (<c>~/.agentwiki/logs</c>).</summary>
    public static string LogDirectory { get; private set; } = "";

    /// <summary>Serilog rolling file path pattern (date token handled by Serilog).</summary>
    public static string LogFilePathPattern { get; private set; } = "";

    /// <summary>Resolved path for today's log file (best-effort).</summary>
    public static string TodayLogFilePath =>
        Path.Combine(LogDirectory, $"agent-wiki-{DateTime.Now:yyyyMMdd}.log");

    public static bool IsVerbose { get; private set; }

    /// <summary>
    /// Configures the global Serilog logger.
    /// File always receives detailed events; console only receives logs when <paramref name="verbose"/> is true
    /// (and even then at a controlled level) so Spectre spinners are not corrupted.
    /// </summary>
    public static void Configure(bool verbose)
    {
        IsVerbose = verbose;

        // Prefer a user-visible home directory over LocalApplicationData
        // (on macOS that is "Library/Application Support", which is easy to miss).
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agentwiki",
            "logs");
        Directory.CreateDirectory(LogDirectory);
        LogFilePathPattern = Path.Combine(LogDirectory, "agent-wiki-.log");

        var fileLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(fileLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.SemanticKernel", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", AgentWikiConstants.ProductName)
            .Enrich.WithProperty("Version", AgentWikiConstants.Version)
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.File(
                LogFilePathPattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                restrictedToMinimumLevel: fileLevel,
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (tid:{ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}");

        if (verbose)
        {
            // Verbose mode: still avoid drowning Spectre UI — Debug/Info go to console only between UI actions.
            // Prefer file as source of truth; console gets Warning+ always, Debug when verbose.
            config = config.WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }
        // Non-verbose: NO console sink — Spectre owns the terminal. Everything is in the log file.

        Log.Logger = config.CreateLogger();
        Log.Information(
            "AgentWiki {Version} starting. Verbose={Verbose}. LogFile={LogFile}",
            AgentWikiConstants.Version,
            verbose,
            TodayLogFilePath);
    }

    /// <summary>Prints a short user-facing pointer to the log file (Spectre).</summary>
    public static void WriteLogHint(string? prefix = null)
    {
        var path = TodayLogFilePath;
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
        if (ex is not null)
        {
            Log.Error(ex, "CLI error: {Message}", message);
        }
        else
        {
            Log.Error("CLI error: {Message}", message);
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        WriteLogHint("Details were written to the log.");
        if (!IsVerbose)
        {
            AnsiConsole.MarkupLine("[grey]Re-run with --verbose to also stream diagnostics to the console.[/]");
        }
    }
}
