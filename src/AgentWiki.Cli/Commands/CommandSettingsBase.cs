using System.ComponentModel;
using Spectre.Console.Cli;

namespace AgentWiki.Cli.Commands;

/// <summary>
/// Shared CLI options available on most AgentWiki commands.
/// </summary>
public class CommandSettingsBase : CommandSettings
{
    [CommandOption("-r|--repo-path <PATH>")]
    [Description("Path to the repository root (default: current directory)")]
    [DefaultValue(".")]
    public string RepoPath { get; init; } = ".";

    [CommandOption("-c|--config <PATH>")]
    [Description("Optional path to an AgentWiki config JSON file")]
    public string? ConfigPath { get; init; }

    [CommandOption("--verbose")]
    [Description("Enable verbose (debug) logging to console and file")]
    [DefaultValue(false)]
    public bool Verbose { get; init; }
}

/// <summary>
/// Settings shared by generate / update commands.
/// </summary>
public class GenerationSettings : CommandSettingsBase
{
    [CommandOption("-o|--output <PATH>")]
    [Description("Wiki output path relative to the repo (default: docs/wiki)")]
    public string? OutputPath { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    [Description("LLM model or Azure OpenAI deployment name")]
    public string? Model { get; init; }

    [CommandOption("--provider <PROVIDER>")]
    [Description("LLM provider: azure-openai | openai | github-models")]
    public string? Provider { get; init; }

    [CommandOption("--force")]
    [Description("Overwrite existing wiki output without confirmation")]
    [DefaultValue(false)]
    public bool Force { get; init; }

    [CommandOption("--dry-run")]
    [Description("Analyze and report without writing files")]
    [DefaultValue(false)]
    public bool DryRun { get; init; }
}
