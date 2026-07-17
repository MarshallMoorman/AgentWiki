using System.Text.RegularExpressions;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;
using AgentWiki.Core;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Default wiki post-processor: path relativization, dependency cleanup,
/// deprecation neutralization, and basic markdown link hygiene.
/// </summary>
public sealed partial class WikiPostProcessor : IWikiPostProcessor
{
    /// <inheritdoc />
    public WikiPostProcessResult ProcessArchitecture(ArchitectureDocument document, WikiPostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var corrections = new List<WikiPostProcessCorrection>();
        const string page = "architecture";

        document.Title = CleanProse(document.Title, context, corrections, page, "title");
        document.Summary = CleanProse(document.Summary, context, corrections, page, "summary");
        document.SystemContext = CleanProse(document.SystemContext, context, corrections, page, "systemContext");
        document.DataFlows = CleanStringList(document.DataFlows, context, corrections, page, "dataFlows");
        document.Decisions = CleanStringList(document.Decisions, context, corrections, page, "decisions");
        document.Gotchas = CleanStringList(document.Gotchas, context, corrections, page, "gotchas");
        document.HowToExtend = CleanStringList(document.HowToExtend, context, corrections, page, "howToExtend");

        if (!string.IsNullOrWhiteSpace(document.FullMarkdown))
        {
            document.FullMarkdown = CleanMarkdown(document.FullMarkdown, context, corrections, page, "fullMarkdown");
        }

        if (!string.IsNullOrWhiteSpace(document.MermaidDiagram))
        {
            document.MermaidDiagram = RewriteAbsolutePathsInText(
                document.MermaidDiagram,
                context.RepoRoot,
                corrections,
                page,
                "mermaidDiagram");
        }

        foreach (var layer in document.Layers)
        {
            layer.Name = CleanProse(layer.Name, context, corrections, page, "layer.name");
            layer.Responsibility = CleanProse(layer.Responsibility, context, corrections, page, "layer.responsibility");
            layer.KeyPaths = CleanPathList(layer.KeyPaths, context, corrections, page, "layer.keyPaths");
        }

        foreach (var component in document.KeyComponents)
        {
            component.Name = CleanProse(component.Name, context, corrections, page, "component.name");
            component.Purpose = CleanProse(component.Purpose, context, corrections, page, "component.purpose");
            if (!string.IsNullOrWhiteSpace(component.Path))
            {
                var cleaned = RewriteSinglePath(component.Path, context.RepoRoot, corrections, page, "component.path");
                component.Path = cleaned;
            }
        }

        return WikiPostProcessResult.From(corrections);
    }

    /// <inheritdoc />
    public WikiPostProcessResult ProcessModulePlan(ModulePlan plan, WikiPostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var corrections = new List<WikiPostProcessCorrection>();
        const string page = "module-plan";

        foreach (var module in plan.Modules)
        {
            module.RootPaths = CleanPathList(module.RootPaths, context, corrections, page, $"{module.Id}.rootPaths");
            module.RelatedFiles = CleanPathList(module.RelatedFiles, context, corrections, page, $"{module.Id}.relatedFiles");
            module.Summary = CleanProse(module.Summary, context, corrections, page, $"{module.Id}.summary");
        }

        return WikiPostProcessResult.From(corrections);
    }

    /// <inheritdoc />
    public WikiPostProcessResult ProcessModule(ModuleDocument document, WikiPostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var corrections = new List<WikiPostProcessCorrection>();
        var page = $"modules/{document.Id}";

        document.Title = CleanProse(document.Title, context, corrections, page, "title");
        document.Purpose = CleanProse(document.Purpose, context, corrections, page, "purpose");
        document.EntryPoints = CleanPathList(document.EntryPoints, context, corrections, page, "entryPoints");
        document.Dependencies = NormalizeDependencies(document.Dependencies, context, corrections, page);
        document.KeyTypes = CleanStringList(document.KeyTypes, context, corrections, page, "keyTypes");
        document.HowToExtend = CleanStringList(document.HowToExtend, context, corrections, page, "howToExtend");
        document.Gotchas = CleanStringList(document.Gotchas, context, corrections, page, "gotchas");
        document.RelatedFiles = CleanPathList(document.RelatedFiles, context, corrections, page, "relatedFiles");

        return WikiPostProcessResult.From(corrections);
    }

