using System.Collections.ObjectModel;
using System.Text.Json;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class DashboardViewModel(
    IConfigLoader configLoader,
    IRepoAnalyzer repoAnalyzer,
    ILastRunStore lastRunStore) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenAgentsMdCommand))]
    private string _repoPath = "";

    [ObservableProperty]
    private string _summary = "No repository loaded.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private bool _isAnalyzing;

    public ObservableCollection<KeyValueItem> ConfigRows { get; } = [];
    public ObservableCollection<KeyValueItem> LastRunRows { get; } = [];
    public ObservableCollection<KeyValueItem> InventoryRows { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    public async Task LoadAsync(string repoPath, AgentWikiConfig? preloaded = null)
    {
        RepoPath = repoPath;
        ConfigRows.Clear();
        LastRunRows.Clear();
        InventoryRows.Clear();
        Warnings.Clear();

        var config = preloaded
            ?? configLoader.ApplyCliOverrides(
                await configLoader.LoadAsync(repoPath).ConfigureAwait(true),
                repoPath: repoPath);

        var resolvedRepo = PathResolver.ResolveRepo(config.RepoPath);
        var outputPath = PathResolver.ResolveOutput(config, resolvedRepo);
        var configFile = Path.Combine(resolvedRepo, AgentWikiConstants.ConfigDirectoryName, AgentWikiConstants.ConfigFileName);
        var envPath = Path.Combine(resolvedRepo, ".env");
        var agentMd = Path.Combine(resolvedRepo, config.AgentMdPath);
        var notReady = LlmSettings.DescribeNotReadyReason(config);
        var effectiveModel = LlmSettings.ResolveModel(config);

        ConfigRows.Add(new("Tool version", AgentWikiConstants.Version));
        ConfigRows.Add(new("Repo path", resolvedRepo));
        ConfigRows.Add(new("Config file", File.Exists(configFile) ? configFile : "(not found — run Setup)"));
        ConfigRows.Add(new("Output path", outputPath));
        ConfigRows.Add(new("Output exists", Directory.Exists(outputPath) ? "yes" : "no"));
        ConfigRows.Add(new("Provider", config.Provider));
        ConfigRows.Add(new("defaultModel", config.DefaultModel));
        ConfigRows.Add(new("Effective model", effectiveModel));
        ConfigRows.Add(new(
            "LLM timeout",
            config.LlmTimeoutSeconds == 300
                ? "300s (built-in default)"
                : $"{config.LlmTimeoutSeconds}s"));
        ConfigRows.Add(new("Max summary chars", config.MaxLlmSummaryChars.ToString("N0")));
        ConfigRows.Add(new("Agent MD", File.Exists(agentMd) ? agentMd : agentMd + " (missing)"));
        ConfigRows.Add(new("Incremental", config.EnableIncrementalUpdates ? "enabled" : "disabled"));
        ConfigRows.Add(new("Max files", config.MaxFilesToAnalyze.ToString()));
        ConfigRows.Add(new("Ignore patterns", config.IgnorePatterns.Count.ToString()));
        ConfigRows.Add(new("Repo .env", File.Exists(envPath) ? envPath : "(not present)"));
        ConfigRows.Add(new("Azure endpoint", RedactEndpoint(config.AzureOpenAI.Endpoint)));
        ConfigRows.Add(new("Azure deployment", string.IsNullOrWhiteSpace(config.AzureOpenAI.DeploymentName) ? "(uses defaultModel)" : config.AzureOpenAI.DeploymentName!));
        ConfigRows.Add(new("Azure API key", string.IsNullOrWhiteSpace(config.AzureOpenAI.ApiKey) ? "(not set)" : "***"));
        ConfigRows.Add(new("Managed identity", config.AzureOpenAI.UseManagedIdentity ? "yes" : "no"));
        ConfigRows.Add(new("OpenAI endpoint", string.IsNullOrWhiteSpace(config.OpenAI.Endpoint) ? "(default/public)" : RedactEndpoint(config.OpenAI.Endpoint)));
        ConfigRows.Add(new("OpenAI model", string.IsNullOrWhiteSpace(config.OpenAI.Model) ? "(uses defaultModel)" : config.OpenAI.Model!));
        ConfigRows.Add(new("OpenAI API key", string.IsNullOrWhiteSpace(config.OpenAI.ApiKey) ? "(not set)" : "***"));
        ConfigRows.Add(new("LLM ready", notReady is null ? "yes" : $"no — {notReady}"));

        Summary = notReady is null
            ? $"Live LLM ready · model {effectiveModel}"
            : $"Offline-capable · {notReady}";

        var lastRun = await lastRunStore.LoadAsync(resolvedRepo).ConfigureAwait(true);
        if (lastRun is not null)
        {
            LastRunRows.Add(new("Commit", lastRun.CommitSha ?? "—"));
            LastRunRows.Add(new("Timestamp (UTC)", lastRun.TimestampUtc.ToString("O")));
            LastRunRows.Add(new("Mode", lastRun.Mode));
            LastRunRows.Add(new("Modules", lastRun.ModuleIds.Count.ToString()));
            LastRunRows.Add(new("Files written", lastRun.FilesWritten.Count.ToString()));
            LastRunRows.Add(new("Tool version", lastRun.ToolVersion ?? "—"));
            LastRunRows.Add(new("Correlation ID", lastRun.CorrelationId ?? "—"));
        }
        else
        {
            LastRunRows.Add(new("Status", "No last-run.json yet. Run Generate to create a baseline."));
        }

        var metaPath = Path.Combine(outputPath, AgentWikiConstants.MetaFileName);
        if (File.Exists(metaPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaPath).ConfigureAwait(true);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("filesWritten") || prop.NameEquals("languages")
                        || prop.NameEquals("modules") || prop.NameEquals("crossCutting")
                        || prop.NameEquals("steps") || prop.NameEquals("changeDetection"))
                    {
                        var text = prop.Value.ValueKind == JsonValueKind.Array
                            ? prop.Value.GetArrayLength() + " item(s)"
                            : prop.Value.ValueKind == JsonValueKind.Object
                                ? "(object)"
                                : prop.Value.ToString();
                        ConfigRows.Add(new($"meta.{prop.Name}", text));
                        continue;
                    }

                    ConfigRows.Add(new($"meta.{prop.Name}", prop.Value.ToString()));
                }
            }
            catch (Exception ex)
            {
                Warnings.Add($"Could not parse meta file: {ex.Message}");
            }
        }

        RefreshCommandStates();
    }

    /// <summary>Re-evaluate gated commands after tab load / bind.</summary>
    public void RefreshCommandStates()
    {
        AnalyzeCommand.NotifyCanExecuteChanged();
        OpenAgentsMdCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath) || IsAnalyzing)
        {
            return;
        }

        try
        {
            IsAnalyzing = true;
            InventoryRows.Clear();
            Warnings.Clear();

            var config = configLoader.ApplyCliOverrides(
                await configLoader.LoadAsync(RepoPath).ConfigureAwait(true),
                repoPath: RepoPath);
            var analysis = await repoAnalyzer.AnalyzeAsync(RepoPath, config).ConfigureAwait(true);

            InventoryRows.Add(new("Discovery", analysis.DiscoveryMethod));
            InventoryRows.Add(new("Total files", analysis.Stats.TotalFiles.ToString()));
            InventoryRows.Add(new("Selected", analysis.Stats.SelectedFiles.ToString()));
            InventoryRows.Add(new("Approx. lines", analysis.Stats.TotalLines.ToString("N0")));
            InventoryRows.Add(new("Duration", analysis.Duration.TotalSeconds.ToString("F2") + "s"));
            InventoryRows.Add(new(
                "Languages",
                analysis.Stats.DetectedLanguages.Count == 0
                    ? "—"
                    : string.Join(", ", analysis.Stats.DetectedLanguages)));

            foreach (var category in Enum.GetValues<FileCategory>())
            {
                analysis.Stats.FilesByCategory.TryGetValue(category, out var count);
                InventoryRows.Add(new($"Category · {category}", count.ToString()));
            }

            foreach (var folder in analysis.Stats.TopFolders.Take(10))
            {
                InventoryRows.Add(new($"Folder · {folder.RelativePath}", folder.FileCount.ToString()));
            }

            foreach (var warning in analysis.Warnings)
            {
                Warnings.Add(warning);
            }
        }
        catch (Exception ex)
        {
            Warnings.Add(ex.Message);
        }
        finally
        {
            IsAnalyzing = false;
            RefreshCommandStates();
        }
    }

    private bool CanAnalyze() => !IsAnalyzing && !string.IsNullOrWhiteSpace(RepoPath);

    [RelayCommand(CanExecute = nameof(CanOpenAgentsMd))]
    private void OpenAgentsMd()
    {
        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            return;
        }

        var path = Path.Combine(RepoPath, "AGENTS.md");
        if (!File.Exists(path))
        {
            path = Path.Combine(RepoPath, "CLAUDE.md");
        }

        if (File.Exists(path))
        {
            MainViewModel.OpenInOs(path);
        }
    }

    private bool CanOpenAgentsMd() => !string.IsNullOrWhiteSpace(RepoPath);

    private static string RedactEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return "(not set)";
        }

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}/"
            : endpoint;
    }
}

public sealed record KeyValueItem(string Key, string Value);
