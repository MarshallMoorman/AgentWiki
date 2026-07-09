namespace AgentWiki.Core.Models;

/// <summary>
/// A logical section of the generated wiki (maps to one or more Markdown files).
/// </summary>
public sealed record WikiSection(
    string Id,
    string Title,
    string RelativePath,
    string Content,
    IReadOnlyList<string>? RelatedFilePaths = null);