    /// <inheritdoc />
    public WikiPostProcessResult ProcessCrossCutting(CrossCuttingDocument document, WikiPostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var corrections = new List<WikiPostProcessCorrection>();
        var page = $"cross-cutting/{document.Id}";

        document.Title = CleanProse(document.Title, context, corrections, page, "title");
        document.Summary = CleanProse(document.Summary, context, corrections, page, "summary");
        document.Patterns = CleanStringList(document.Patterns, context, corrections, page, "patterns");
        document.KeyFiles = CleanPathList(document.KeyFiles, context, corrections, page, "keyFiles");
        document.Guidance = CleanStringList(document.Guidance, context, corrections, page, "guidance");

        return WikiPostProcessResult.From(corrections);
    }

    /// <inheritdoc />
    public (IReadOnlyList<WikiSection> Sections, WikiPostProcessResult Result) ProcessSections(
        IReadOnlyList<WikiSection> sections,
        WikiPostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(context);

        var corrections = new List<WikiPostProcessCorrection>();
        var output = new List<WikiSection>(sections.Count);

        // Enrich known pages from the section list itself.
        var known = new HashSet<string>(context.KnownWikiPages, StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            known.Add(section.RelativePath.Replace('\\', '/'));
        }

        var sectionContext = new WikiPostProcessContext
        {
            RepoRoot = context.RepoRoot,
            WikiOutputRoot = context.WikiOutputRoot,
            Mode = context.Mode,
            SourceHasObsoleteMarkers = context.SourceHasObsoleteMarkers,
            KnownWikiPages = known,
            KnownRepoPaths = context.KnownRepoPaths
        };

        foreach (var section in sections)
        {
            var content = CleanMarkdown(
                section.Content,
                sectionContext,
                corrections,
                section.RelativePath,
                "content");

            var related = section.RelatedFilePaths is null
                ? null
                : (IReadOnlyList<string>)CleanPathList(
                    section.RelatedFilePaths.ToList(),
                    sectionContext,
                    corrections,
                    section.RelativePath,
                    "relatedFiles");

            output.Add(section with { Content = content, RelatedFilePaths = related });
        }

        return (output, WikiPostProcessResult.From(corrections));
    }

    /// <summary>
    /// Lightweight scan of selected C# sources for <c>[Obsolete]</c> markers.
    /// Safe when files are missing (returns false).
    /// </summary>
    public static bool DetectObsoleteMarkers(
        string repoRoot,
        IReadOnlyList<RepoFile> files,
        int maxFiles = Constants.Analysis.ObsoleteScanMaxFiles)
    {
        if (files.Count == 0)
        {
            return false;
        }

        var candidates = files
            .Where(f => f.SelectedForAnalysis
                        && string.Equals(f.Extension, ".cs", StringComparison.OrdinalIgnoreCase))
            .Take(maxFiles);

        foreach (var file in candidates)
        {
            var path = !string.IsNullOrWhiteSpace(file.AbsolutePath) && File.Exists(file.AbsolutePath)
                ? file.AbsolutePath
                : Path.Combine(repoRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                // Small files only — avoid loading huge generated sources.
                var info = new FileInfo(path);
                if (info.Length > 512_000)
                {
                    continue;
                }

                var text = File.ReadAllText(path);
                if (text.Contains("[Obsolete", StringComparison.Ordinal)
                    || text.Contains("ObsoleteAttribute", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // ignore IO errors during soft scan
            }
        }

        return false;
    }

    public static WikiPostProcessingMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WikiPostProcessingMode.Lenient;
        }

        return value.Trim().Equals("strict", StringComparison.OrdinalIgnoreCase)
            ? WikiPostProcessingMode.Strict
            : WikiPostProcessingMode.Lenient;
    }

    private static List<string> NormalizeDependencies(
        List<string> dependencies,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page)
    {
        if (dependencies.Count == 0)
        {
            return dependencies;
        }

        var expanded = new List<string>();
        var changed = false;

        foreach (var raw in dependencies)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                changed = true;
                continue;
            }

            var item = raw.Trim();

