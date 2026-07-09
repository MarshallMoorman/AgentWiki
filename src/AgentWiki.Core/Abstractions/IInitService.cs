namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Scaffolds AgentWiki configuration and sample assets in a repository.
/// </summary>
public interface IInitService
{
    /// <summary>
    /// Initializes AgentWiki in the target repository.
    /// </summary>
    /// <param name="repoPath">Repository root.</param>
    /// <param name="force">Overwrite existing config when true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paths of files created or updated.</returns>
    Task<InitResult> InitializeAsync(
        string repoPath,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of an <c>agent-wiki init</c> run.</summary>
public sealed class InitResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> FilesCreated { get; init; } = [];
    public string? Error { get; init; }

    public static InitResult Ok(string message, IReadOnlyList<string> files) =>
        new() { Success = true, Message = message, FilesCreated = files };

    public static InitResult Fail(string error) =>
        new() { Success = false, Message = "Initialization failed.", Error = error };
}
