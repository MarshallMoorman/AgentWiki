using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Optional run telemetry sink (e.g. Application Insights). No-op when not configured.
/// </summary>
public interface IRunTelemetry
{
    /// <summary>Whether a real sink is configured (vs no-op).</summary>
    bool IsEnabled { get; }

    /// <summary>Track a completed generation/update run (success or failure).</summary>
    void TrackRun(WikiGenerationRequest request, GenerationResult result);
}