            // Flattened object map: "customers: Customer API; shared: Shared kernel"
            if (item.Contains(';', StringComparison.Ordinal) && item.Contains(':', StringComparison.Ordinal))
            {
                var parts = item.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 1 && parts.All(p => p.Contains(':', StringComparison.Ordinal)))
                {
                    foreach (var part in parts)
                    {
                        expanded.Add(FormatDependencyPart(part));
                    }

                    changed = true;
                    continue;
                }
            }

            // Single "key: value" dependency object residue → keep as "key (value)" or path
            if (LooksLikeKeyValuePair(item) && !item.Contains('/', StringComparison.Ordinal)
                                            && !item.Contains('\\', StringComparison.Ordinal))
            {
                var formatted = FormatDependencyPart(item);
                if (!string.Equals(formatted, item, StringComparison.Ordinal))
                {
                    changed = true;
                }

                expanded.Add(formatted);
                continue;
            }

            expanded.Add(item);
        }

        var cleaned = CleanPathList(expanded, context, corrections, page, "dependencies");
        cleaned = CleanStringList(cleaned, context, corrections, page, "dependencies");

        // Deduplicate while preserving order
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cleaned)
        {
            if (seen.Add(item))
            {
                distinct.Add(item);
            }
            else
            {
                changed = true;
            }
        }

        if (changed || distinct.Count != dependencies.Count
            || !distinct.SequenceEqual(dependencies, StringComparer.Ordinal))
        {
            corrections.Add(new WikiPostProcessCorrection
            {
                RuleId = "deps-normalize",
                Description = "Normalized free-form dependency entries into a clean string list.",
                Field = "dependencies",
                Page = page
            });
        }

        return distinct;
    }

    private static bool LooksLikeKeyValuePair(string item)
    {
        var idx = item.IndexOf(':');
        if (idx <= 0 || idx >= item.Length - 1)
        {
            return false;
        }

        // Avoid URLs and Windows drive letters
        if (item.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (idx == 1 && char.IsLetter(item[0]))
        {
            return false; // C:\...
        }

        var key = item[..idx].Trim();
        return key.Length > 0
               && key.Length < 64
               && key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
    }

    private static string FormatDependencyPart(string part)
    {
        var idx = part.IndexOf(':');
        if (idx <= 0)
        {
            return part.Trim();
        }

        var key = part[..idx].Trim();
        var value = part[(idx + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return key;
        }

        // Prefer "key — value" for readability
        return $"{key} — {value}";
    }

    private static List<string> CleanPathList(
        List<string> items,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var cleaned = RewriteAbsolutePathsInText(item.Trim(), context.RepoRoot, corrections, page, field);
            // If the whole item is a single path-like token, normalize via PathUtility directly.
            if (IsLikelyPathToken(cleaned))
            {
                var relative = PathUtility.ToRepoRelative(context.RepoRoot, cleaned);
                if (!string.Equals(relative, cleaned, StringComparison.Ordinal))
                {
                    corrections.Add(new WikiPostProcessCorrection
                    {
                        RuleId = "path-relative",
                        Description = $"Rewrote path to repo-relative: `{relative}`",
                        Field = field,
                        Page = page
                    });
                    cleaned = relative;
                }
            }

            result.Add(cleaned);
        }

        return result;
    }

    private static List<string> CleanStringList(
        List<string> items,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (items.Count == 0)
        {
            return items;
        }

        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var cleaned = CleanProse(item.Trim(), context, corrections, page, field);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            result.Add(cleaned);
        }

        return result;
    }

    private static string CleanProse(
        string text,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? "";
        }

        var result = RewriteAbsolutePathsInText(text, context.RepoRoot, corrections, page, field);
        result = NeutralizeDeprecationLanguage(result, context, corrections, page, field);
        return result;
    }

    private static string CleanMarkdown(
        string markdown,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return markdown ?? "";
        }

        var result = RewriteAbsolutePathsInText(markdown, context.RepoRoot, corrections, page, field);
        result = NeutralizeDeprecationLanguage(result, context, corrections, page, field);
        result = FixMarkdownLinks(result, context, corrections, page, field);
        result = EnsureTrailingNewline(result);
        return result;
    }

    private static string RewriteSinglePath(
        string path,
        string repoRoot,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        var relative = PathUtility.ToRepoRelative(repoRoot, path);
        if (!string.Equals(relative, path.Replace('\\', '/'), StringComparison.Ordinal)
            && !string.Equals(relative, path, StringComparison.Ordinal))
        {
            corrections.Add(new WikiPostProcessCorrection
            {
                RuleId = "path-relative",
                Description = $"Rewrote path `{Truncate(path, 80)}` → `{relative}`",
                Field = field,
                Page = page
            });
        }

        return relative;
    }

    private static string RewriteAbsolutePathsInText(
        string text,
        string repoRoot,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (string.IsNullOrEmpty(text) || !MightContainAbsolutePath(text))
        {
            return text;
        }

        var changed = false;
        var result = AbsolutePathRegex().Replace(text, match =>
        {
            var original = match.Value;
            // Preserve leading delimiter captured loosely — regex is path-only.
            var relative = PathUtility.ToRepoRelative(repoRoot, original);
            if (string.Equals(relative, original, StringComparison.Ordinal)
                || string.Equals(relative, original.Replace('\\', '/'), StringComparison.Ordinal))
            {
                return original;
            }

            changed = true;
            return relative;
        });

        // Windows-style paths not always matched if forward-slash mixed
        result = WindowsPathRegex().Replace(result, match =>
        {
            var original = match.Value;
            var relative = PathUtility.ToRepoRelative(repoRoot, original);
            if (string.Equals(relative, original, StringComparison.Ordinal)
                || string.Equals(relative, original.Replace('\\', '/'), StringComparison.Ordinal))
            {
                return original;
            }

            changed = true;
            return relative;
        });

        if (changed)
        {
            corrections.Add(new WikiPostProcessCorrection
            {
                RuleId = "path-relative",
                Description = "Rewrote absolute or home-relative path(s) to repo-relative form.",
                Field = field,
                Page = page
            });
        }

        return result;
    }

    private static bool MightContainAbsolutePath(string text)
    {
        if (text.Contains("/Users/", StringComparison.Ordinal)
            || text.Contains("/home/", StringComparison.Ordinal)
            || text.Contains("/tmp/", StringComparison.Ordinal)
            || text.Contains("/var/", StringComparison.Ordinal)
            || text.Contains("/private/", StringComparison.Ordinal)
            || text.Contains("/opt/", StringComparison.Ordinal)
            || text.Contains("~/"))
        {
            return true;
        }

        // Windows drive path (C:\ or C:/)
        if (text.Length >= 3
            && char.IsLetter(text[0])
            && text[1] == ':'
            && (text[2] is '\\' or '/'))
        {
            return true;
        }

        return text.Contains(":\\", StringComparison.Ordinal);
    }

    private static string NeutralizeDeprecationLanguage(
        string text,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        if (context.SourceHasObsoleteMarkers || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!DeprecationLanguageRegex().IsMatch(text))
        {
            return text;
        }

        if (context.Mode == WikiPostProcessingMode.Strict)
        {
            // Drop sentences / bullets that are primarily deprecation claims.
            var sentences = SentenceSplitRegex().Split(text);
            var kept = new List<string>();
            var dropped = false;
            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                if (DeprecationLanguageRegex().IsMatch(sentence)
                    && IsPrimarilyDeprecationClaim(sentence))
                {
                    dropped = true;
                    continue;
                }

                kept.Add(sentence.Trim());
            }

            if (dropped)
            {
                corrections.Add(new WikiPostProcessCorrection
                {
                    RuleId = "deprecation-strict",
                    Description =
                        "Removed unverified deprecation/legacy language (no [Obsolete] markers found in source scan).",
                    Field = field,
                    Page = page
                });
                return string.Join(' ', kept).Trim();
            }
        }

        // Lenient: soft rewrite common phrases.
        var rewritten = text;
        rewritten = Regex.Replace(
            rewritten,
            @"\b(is|are|was|were)\s+deprecated\b",
            "should be verified in source (no [Obsolete] markers confirmed)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\bdeprecated\b",
            "current (verify in source)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\bobsolete\b",
            "existing (verify in source)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\blegacy\b",
            "existing",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\bno longer (supported|used|maintained)\b",
            "still present (verify support status in source)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rewritten = Regex.Replace(
            rewritten,
            @"\b(will be|to be)\s+removed\b",
            "may change (verify in source)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!string.Equals(rewritten, text, StringComparison.Ordinal))
        {
            corrections.Add(new WikiPostProcessCorrection
            {
                RuleId = "deprecation-lenient",
                Description =
                    "Neutralized unverified deprecation/legacy language (no [Obsolete] markers found in source scan).",
                Field = field,
                Page = page
            });
        }

        return rewritten;
    }

    private static bool IsPrimarilyDeprecationClaim(string sentence)
    {
        var lower = sentence.ToLowerInvariant();
        // Short claims or those centered on deprecation words
        if (sentence.Length < 160
            && (lower.Contains("deprecated", StringComparison.Ordinal)
                || lower.Contains("obsolete", StringComparison.Ordinal)
                || lower.Contains("legacy", StringComparison.Ordinal)
                || lower.Contains("no longer", StringComparison.Ordinal)))
        {
            return true;
        }

        return DeprecationLanguageRegex().Matches(sentence).Count >= 2;
    }

    private static string FixMarkdownLinks(
        string markdown,
        WikiPostProcessContext context,
        List<WikiPostProcessCorrection> corrections,
        string page,
        string field)
    {
        return MarkdownLinkRegex().Replace(markdown, match =>
        {
            var label = match.Groups[1].Value;
            var href = match.Groups[2].Value.Trim();

            // External / anchors / absolute URIs — leave alone
            if (href.StartsWith('#')
                || href.Contains("://", StringComparison.Ordinal)
                || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            var hashIndex = href.IndexOf('#');
            var pathPart = hashIndex >= 0 ? href[..hashIndex] : href;
            var fragment = hashIndex >= 0 ? href[hashIndex..] : "";

            if (string.IsNullOrWhiteSpace(pathPart))
            {
                return match.Value;
            }

            // Absolute filesystem paths in links
            if (Path.IsPathRooted(PathUtility.ExpandHome(pathPart))
                || pathPart.StartsWith('~'))
            {
                var relative = PathUtility.ToRepoRelative(context.RepoRoot, pathPart);
                // Wiki links should usually be wiki-relative, not repo-root absolute files.
                // If it looks like a .md under docs/wiki, strip to wiki-relative.
                relative = ToWikiRelativeLink(relative, context);
                corrections.Add(new WikiPostProcessCorrection
                {
                    RuleId = "link-path",
                    Description = $"Rewrote absolute link target to `{relative}{fragment}`",
                    Field = field,
                    Page = page
                });
                return $"[{label}]({relative}{fragment})";
            }

            // Obvious missing .md extension for known page basenames
            var normalized = pathPart.Replace('\\', '/');
            if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith('/'))
            {
                var candidate = normalized + ".md";
                var candidateFileName = Path.GetFileName(candidate);
                if (context.KnownWikiPages.Contains(candidate)
                    || context.KnownWikiPages.Contains(candidateFileName)
                    || context.KnownWikiPages.Any(p =>
                        p.EndsWith("/" + candidateFileName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p, candidateFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    corrections.Add(new WikiPostProcessCorrection
                    {
                        RuleId = "link-extension",
                        Description = $"Added missing .md extension to link `{normalized}`",
                        Field = field,
                        Page = page
                    });
                    return $"[{label}]({candidate}{fragment})";
                }
            }

            // Leading slash on relative wiki links (e.g. /architecture.md)
            if (normalized.StartsWith('/') && !Path.IsPathRooted(PathUtility.ExpandHome(normalized)))
            {
                var stripped = normalized.TrimStart('/');
                corrections.Add(new WikiPostProcessCorrection
                {
                    RuleId = "link-leading-slash",
                    Description = $"Stripped leading slash from relative link `{normalized}`",
                    Field = field,
                    Page = page
                });
                return $"[{label}]({stripped}{fragment})";
            }

            return match.Value;
        });
    }

    private static string ToWikiRelativeLink(string repoRelative, WikiPostProcessContext context)
    {
        var normalized = repoRelative.Replace('\\', '/');
        // docs/wiki/foo.md → foo.md when output path is docs/wiki
        var markers = new[] { "docs/wiki/", "wiki/" };
        foreach (var marker in markers)
        {
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return normalized[(idx + marker.Length)..];
            }
        }

        if (context.KnownWikiPages.Contains(Path.GetFileName(normalized)))
        {
            return Path.GetFileName(normalized);
        }

        return normalized;
    }

    private static bool IsLikelyPathToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains(' ', StringComparison.Ordinal)
            && !value.Contains('/')
            && !value.Contains('\\'))
        {
            return false;
        }

        return value.Contains('/')
               || value.Contains('\\')
               || value.StartsWith('~')
               || Path.IsPathRooted(PathUtility.ExpandHome(value));
    }

    private static string EnsureTrailingNewline(string text) =>
        text.EndsWith('\n') ? text : text + "\n";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    // /Users/..., /home/..., /tmp/..., /var/..., /private/..., /opt/..., ~/...
    [GeneratedRegex(
        @"(?:/(?:Users|home|tmp|var|private|opt|System)(?:/[^/\s`""'<>)\]|,]+)+|~(?:/[^/\s`""'<>)\]|,]+)+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex(
        @"\b[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n\s`']+\\)*[^\\/:*?""<>|\r\n\s`']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(
        @"\b(deprecated|deprecation|obsolete|obsoleted|legacy|no longer (?:supported|used|maintained)|(?:will be|to be) removed)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeprecationLanguageRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex MarkdownLinkRegex();
}
