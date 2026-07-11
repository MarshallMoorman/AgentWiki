namespace AgentWiki.Core.Models;

/// <summary>
/// Result of optional static analysis (Roslyn syntax walk or graceful skip).
/// </summary>
public sealed class StaticAnalysisResult
{
    public bool Enabled { get; init; }
    public bool Succeeded { get; init; }
    public bool UsedRoslyn { get; init; }
    public string Summary { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<AnalyzedProject> Projects { get; init; } = [];
    public IReadOnlyList<TypeSymbolInfo> PublicTypes { get; init; } = [];
    public IReadOnlyList<EndpointInfo> Endpoints { get; init; } = [];
    public IReadOnlyList<string> EntryPoints { get; init; } = [];
    public IReadOnlyList<string> DiRegistrations { get; init; } = [];
    public IReadOnlyList<string> ObsoleteSymbols { get; init; } = [];
    public int FilesAnalyzed { get; init; }
    public TimeSpan Duration { get; init; }

    public static StaticAnalysisResult Skipped(string reason) => new()
    {
        Enabled = false,
        Succeeded = true,
        UsedRoslyn = false,
        Summary = reason
    };

    public static StaticAnalysisResult Empty(string reason) => new()
    {
        Enabled = true,
        Succeeded = true,
        UsedRoslyn = false,
        Summary = reason
    };
}

/// <summary>Per-project rollup from static analysis.</summary>
public sealed class AnalyzedProject
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Kind { get; init; } = "library";
    public IReadOnlyList<string> EntryPoints { get; init; } = [];
    public int PublicTypeCount { get; init; }
    public int EndpointCount { get; init; }
}

/// <summary>A type discovered via syntax analysis.</summary>
public sealed class TypeSymbolInfo
{
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "class";
    public string RelativePath { get; init; } = "";
    public string? Namespace { get; init; }
    public bool IsPublic { get; init; }
    public IReadOnlyList<string> Attributes { get; init; } = [];
    public string? ProjectName { get; init; }
}

/// <summary>
/// HTTP / Function endpoint discovered from controllers, minimal APIs, or Azure Functions.
/// </summary>
public sealed class EndpointInfo
{
    public string HttpMethod { get; init; } = "";
    public string Route { get; init; } = "";
    public string HandlerName { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string Kind { get; init; } = "controller";
    public IReadOnlyList<string> AuthHints { get; init; } = [];
    public IReadOnlyList<string> Parameters { get; init; } = [];
    public string? ProjectName { get; init; }

    /// <summary>Optional human/LLM description of purpose.</summary>
    public string? Description { get; set; }

    /// <summary>Display key for dedupe / enrichment matching.</summary>
    public string Key => $"{HttpMethod.ToUpperInvariant()} {Route} → {HandlerName}";
}
