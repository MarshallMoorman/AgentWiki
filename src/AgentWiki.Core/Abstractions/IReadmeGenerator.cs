using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Generates or replaces a generic <c>README.md</c> from repository analysis.
/// Never overwrites a non-generic README unless <see cref="ReadmeGenerationRequest.Force"/> is set.
/// </summary>
public interface IReadmeGenerator
{
    Task<ReadmeGenerationResult> GenerateAsync(
        ReadmeGenerationRequest request,
        CancellationToken cancellationToken = default);
}
