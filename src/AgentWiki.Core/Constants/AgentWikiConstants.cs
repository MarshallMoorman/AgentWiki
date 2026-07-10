namespace AgentWiki.Core.Constants;

/// <summary>
/// Shared constants for the AgentWiki tool.
/// </summary>
public static class AgentWikiConstants
{
    public const string ToolName = "agent-wiki";
    public const string ProductName = "AgentWiki";
    public const string Version = "1.0.9";

    public const string ConfigDirectoryName = ".agentwiki";
    public const string ConfigFileName = "config.json";
    public const string LastRunFileName = "last-run.json";
    public const string MetaFileName = ".agentwiki-meta.json";

    public const string DefaultOutputPath = "docs/wiki";
    public const string DefaultAgentMdPath = "AGENTS.md";
    public const string DefaultModel = "gpt-4o";
    public const string DefaultProvider = "azure-openai";

    /// <summary>Environment variable prefix for configuration overrides.</summary>
    public const string EnvironmentVariablePrefix = "AGENTWIKI_";

    /// <summary>Marker used by AgentBootstrapper for idempotent AGENTS.md updates.</summary>
    public const string AgentsMdMarkerBegin = "<!-- BEGIN AGENTWIKI -->";
    public const string AgentsMdMarkerEnd = "<!-- END AGENTWIKI -->";
}
