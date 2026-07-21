using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWiki.Core;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// JSON store for workspace-level last-run state
/// (<c>.agentwiki/workspace-last-run.json</c>).
/// </summary>
public sealed class WorkspaceLastRunStore(ILogger<WorkspaceLastRunStore> logger) : IWorkspaceLastRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<WorkspaceLastRunState?> LoadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(workspaceRoot);
        if (!File.Exists(path))
        {
            logger.LogDebug("No workspace last-run file at {Path}", path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer
            .DeserializeAsync<WorkspaceLastRunState>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        logger.LogDebug(
            "Loaded workspace last-run at {Timestamp} members={Count}",
            state?.TimestampUtc,
            state?.Members.Count ?? 0);
        return state;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string workspaceRoot,
        WorkspaceLastRunState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = GetPath(workspaceRoot);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Wrote workspace last-run state to {Path}", path);
    }

    public static string GetPath(string workspaceRoot) =>
        Path.Combine(
            PathUtility.ExpandAndResolve(workspaceRoot),
            Constants.Paths.ConfigDirectoryName,
            Constants.Paths.WorkspaceLastRunFileName);
}
