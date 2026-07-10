namespace AgentWiki.Core.Analysis;

/// <summary>
/// Helpers for preparing text sent to LLMs.
/// </summary>
public static class PromptText
{
    /// <summary>
    /// Truncates <paramref name="text"/> to at most <paramref name="maxChars"/> characters,
    /// appending a clear marker when truncated.
    /// </summary>
    public static string TruncateForLlm(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars)
        {
            return text;
        }

        // Leave room for the truncation notice.
        const string notice = "\n\n…[truncated for LLM prompt size limit]…\n";
        var keep = Math.Max(0, maxChars - notice.Length);
        return text[..keep] + notice;
    }
}
