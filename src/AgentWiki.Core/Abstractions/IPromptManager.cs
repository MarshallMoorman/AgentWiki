namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Loads prompt templates and applies simple <c>{{Variable}}</c> substitution.
/// </summary>
public interface IPromptManager
{
    /// <summary>
    /// Gets a prompt by logical name (e.g. <c>SystemPrompt</c>, <c>ArchitectureOverviewPrompt</c>).
    /// </summary>
    string GetPrompt(string name);

    /// <summary>
    /// Gets a prompt and replaces <c>{{Key}}</c> placeholders using <paramref name="variables"/>.
    /// Missing keys are left as-is.
    /// </summary>
    string Render(string name, IReadOnlyDictionary<string, string> variables);
}
