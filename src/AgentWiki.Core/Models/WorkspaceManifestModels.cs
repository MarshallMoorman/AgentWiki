namespace AgentWiki.Core.Models;

/// <summary>
/// Parsed human-owned workspace contribution manifest
/// (<c>docs/wiki/workspace-manifest.md</c> by default).
/// </summary>
public sealed class WorkspaceManifestDocument
{
    /// <summary>Absolute path of the manifest file when loaded from disk.</summary>
    public string? SourcePath { get; init; }

    /// <summary>True when a file was found and parsed (may still have empty fields).</summary>
    public bool Present { get; init; }

    /// <summary>Raw free-form text under Purpose (documentation; optional for tooling).</summary>
    public string? Purpose { get; init; }

    /// <summary>Raw free-form text under Maintenance rules.</summary>
    public string? MaintenanceRules { get; init; }

    /// <summary>Layer token (experience | process | domain | …).</summary>
    public string? Layer { get; init; }

    /// <summary>Team display string (e.g. @team-name).</summary>
    public string? Team { get; init; }

    /// <summary>Applications / services owned by this repo.</summary>
    public IReadOnlyList<WorkspaceManifestApplication> Applications { get; init; } = [];

    /// <summary>Normalized brand tokens (Rise, Shine, Elastic, Blueprint + unknowns preserved).</summary>
    public IReadOnlyList<string> Brands { get; init; } = [];

    public IReadOnlyList<string> Responsibilities { get; init; } = [];

    public IReadOnlyList<string> RouteWhen { get; init; } = [];

    public IReadOnlyList<string> DoNotRouteWhen { get; init; } = [];

    public IReadOnlyList<string> RelatedSystems { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Free-form Markdown under Additional context (always ingested into routing cards).</summary>
    public string? AdditionalContext { get; init; }

    /// <summary>Parse / quality warnings (missing sections, unknown brands, etc.).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>A single application or service entry from the manifest.</summary>
public sealed class WorkspaceManifestApplication
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>Result of scaffolding a missing workspace-manifest.md.</summary>
public sealed class WorkspaceManifestScaffoldResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public bool Created { get; init; }
    public bool SkippedExisting { get; init; }
    public bool DryRun { get; init; }

    public static WorkspaceManifestScaffoldResult CreatedOk(string path, bool dryRun) =>
        new()
        {
            Success = true,
            Created = !dryRun,
            SkippedExisting = false,
            DryRun = dryRun,
            Path = path,
            Message = dryRun
                ? $"[dry-run] Would scaffold workspace contribution manifest at {path}"
                : $"Scaffolded workspace contribution manifest at {path}"
        };

    public static WorkspaceManifestScaffoldResult AlreadyExists(string path) =>
        new()
        {
            Success = true,
            Created = false,
            SkippedExisting = true,
            Path = path,
            Message = $"Workspace contribution manifest already exists at {path} (not overwritten)."
        };

    public static WorkspaceManifestScaffoldResult Fail(string error) =>
        new()
        {
            Success = false,
            Message = error
        };
}
