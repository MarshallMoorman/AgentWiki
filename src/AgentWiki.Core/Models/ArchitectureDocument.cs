using System.Text.Json.Serialization;

namespace AgentWiki.Core.Models;

/// <summary>
/// Structured architecture content produced by the LLM (or offline fallback).
/// Serialized as JSON for reliable structured-output parsing.
/// </summary>
public sealed class ArchitectureDocument
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Architecture Overview";

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("systemContext")]
    public string SystemContext { get; set; } = "";

    [JsonPropertyName("layers")]
    public List<ArchitectureLayer> Layers { get; set; } = [];

    [JsonPropertyName("keyComponents")]
    public List<ArchitectureComponent> KeyComponents { get; set; } = [];

    [JsonPropertyName("dataFlows")]
    public List<string> DataFlows { get; set; } = [];

    [JsonPropertyName("decisions")]
    public List<string> Decisions { get; set; } = [];

    [JsonPropertyName("gotchas")]
    public List<string> Gotchas { get; set; } = [];

    [JsonPropertyName("howToExtend")]
    public List<string> HowToExtend { get; set; } = [];

    [JsonPropertyName("mermaidDiagram")]
    public string? MermaidDiagram { get; set; }

    /// <summary>True when content was produced without calling an LLM.</summary>
    [JsonIgnore]
    public bool UsedOfflineFallback { get; set; }

    /// <summary>Optional token usage from the LLM call.</summary>
    [JsonIgnore]
    public TokenUsage? TokenUsage { get; set; }
}

/// <summary>A logical architectural layer.</summary>
public sealed class ArchitectureLayer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("responsibility")]
    public string Responsibility { get; set; } = "";

    [JsonPropertyName("keyPaths")]
    public List<string> KeyPaths { get; set; } = [];
}

/// <summary>A notable component, service, or module.</summary>
public sealed class ArchitectureComponent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("purpose")]
    public string Purpose { get; set; } = "";
}

/// <summary>Approximate token accounting for a generation step.</summary>
public sealed class TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
}
