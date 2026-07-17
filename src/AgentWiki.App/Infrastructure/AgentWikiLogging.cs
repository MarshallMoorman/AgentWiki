using AgentWiki.Core;
using Serilog;
using Serilog.Events;

namespace AgentWiki.App.Infrastructure;

/// <summary>
/// Configures Serilog so diagnostics go to a discoverable log file under <c>~/.agentwiki/logs</c>.
/// Console sink is optional (CLI verbose mode); Desktop typically uses file-only + in-app tail.
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

    /// <summary>Whether verbose (debug) logging is enabled.</summary>
    public static bool IsVerbose { get; private set; }

    /// <summary>
    /// Configures the global Serilog logger.
    /// File always receives detailed events; optional console sink for CLI verbose mode.
    /// </summary>
    /// <param name="verbose">When true, file and console receive Debug-level events.</param>
    /// <param name="enableConsoleSink">When true, also write to the console (CLI only).</param>
    public static void Configure(bool verbose, bool enableConsoleSink = false)
    {
        IsVerbose = verbose;

        // Prefer a user-visible home directory over LocalApplicationData
        // (on macOS that is "Library/Application Support", which is easy to miss).
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.Paths.ConfigDirectoryName,
            "logs");
        Directory.CreateDirectory(LogDirectory);
        LogFilePathPattern = Path.Combine(LogDirectory, $"{Constants.Product.ToolName}-.log");

        var fileLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(fileLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.SemanticKernel", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", Constants.Product.ProductName)
            .Enrich.WithProperty("Version", Constants.Product.Version)
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

        if (enableConsoleSink && verbose)
        {
            // Verbose CLI: still avoid drowning Spectre UI — prefer file as source of truth.
            config = config.WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = config.CreateLogger();
        Log.Information(
            "AgentWiki {Version} starting. Verbose={Verbose}. ConsoleSink={ConsoleSink}. LogFile={LogFile}",
            Constants.Product.Version,
            verbose,
            enableConsoleSink && verbose,
            TodayLogFilePath);
    }

    /// <summary>Records an error to the log file (no console dependency).</summary>
    public static void LogError(string message, Exception? ex = null)
    {
        if (ex is not null)
        {
            Log.Error(ex, "Application error: {Message}", message);
        }
        else
        {
            Log.Error("Application error: {Message}", message);
        }
    }
}
