using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Constants;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// JSON file store for <c>.agentwiki/last-run.json</c>.
/// </summary>
public sealed class LastRunStore(ILogger<LastRunStore> logger) : ILastRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<LastRunState?> LoadAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        var path = GetPath(repoPath);
        if (!File.Exists(path))
        {
            logger.LogDebug("No last-run file at {Path}", path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer
            .DeserializeAsync<LastRunState>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        logger.LogDebug("Loaded last-run state commit={Commit} at {Timestamp}", state?.CommitSha, state?.TimestampUtc);
        return state;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string repoPath, LastRunState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = GetPath(repoPath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Wrote last-run state to {Path} (commit={Commit})", path, state.CommitSha);
    }

    public static string GetPath(string repoPath) =>
        Path.Combine(Path.GetFullPath(repoPath), AgentWikiConstants.ConfigDirectoryName, AgentWikiConstants.LastRunFileName);
}
