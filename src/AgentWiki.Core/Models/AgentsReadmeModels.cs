namespace AgentWiki.Core.Models;

/// <summary>Discovered instruction file to merge into AGENTS.md.</summary>
public sealed class InstructionSource
{
    public required string RelativePath { get; init; }
    public required string AbsolutePath { get; init; }
    public required string Content { get; init; }

    /// <summary>When true, delete this file after a successful AGENTS.md write (if migration enabled).</summary>
    public bool DeleteAfterMigration { get; init; }
}

/// <summary>Request for full AGENTS.md generation.</summary>
public sealed class AgentsMdGenerationRequest
{
    public required AgentWikiConfig Config { get; init; }
    public required string RepoPath { get; init; }

    /// <summary>Absolute wiki output directory (may or may not exist yet).</summary>
    public string? WikiOutputPath { get; init; }

    /// <summary>Pre-computed analysis; when null the generator analyzes the repo.</summary>
    public RepoAnalysisResult? Analysis { get; init; }

    /// <summary>When true, overwrite a substantial existing AGENTS.md.</summary>
    public bool Force { get; init; }

    public bool DryRun { get; init; }

    public string? ModelOverride { get; init; }
    public string? ProviderOverride { get; init; }

    public IProgress<string>? Progress { get; init; }
}

/// <summary>Outcome of full AGENTS.md generation.</summary>
public sealed class AgentsMdGenerationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? TargetPath { get; init; }
    public string? Content { get; init; }
    public AgentsMdAction Action { get; init; }
    public bool DryRun { get; init; }
    public IReadOnlyList<string> MigratedFrom { get; init; } = [];
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];
    public IReadOnlyList<string> WouldDeleteFiles { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Error { get; init; }
    public bool UsedOfflineFallback { get; init; } = true;

    public static AgentsMdGenerationResult Ok(
        string message,
        string targetPath,
        string content,
        AgentsMdAction action,
        bool dryRun = false,
        IReadOnlyList<string>? migratedFrom = null,
        IReadOnlyList<string>? deleted = null,
        IReadOnlyList<string>? wouldDelete = null,
        IReadOnlyList<string>? warnings = null,
        bool usedOfflineFallback = true) =>
        new()
        {
            Success = true,
            Message = message,
            TargetPath = targetPath,
            Content = content,
            Action = action,
            DryRun = dryRun,
            MigratedFrom = migratedFrom ?? [],
            DeletedFiles = deleted ?? [],
            WouldDeleteFiles = wouldDelete ?? [],
            Warnings = warnings ?? [],
            UsedOfflineFallback = usedOfflineFallback
        };

    public static AgentsMdGenerationResult Fail(string error, IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Success = false,
            Message = "AGENTS.md generation failed.",
            Error = error,
            Warnings = warnings ?? []
        };

    public static AgentsMdGenerationResult Skipped(string message, string? targetPath = null) =>
        new()
        {
            Success = true,
            Message = message,
            TargetPath = targetPath,
            Action = AgentsMdAction.Skipped
        };
}

public enum AgentsMdAction
{
    Created,
    Updated,
    Unchanged,
    Skipped
}

/// <summary>Request for README.md generation.</summary>
public sealed class ReadmeGenerationRequest
{
    public required AgentWikiConfig Config { get; init; }
    public required string RepoPath { get; init; }
    public string? WikiOutputPath { get; init; }
    public RepoAnalysisResult? Analysis { get; init; }
    public bool Force { get; init; }
    public bool DryRun { get; init; }
    public IProgress<string>? Progress { get; init; }
}

/// <summary>Outcome of README.md generation.</summary>
public sealed class ReadmeGenerationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? TargetPath { get; init; }
    public string? Content { get; init; }
    public ReadmeAction Action { get; init; }
    public bool DryRun { get; init; }
    public bool WasGeneric { get; init; }
    public string? Error { get; init; }

    public static ReadmeGenerationResult Ok(
        string message,
        string targetPath,
        string content,
        ReadmeAction action,
        bool dryRun = false,
        bool wasGeneric = false) =>
        new()
        {
            Success = true,
            Message = message,
            TargetPath = targetPath,
            Content = content,
            Action = action,
            DryRun = dryRun,
            WasGeneric = wasGeneric
        };

    public static ReadmeGenerationResult Fail(string error) =>
        new()
        {
            Success = false,
            Message = "README generation failed.",
            Error = error
        };

    public static ReadmeGenerationResult Skipped(string message, string? targetPath = null) =>
        new()
        {
            Success = true,
            Message = message,
            TargetPath = targetPath,
            Action = ReadmeAction.Skipped
        };
}

public enum ReadmeAction
{
    Created,
    ReplacedGeneric,
    Unchanged,
    Skipped
}
