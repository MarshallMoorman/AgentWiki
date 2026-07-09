namespace AgentWiki.Core.Models;

/// <summary>
/// Parameters for a full or incremental wiki generation run.
/// </summary>
public sealed class WikiGenerationRequest
{
    /// <summary>Resolved configuration for this run.</summary>
    public required AgentWikiConfig Config { get; init; }

    /// <summary>Absolute path to the repository root.</summary>
    public required string RepoPath { get; init; }

    /// <summary>Absolute path to the wiki output directory.</summary>
    public required string OutputPath { get; init; }

    /// <summary>When true, overwrite existing wiki content without interactive confirmation.</summary>
    public bool Force { get; init; }

    /// <summary>When true, analyze and report without writing files.</summary>
    public bool DryRun { get; init; }

    /// <summary>When true, only regenerate sections impacted by recent changes.</summary>
    public bool Incremental { get; init; }

    /// <summary>Optional model override from the CLI.</summary>
    public string? ModelOverride { get; init; }

    /// <summary>Optional provider override from the CLI.</summary>
    public string? ProviderOverride { get; init; }

    /// <summary>Correlation id for structured logging of this run.</summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}
