namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Ensures agent bootstrap instructions exist in <c>AGENTS.md</c> / <c>CLAUDE.md</c>.
/// </summary>
public interface IAgentBootstrapper
{
    /// <summary>
    /// Creates or updates the AgentWiki instruction block idempotently.
    /// </summary>
    Task<AgentBootstrapResult> EnsureInstructionsAsync(
        string repoPath,
        string agentMdPath,
        string wikiOutputPathRelative,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an agent bootstrap operation.</summary>
public sealed class AgentBootstrapResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? TargetPath { get; init; }
    public BootstrapAction Action { get; init; }
    public string? Error { get; init; }

    public static AgentBootstrapResult Ok(string message, string targetPath, BootstrapAction action) =>
        new() { Success = true, Message = message, TargetPath = targetPath, Action = action };

    public static AgentBootstrapResult Fail(string error) =>
        new() { Success = false, Message = "Agent bootstrap failed.", Error = error };
}

/// <summary>What the bootstrapper did to the agent markdown file.</summary>
public enum BootstrapAction
{
    Created,
    Updated,
    Unchanged,
    Skipped
}
