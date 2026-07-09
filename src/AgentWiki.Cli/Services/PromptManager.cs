using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using AgentWiki.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Loads prompt templates from embedded resources, optional on-disk overrides,
/// and applies <c>{{Variable}}</c> substitution.
/// </summary>
public sealed partial class PromptManager : IPromptManager
{
    private static readonly Regex VariablePattern = VariableRegex();
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PromptManager> _logger;
    private readonly string? _repoPromptsDirectory;

    public PromptManager(ILogger<PromptManager> logger)
        : this(logger, repoPromptsDirectory: null)
    {
    }

    public PromptManager(ILogger<PromptManager> logger, string? repoPromptsDirectory)
    {
        _logger = logger;
        _repoPromptsDirectory = repoPromptsDirectory;
    }

    /// <inheritdoc />
    public string GetPrompt(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _cache.GetOrAdd(name, LoadPrompt);
    }

    /// <inheritdoc />
    public string Render(string name, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var template = GetPrompt(name);
        return VariablePattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Creates a prompt manager that prefers <c>.agentwiki/prompts</c> under the repo root.
    /// </summary>
    public static PromptManager ForRepository(string repoPath, ILogger<PromptManager> logger)
    {
        var dir = Path.Combine(Path.GetFullPath(repoPath), ".agentwiki", "prompts");
        return new PromptManager(logger, Directory.Exists(dir) ? dir : null);
    }

    private string LoadPrompt(string name)
    {
        var fileName = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? name
            : name + ".txt";

        if (!string.IsNullOrWhiteSpace(_repoPromptsDirectory))
        {
            var diskPath = Path.Combine(_repoPromptsDirectory, fileName);
            if (File.Exists(diskPath))
            {
                _logger.LogDebug("Loaded prompt {Name} from {Path}", name, diskPath);
                return File.ReadAllText(diskPath);
            }
        }

        var assembly = typeof(PromptManager).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n =>
                n.EndsWith($".Prompts.{fileName}", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith($".{fileName}", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Unable to open embedded prompt '{resourceName}'.");
            using var reader = new StreamReader(stream);
            _logger.LogDebug("Loaded prompt {Name} from embedded resource {Resource}", name, resourceName);
            return reader.ReadToEnd();
        }

        // Last resort: prompts next to the executable (published layout).
        var basePath = Path.Combine(AppContext.BaseDirectory, "Prompts", fileName);
        if (File.Exists(basePath))
        {
            return File.ReadAllText(basePath);
        }

        throw new FileNotFoundException(
            $"Prompt template '{name}' was not found as an embedded resource, repo override, or file at '{basePath}'.");
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex VariableRegex();
}
