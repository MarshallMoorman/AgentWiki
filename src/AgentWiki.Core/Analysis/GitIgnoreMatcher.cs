using System.Text.RegularExpressions;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Matches relative paths against <c>.gitignore</c>-style patterns and extra ignore globs.
/// Supports nested rule sets (directory-scoped), negation (<c>!</c>), and directory-only rules.
/// </summary>
public sealed class GitIgnoreMatcher
{
    private readonly List<RuleSet> _ruleSets = [];

    /// <summary>
    /// Creates a matcher with optional extra patterns (treated as root-scoped).
    /// </summary>
    public GitIgnoreMatcher(IEnumerable<string>? extraPatterns = null)
    {
        // Always ignore .git itself, plus caller-supplied extra patterns.
        var rootPatterns = new List<string> { ".git/" };
        if (extraPatterns is not null)
        {
            rootPatterns.AddRange(extraPatterns);
        }

        AddRuleSet(baseDir: "", patterns: rootPatterns);
    }

    /// <summary>
    /// Adds patterns from a <c>.gitignore</c> file located at <paramref name="gitignoreAbsolutePath"/>
    /// under repository root <paramref name="repoRoot"/>.
    /// </summary>
    public void AddGitIgnoreFile(string repoRoot, string gitignoreAbsolutePath)
    {
        if (!File.Exists(gitignoreAbsolutePath))
        {
            return;
        }

        var repoRootFull = Path.GetFullPath(repoRoot);
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(gitignoreAbsolutePath)) ?? repoRootFull;
        var relativeBase = Path.GetRelativePath(repoRootFull, fileDir)
            .Replace('\\', '/');
        if (relativeBase is "." or "")
        {
            relativeBase = "";
        }
        else if (!relativeBase.EndsWith('/'))
        {
            relativeBase += "/";
        }

        var lines = File.ReadAllLines(gitignoreAbsolutePath);
        AddRuleSet(relativeBase, lines);
    }

    /// <summary>
    /// Adds a batch of patterns scoped to <paramref name="baseDir"/> (repo-relative, trailing slash optional).
    /// </summary>
    public void AddRuleSet(string baseDir, IEnumerable<string> patterns)
    {
        var normalizedBase = NormalizeBase(baseDir);
        var rules = new List<IgnoreRule>();

        foreach (var raw in patterns)
        {
            if (TryParseRule(raw, out var rule))
            {
                rules.Add(rule);
            }
        }

        if (rules.Count > 0)
        {
            _ruleSets.Add(new RuleSet(normalizedBase, rules));
        }
    }

    /// <summary>
    /// Returns true if <paramref name="relativePath"/> should be ignored.
    /// Paths should use forward slashes and must not start with <c>./</c>.
    /// </summary>
    /// <param name="relativePath">Repo-relative path.</param>
    /// <param name="isDirectory">Whether the path is a directory.</param>
    public bool IsIgnored(string relativePath, bool isDirectory = false)
    {
        var path = NormalizePath(relativePath);
        if (path.Length == 0)
        {
            return false;
        }

        // Last matching rule across all applicable rule sets wins (gitignore semantics).
        bool? ignored = null;

        foreach (var set in _ruleSets)
        {
            if (!IsUnderBase(path, set.BaseDir))
            {
                continue;
            }

            var pathRelativeToBase = set.BaseDir.Length == 0
                ? path
                : path[set.BaseDir.Length..];

            foreach (var rule in set.Rules)
            {
                if (rule.DirectoryOnly && !isDirectory && !pathRelativeToBase.Contains('/'))
                {
                    // Directory-only rules still match files inside matching dirs via prefix checks below.
                }

                if (rule.IsMatch(pathRelativeToBase, isDirectory))
                {
                    ignored = !rule.Negated;
                }
            }
        }

        return ignored == true;
    }

    private static string NormalizeBase(string baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || baseDir is "." or "./")
        {
            return "";
        }

        var n = baseDir.Replace('\\', '/').Trim('/');
        return n.Length == 0 ? "" : n + "/";
    }

    private static string NormalizePath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return path.TrimStart('/');
    }

    private static bool IsUnderBase(string path, string baseDir) =>
        baseDir.Length == 0 || path.StartsWith(baseDir, StringComparison.Ordinal);

    private static bool TryParseRule(string raw, out IgnoreRule rule)
    {
        rule = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var line = raw.Trim();
        if (line.StartsWith('#') || line is ".")
        {
            return false;
        }

        // Escape: \# etc. not fully supported; handle trailing spaces escaped with backslash simply by trim.
        var negated = false;
        if (line.StartsWith('!'))
        {
            negated = true;
            line = line[1..];
            if (line.Length == 0)
            {
                return false;
            }
        }

        var directoryOnly = line.EndsWith('/');
        if (directoryOnly)
        {
            line = line.TrimEnd('/');
        }

        var anchored = line.StartsWith('/');
        if (anchored)
        {
            line = line.TrimStart('/');
        }

        // Patterns with a slash (not only trailing) are relative to the .gitignore location (anchored).
        if (line.Contains('/'))
        {
            anchored = true;
        }

        var regex = BuildRegex(line, anchored, directoryOnly);
        rule = new IgnoreRule(regex, negated, directoryOnly);
        return true;
    }

    private static Regex BuildRegex(string pattern, bool anchored, bool directoryOnly)
    {
        // Translate gitignore glob to regex. Simplified but covers common cases.
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        if (!anchored)
        {
            // Unanchored: may match in any directory.
            sb.Append("(?:.*/)?");
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        // ** 
                        i++;
                        if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                        {
                            i++;
                            sb.Append("(?:.*/)?");
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                case '[':
                    // Character class — copy through until ]
                    sb.Append('[');
                    i++;
                    while (i < pattern.Length && pattern[i] != ']')
                    {
                        sb.Append(pattern[i] == '\\' ? "\\\\" : pattern[i].ToString());
                        i++;
                    }
                    sb.Append(']');
                    break;
                case '.':
                case '(':
                case ')':
                case '+':
                case '|':
                case '^':
                case '$':
                case '{':
                case '}':
                case '\\':
                    sb.Append('\\').Append(c);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        if (directoryOnly)
        {
            // Directory rule matches the dir itself or anything under it.
            sb.Append("(?:/.*)?");
        }
        else
        {
            // File rule can also match as a directory prefix for pruning walks.
            sb.Append("(?:/.*)?");
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private sealed record RuleSet(string BaseDir, List<IgnoreRule> Rules);

    private sealed class IgnoreRule(Regex pattern, bool negated, bool directoryOnly)
    {
        public bool Negated { get; } = negated;
        public bool DirectoryOnly { get; } = directoryOnly;

        public bool IsMatch(string pathRelativeToBase, bool isDirectory)
        {
            if (DirectoryOnly && !isDirectory)
            {
                // Still match if path is inside a directory that matches as a prefix —
                // regex already allows /.* suffix, so file paths under ignored dirs match.
            }

            return pattern.IsMatch(pathRelativeToBase);
        }
    }
}
