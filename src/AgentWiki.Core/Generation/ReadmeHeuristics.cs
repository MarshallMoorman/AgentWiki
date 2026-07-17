namespace AgentWiki.Core.Generation;

/// <summary>
/// Detects missing or generic/template README.md content that is safe to replace.
/// </summary>
public static class ReadmeHeuristics
{
    private static readonly string[] GenericMarkers =
    [
        "TODO",
        "Your Project",
        "Replace this",
        "dotnet new",
        "Visual Studio",
        "ASP.NET Core Web API",
        "Getting Started with",
        "This is a sample",
        "This template",
        "Create a new project",
        "INSERT DESCRIPTION",
        "Project Description goes here",
        "Add a brief description",
        "Lorem ipsum"
    ];

    /// <summary>
    /// Returns true when the README is missing, empty, short, or looks like a stock template.
    /// </summary>
    public static bool IsMissingOrGeneric(string? absolutePath, int genericMaxLength)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return true;
        }

        string content;
        try
        {
            content = File.ReadAllText(absolutePath);
        }
        catch
        {
            return false; // unreadable existing file → do not overwrite
        }

        return IsGenericContent(content, genericMaxLength);
    }

    /// <summary>Pure content check (unit-testable).</summary>
    public static bool IsGenericContent(string content, int genericMaxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        var trimmed = content.Trim();
        // Short files are treated as empty/template (threshold from config, typically ~500).
        if (trimmed.Length < genericMaxLength)
        {
            return true;
        }

        foreach (var marker in GenericMarkers)
        {
            if (trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Prior AgentWiki offline stub — safe to regenerate with a richer template.
        if (trimmed.Contains("AgentWiki config (if used)", StringComparison.OrdinalIgnoreCase)
            && trimmed.Contains("Default model/provider (AgentWiki)", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("AgentWiki default model/provider", StringComparison.OrdinalIgnoreCase)
               && trimmed.Contains("software project with approximately", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Very few headings and almost no path/code-ish tokens → likely empty template.
        var headingCount = trimmed.Split('\n').Count(l => l.TrimStart().StartsWith('#'));
        var hasCodeFence = trimmed.Contains("```", StringComparison.Ordinal);
        var hasLikelyProjectToken = trimmed.Contains(".sln", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains(".csproj", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("dotnet ", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("npm ", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("package.json", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("docker", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("AGENTS.md", StringComparison.OrdinalIgnoreCase)
                                    || trimmed.Contains("docs/wiki", StringComparison.OrdinalIgnoreCase);

        if (headingCount <= 1 && !hasCodeFence && !hasLikelyProjectToken && trimmed.Length < genericMaxLength * 2)
        {
            return true;
        }

        return false;
    }
}
