using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>Helpers for combining token usage across pipeline steps.</summary>
public static class TokenUsageMath
{
    public static TokenUsage Sum(params TokenUsage?[] usages)
    {
        var input = 0;
        var output = 0;
        foreach (var usage in usages)
        {
            if (usage is null)
            {
                continue;
            }

            input += usage.InputTokens;
            output += usage.OutputTokens;
        }

        return new TokenUsage { InputTokens = input, OutputTokens = output };
    }

    public static TokenUsage Add(TokenUsage? a, TokenUsage? b) => Sum(a, b);
}
