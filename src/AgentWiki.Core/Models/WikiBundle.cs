using System.Text.Json.Serialization;

namespace AgentWiki.Core.Models;

/// <summary>
/// Full multi-step generation output for a wiki run.
/// </summary>
public sealed class WikiBundle
{
    public required ArchitectureDocument Architecture { get; init; }
    public required ModulePlan ModulePlan { get; init; }
    public required IReadOnlyList<ModuleDocument> Modules { get; init; }
    public required IReadOnlyList<CrossCuttingDocument> CrossCutting { get; init; }
    public required IReadOnlyList<WikiSection> Sections { get; init; }
    public bool UsedOfflineFallback { get; init; }
    public TokenUsage TokenUsage { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> StepsCompleted { get; init; } = [];
}

/// <summary>
/// Result of module / bounded-context identification.
/// </summary>
public sealed class ModulePlan
{
    [JsonPropertyName("modules")]
    public List<ModuleDescriptor> Modules { get; set; } = [];

    [JsonIgnore]
    public bool UsedOfflineFallback { get; set; }

    [JsonIgnore]
    public TokenUsage? TokenUsage { get; set; }
}

/// <summary>
/// Lightweight module identity used for planning generation steps.
/// </summary>
public sealed class ModuleDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("rootPaths")]
    public List<string> RootPaths { get; set; } = [];

    [JsonPropertyName("relatedFiles")]
    public List<string> RelatedFiles { get; set; } = [];
}

/// <summary>
/// Detailed module wiki content.
/// </summary>
public sealed class ModuleDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("purpose")]
    public string Purpose { get; set; } = "";

    [JsonPropertyName("entryPoints")]
    public List<string> EntryPoints { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("keyTypes")]
    public List<string> KeyTypes { get; set; } = [];

    [JsonPropertyName("howToExtend")]
    public List<string> HowToExtend { get; set; } = [];

    [JsonPropertyName("gotchas")]
    public List<string> Gotchas { get; set; } = [];

    [JsonPropertyName("relatedFiles")]
    public List<string> RelatedFiles { get; set; } = [];

    [JsonIgnore]
    public string RelativePath => $"modules/{Id}.md";

    [JsonIgnore]
    public bool UsedOfflineFallback { get; set; }

    [JsonIgnore]
    public TokenUsage? TokenUsage { get; set; }
}

/// <summary>
/// Cross-cutting concern wiki content (auth, logging, config, etc.).
/// </summary>
public sealed class CrossCuttingDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = [];

    [JsonPropertyName("keyFiles")]
    public List<string> KeyFiles { get; set; } = [];

    [JsonPropertyName("guidance")]
    public List<string> Guidance { get; set; } = [];

    [JsonIgnore]
    public string RelativePath => $"cross-cutting/{Id}.md";

    [JsonIgnore]
    public bool UsedOfflineFallback { get; set; }

    [JsonIgnore]
    public TokenUsage? TokenUsage { get; set; }
}
