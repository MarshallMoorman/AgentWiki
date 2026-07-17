namespace AgentWiki.Core.Generation;

/// <summary>Classifies existing AGENTS.md content for full-generate vs bootstrap-block-only.</summary>
public static class AgentsMdFileClassifier
{
    public static bool IsMissingOrTrivial(string? absolutePath, int trivialMaxLength)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return true;
        }

        try
        {
            var content = File.ReadAllText(absolutePath);
            return IsTrivialContent(content, trivialMaxLength);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsTrivialContent(string content, int trivialMaxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        var trimmed = content.Trim();
        if (trimmed.Length < Math.Max(40, trivialMaxLength))
        {
            return true;
        }

        // Bootstrap-only shell: AgentWiki marker block with little or no surrounding content.
        if (ContainsAgentWikiBlock(trimmed))
        {
            var start = trimmed.IndexOf(Constants.AgentsMd.MarkerBegin, StringComparison.Ordinal);
            var end = trimmed.IndexOf(Constants.AgentsMd.MarkerEnd, StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                end += Constants.AgentsMd.MarkerEnd.Length;
                var outside = (trimmed[..start] + trimmed[end..]).Trim();
                // Only the marker block (or a tiny heading around it) → treat as missing/trivial.
                if (outside.Length < trivialMaxLength)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool ContainsAgentWikiBlock(string content) =>
        content.Contains(Constants.AgentsMd.MarkerBegin, StringComparison.Ordinal)
        && content.Contains(Constants.AgentsMd.MarkerEnd, StringComparison.Ordinal);
}
