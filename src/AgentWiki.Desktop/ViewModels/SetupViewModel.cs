using System.Collections.ObjectModel;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentWiki.Desktop.ViewModels;

public partial class SetupViewModel(IInitService initService) : ViewModelBase
{
    [ObservableProperty]
    private string _repoPath = "";

    [ObservableProperty]
    private bool _force;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _message = "Pick a repository and preview what init will create.";

    public ObservableCollection<string> PreviewItems { get; } = [];
    public ObservableCollection<string> CreatedFiles { get; } = [];

    public void BindRepo(string repoPath)
    {
        RepoPath = repoPath;
        RefreshPreview();
    }

    partial void OnRepoPathChanged(string value) => RefreshPreview();

    partial void OnForceChanged(bool value) => RefreshPreview();

    private void RefreshPreview()
    {
        PreviewItems.Clear();
        if (string.IsNullOrWhiteSpace(RepoPath) || !Directory.Exists(RepoPath))
        {
            Message = "Select a valid repository path.";
            return;
        }

        var agentDir = Path.Combine(RepoPath, AgentWikiConstants.ConfigDirectoryName);
        var promptsDir = Path.Combine(agentDir, "prompts");
        AddPreview(
            Path.Combine(agentDir, AgentWikiConstants.ConfigFileName),
            ".agentwiki/config.json");
        AddPreview(Path.Combine(RepoPath, ".env.example"), ".env.example");
        AddPreview(Path.Combine(agentDir, ".gitignore"), ".agentwiki/.gitignore");

        foreach (var name in new[]
                 {
                     "SystemPrompt.txt",
                     "ArchitectureOverviewPrompt.txt",
                     "ModulePlanPrompt.txt",
                     "ModuleAnalysisPrompt.txt",
                     "CrossCuttingPrompt.txt",
                     "CrossLinkValidationPrompt.txt"
                 })
        {
            AddPreview(Path.Combine(promptsDir, name), $".agentwiki/prompts/{name}");
        }

        Message = Force
            ? "Force enabled — existing scaffold files will be overwritten."
            : "Existing files will be left alone unless Force is enabled.";
    }

    private void AddPreview(string absolute, string relative)
    {
        var exists = File.Exists(absolute);
        var action = !exists
            ? "create"
            : Force
                ? "overwrite"
                : "skip (exists)";
        PreviewItems.Add($"{action}: {relative}");
    }

    [RelayCommand]
    private async Task RunInitAsync()
    {
        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            return;
        }

        try
        {
            IsRunning = true;
            CreatedFiles.Clear();
            var resolved = PathResolver.ResolveRepo(RepoPath);
            var result = await initService.InitializeAsync(resolved, Force).ConfigureAwait(true);
            foreach (var file in result.FilesCreated)
            {
                CreatedFiles.Add(file);
            }

            RefreshPreview();
            // Set after preview so user sees the init outcome (preview only updates scaffolding hints).
            Message = result.Success ? result.Message : (result.Error ?? result.Message);
        }
        catch (Exception ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
