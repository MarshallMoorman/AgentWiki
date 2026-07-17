using System.Collections.ObjectModel;
using AgentWiki.App.Infrastructure;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using AgentWiki.Desktop.Services;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

/// <summary>Shared Generate / Update options, progress, and results.</summary>
public abstract partial class GenerationViewModelBase : ViewModelBase
{
    private readonly IConfigLoader _configLoader;
    private readonly IWikiGenerator _wikiGenerator;
    private CancellationTokenSource? _cts;

    protected GenerationViewModelBase(
        IConfigLoader configLoader,
        IWikiGenerator wikiGenerator,
        bool incremental)
    {
        _configLoader = configLoader;
        _wikiGenerator = wikiGenerator;
        Incremental = incremental;
        Force = incremental; // update is non-interactive / force implied
    }

    public bool Incremental { get; }

    public ObservableCollection<string> ProgressLines { get; } = [];
    public ObservableCollection<KeyValueItem> ResultRows { get; } = [];
    public ObservableCollection<string> FilesWritten { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public ObservableCollection<string> ChangedFiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _repoPath = "";

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private string? _provider;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private bool _force;

    [ObservableProperty]
    private bool _dryRun;

    [ObservableProperty]
    private bool _verbose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Ready.";

    [ObservableProperty]
    private string _resultMessage = "";

    [ObservableProperty]
    private bool? _lastSucceeded;

    /// <summary>
    /// Applies the active repository (and optional config) and re-evaluates command CanExecute.
    /// Always refreshes command state so buttons enable when the tab loads even if
    /// <see cref="RepoPath"/> is unchanged from a previous visit.
    /// </summary>
    public void BindRepo(string repoPath, AgentWikiConfig? config)
    {
        RepoPath = repoPath;
        if (config is not null)
        {
            OutputPath = config.OutputPath;
            Provider = config.Provider;
            Model = config.DefaultModel;
        }

        RefreshCommandStates();
    }

    /// <summary>Forces CanExecute re-evaluation for generate/update commands after tab load or bind.</summary>
    public void RefreshCommandStates()
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath) || IsRunning)
        {
            return;
        }

        try
        {
            IsRunning = true;
            ProgressLines.Clear();
            ResultRows.Clear();
            FilesWritten.Clear();
            Warnings.Clear();
            ChangedFiles.Clear();
            ResultMessage = "";
            LastSucceeded = null;
            StatusText = Incremental ? "Starting update…" : "Starting generate…";

            _cts = new CancellationTokenSource();
            var config = await _configLoader.LoadAsync(RepoPath).ConfigureAwait(true);
            config = _configLoader.ApplyCliOverrides(
                config,
                repoPath: RepoPath,
                outputPath: OutputPath,
                model: Model,
                provider: Provider);

            var resolvedRepo = PathResolver.ResolveRepo(config.RepoPath);
            var resolvedOutput = PathResolver.ResolveOutput(config, resolvedRepo);

            var progress = new Progress<string>(msg =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressLines.Add(msg);
                    StatusText = msg;
                });
            });

            var request = new WikiGenerationRequest
            {
                Config = config,
                RepoPath = resolvedRepo,
                OutputPath = resolvedOutput,
                Force = Force || Incremental,
                DryRun = DryRun,
                Incremental = Incremental,
                ModelOverride = string.IsNullOrWhiteSpace(Model) ? null : Model,
                ProviderOverride = string.IsNullOrWhiteSpace(Provider) ? null : Provider,
                Progress = progress
            };

            var result = await _wikiGenerator
                .GenerateAsync(request, _cts.Token)
                .ConfigureAwait(true);

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            ResultMessage = "Generation cancelled.";
            LastSucceeded = false;
        }
        catch (Exception ex)
        {
            StatusText = "Failed.";
            ResultMessage = ex.Message;
            LastSucceeded = false;
            Warnings.Add(ex.Message);
            AgentWikiLogging.LogError(ex.Message, ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            // IsRunning NotifyCanExecuteChangedFor covers this; call again for safety after dispose.
            RefreshCommandStates();
        }
    }

    private bool CanRun() => !IsRunning && !string.IsNullOrWhiteSpace(RepoPath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsRunning;

    private void ApplyResult(GenerationResult result)
    {
        LastSucceeded = result.Success;
        ResultMessage = result.Success ? result.Message : (result.Error ?? result.Message);
        StatusText = result.Success ? "Completed." : "Failed.";

        ResultRows.Add(new("Success", result.Success ? "yes" : "no"));
        if (!string.IsNullOrWhiteSpace(result.CorrelationId))
        {
            ResultRows.Add(new("Correlation ID", result.CorrelationId));
        }

        ResultRows.Add(new("Output", result.OutputPath ?? "—"));
        ResultRows.Add(new("Dry-run", result.DryRun ? "yes" : "no"));
        ResultRows.Add(new("Offline fallback", result.UsedOfflineFallback ? "yes" : "no"));
        ResultRows.Add(new("Modules", result.ModuleCount.ToString()));
        ResultRows.Add(new(result.DryRun ? "Files planned" : "Files written", result.FilesWritten.Count.ToString()));
        if (result.DryRun)
        {
            ResultRows.Add(new("Would create", result.FilesWouldCreate.Count.ToString()));
            ResultRows.Add(new("Would update", result.FilesWouldUpdate.Count.ToString()));
            ResultRows.Add(new("Unchanged", result.FilesUnchanged.Count.ToString()));
        }

        ResultRows.Add(new("Duration", result.Duration.TotalSeconds.ToString("F2") + "s"));
        ResultRows.Add(new("Input tokens", result.InputTokens.ToString("N0")));
        ResultRows.Add(new("Output tokens", result.OutputTokens.ToString("N0")));
        if (result.CostEstimate is { } cost)
        {
            ResultRows.Add(
                new(
                    "Est. cost (USD)",
                    result.InputTokens > 0 || result.OutputTokens > 0
                        ? $"{cost.FormatUsd()} ({cost.Model})"
                        : "n/a (no LLM tokens)"));
        }

        if (result.StepsCompleted.Count > 0)
        {
            ResultRows.Add(new("Steps", string.Join(" → ", result.StepsCompleted.Take(10))
                                        + (result.StepsCompleted.Count > 10 ? " …" : "")));
        }

        if (result.Analysis is { } analysis)
        {
            ResultRows.Add(new("Discovery", analysis.DiscoveryMethod));
            ResultRows.Add(new("Files analyzed", analysis.Stats.TotalFiles.ToString()));
            ResultRows.Add(new("Selected for LLM", analysis.Stats.SelectedFiles.ToString()));
            ResultRows.Add(new("Approx. lines", analysis.Stats.TotalLines.ToString("N0")));
        }

        if (result.ChangeDetection is { } changes)
        {
            ResultRows.Add(new("Change detection", changes.DetectionMethod));
            ResultRows.Add(new("Baseline SHA", ShortSha(changes.BaselineCommitSha)));
            ResultRows.Add(new("Current SHA", ShortSha(changes.CurrentCommitSha)));
            ResultRows.Add(new("Changed files", changes.ChangedFiles.Count.ToString()));
            ResultRows.Add(new("No changes", changes.NoChanges ? "yes" : "no"));
            ResultRows.Add(new("Full regen", changes.RequiresFullRegeneration ? "yes" : "no"));
            foreach (var file in changes.ChangedFiles.Take(50))
            {
                ChangedFiles.Add(file);
            }
        }

        if (result.DryRun)
        {
            foreach (var file in result.FilesWouldCreate.Take(100))
            {
                FilesWritten.Add("+ " + file);
            }

            foreach (var file in result.FilesWouldUpdate.Take(100))
            {
                FilesWritten.Add("~ " + file);
            }
        }
        else
        {
            foreach (var file in result.FilesWritten)
            {
                FilesWritten.Add(file);
            }
        }

        foreach (var warning in result.Warnings)
        {
            Warnings.Add(warning);
        }
    }

    private static string ShortSha(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "—" : sha.Length <= 7 ? sha : sha[..7];
}

public sealed class GenerateViewModel(IConfigLoader configLoader, IWikiGenerator wikiGenerator)
    : GenerationViewModelBase(configLoader, wikiGenerator, incremental: false);

public sealed class UpdateViewModel(IConfigLoader configLoader, IWikiGenerator wikiGenerator)
    : GenerationViewModelBase(configLoader, wikiGenerator, incremental: true);
