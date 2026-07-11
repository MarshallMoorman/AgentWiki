namespace AgentWiki.Core.Models;

/// <summary>
/// Strictness for <see cref="Abstractions.IWikiPostProcessor"/> corrections.
/// </summary>
public enum WikiPostProcessingMode
{
    /// <summary>Rewrite/normalize where possible; keep useful content.</summary>
    Lenient = 0,

    /// <summary>Drop suspect content more aggressively (e.g. unverified deprecation claims).</summary>
    Strict = 1
}

/// <summary>
/// Context for a post-processing pass over generated wiki content.
/// </summary>
public sealed class WikiPostProcessContext
{
    /// <summary>Absolute repository root used for path relativization.</summary>
    public required string RepoRoot { get; init; }

    /// <summary>
    /// Absolute wiki output directory (for relative markdown link checks).
    /// Optional — link validation is skipped when null or missing.
    /// </summary>
    public string? WikiOutputRoot { get; init; }

    /// <summary>Processing mode (lenient by default).</summary>
    public WikiPostProcessingMode Mode { get; init; } = WikiPostProcessingMode.Lenient;

    /// <summary>
    /// True when source inventory scan found <c>[Obsolete]</c> (or similar) markers.
    /// When false, deprecation language in generated text is treated as invented.
    /// </summary>
    public bool SourceHasObsoleteMarkers { get; init; }

    /// <summary>
    /// Known wiki page relative paths (e.g. <c>architecture.md</c>, <c>modules/cli.md</c>)
    /// used for link validation.
    /// </summary>
    public IReadOnlySet<string> KnownWikiPages { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inventory relative paths for soft path existence checks (optional).</summary>
    public IReadOnlySet<string> KnownRepoPaths { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A single correction applied by the post-processor.</summary>
public sealed class WikiPostProcessCorrection
{
    public required string RuleId { get; init; }
    public required string Description { get; init; }
    public string? Field { get; init; }
    public string? Page { get; init; }
}

/// <summary>Outcome of a post-processing pass.</summary>
public sealed class WikiPostProcessResult
{
    public IReadOnlyList<WikiPostProcessCorrection> Corrections { get; init; } = [];

    public int CorrectionCount => Corrections.Count;

    public static WikiPostProcessResult Empty { get; } = new();

    public static WikiPostProcessResult From(IReadOnlyList<WikiPostProcessCorrection> corrections) =>
        new() { Corrections = corrections };

    public string Summarize(int maxItems = 8)
    {
        if (Corrections.Count == 0)
        {
            return "No post-processing corrections.";
        }

        var lines = Corrections
            .Take(maxItems)
            .Select(c =>
            {
                var scope = string.IsNullOrWhiteSpace(c.Page) ? c.Field : $"{c.Page}/{c.Field}";
                return string.IsNullOrWhiteSpace(scope)
                    ? $"- [{c.RuleId}] {c.Description}"
                    : $"- [{c.RuleId}] {scope}: {c.Description}";
            });

        var suffix = Corrections.Count > maxItems
            ? $"{Environment.NewLine}- …and {Corrections.Count - maxItems} more"
            : "";

        return $"Post-processing applied {Corrections.Count} correction(s):{Environment.NewLine}"
               + string.Join(Environment.NewLine, lines)
               + suffix;
    }
}
