using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging;

namespace AgentWiki.App.Services;

/// <summary>
/// Collects high-signal cross-repo hints from member inventories (packages, project refs,
/// CODEOWNERS, OpenAPI/contracts). File-based only — no embeddings.
/// </summary>
public sealed partial class CrossRepoSignalCollector(ILogger<CrossRepoSignalCollector> logger)
    : ICrossRepoSignalCollector
{
    /// <inheritdoc />
    public async Task<CrossRepoSignals> CollectAsync(
        IReadOnlyList<WorkspaceMemberAnalysis> members,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);

        var packageMap = new Dictionary<string, PackageAccumulator>(StringComparer.OrdinalIgnoreCase);
        var projectRefs = new List<ProjectReferenceSignal>();
        var ownership = new List<OwnershipSignal>();
        var contracts = new List<ContractSignal>();
        var notes = new List<string>();

        // Project names by member for crude cross-member matching.
        var projectNamesByMember = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members.Where(m => m.Resolved.Success))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = member.Resolved.Definition.Id;
            var root = member.Resolved.AbsolutePath;
            projectNamesByMember[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> relativeFiles;
            if (member.Analysis is { Files.Count: > 0 } analysis)
            {
                relativeFiles = analysis.Files.Select(f => f.RelativePath.Replace('\\', '/'));
            }
            else
            {
                relativeFiles = EnumerateHeuristicFiles(root);
            }

            foreach (var rel in relativeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalized = rel.Replace('\\', '/');
                var fileName = Path.GetFileName(normalized);

                if (normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
                {
                    projectNamesByMember[id].Add(Path.GetFileNameWithoutExtension(fileName));
                    await CollectFromProjectFileAsync(
                            root,
                            normalized,
                            id,
                            packageMap,
                            projectRefs,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (string.Equals(fileName, "packages.config", StringComparison.OrdinalIgnoreCase))
                {
                    await CollectPackagesConfigAsync(root, normalized, id, packageMap, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (string.Equals(fileName, "package.json", StringComparison.OrdinalIgnoreCase))
                {
                    await CollectPackageJsonAsync(root, normalized, id, packageMap, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (IsOwnershipFile(normalized))
                {
                    var excerpt = await ReadExcerptAsync(root, normalized, 40, cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(excerpt))
                    {
                        ownership.Add(new OwnershipSignal
                        {
                            MemberId = id,
                            SourcePath = normalized,
                            Excerpt = excerpt
                        });
                    }
                }
                else if (IsContractFile(normalized))
                {
                    contracts.Add(new ContractSignal
                    {
                        MemberId = id,
                        RelativePath = normalized,
                        Kind = ClassifyContract(normalized)
                    });
                }
            }
        }

        // Match project references to other members by project name.
        foreach (var pr in projectRefs)
        {
            var refName = Path.GetFileNameWithoutExtension(pr.ToReference.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(refName))
            {
                continue;
            }

            foreach (var (memberId, names) in projectNamesByMember)
            {
                if (memberId.Equals(pr.FromMemberId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (names.Contains(refName))
                {
                    pr.MatchedMemberId = memberId;
                    break;
                }
            }
        }

        var sharedPackages = packageMap.Values
            .Select(a => new PackageSignal
            {
                PackageId = a.PackageId,
                Ecosystem = a.Ecosystem,
                MemberIds = a.MemberIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Versions = a.Versions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderByDescending(p => p.MemberIds.Count)
            .ThenBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var multiMemberPackages = sharedPackages.Count(p => p.MemberIds.Count >= 2);
        if (multiMemberPackages > 0)
        {
            notes.Add($"Detected {multiMemberPackages} package(s) shared by 2+ members.");
        }

        if (contracts.Count > 0)
        {
            notes.Add($"Found {contracts.Count} contract/schema artifact(s) across members.");
        }

        logger.LogInformation(
            "Cross-repo signals: packages={Packages} shared={Shared} projectRefs={Refs} ownership={Own} contracts={Contracts}",
            sharedPackages.Count,
            multiMemberPackages,
            projectRefs.Count,
            ownership.Count,
            contracts.Count);

        return new CrossRepoSignals
        {
            SharedPackages = sharedPackages,
            ProjectReferences = projectRefs,
            Ownership = ownership,
            Contracts = contracts,
            Notes = notes
        };
    }

    private static async Task CollectFromProjectFileAsync(
        string root,
        string relativePath,
        string memberId,
        Dictionary<string, PackageAccumulator> packageMap,
        List<ProjectReferenceSignal> projectRefs,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(absolute);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            foreach (var pkg in doc.Descendants("PackageReference"))
            {
                var name = (string?)pkg.Attribute("Include") ?? (string?)pkg.Attribute("Update");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var version = (string?)pkg.Attribute("Version")
                              ?? pkg.Elements("Version").FirstOrDefault()?.Value
                              ?? "";
                AddPackage(packageMap, name.Trim(), "nuget", memberId, version);
            }

            foreach (var pref in doc.Descendants("ProjectReference"))
            {
                var include = (string?)pref.Attribute("Include");
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                projectRefs.Add(new ProjectReferenceSignal
                {
                    FromMemberId = memberId,
                    FromProject = relativePath,
                    ToReference = include.Replace('\\', '/')
                });
            }
        }
        catch
        {
            // Malformed csproj — skip
        }
    }

    private static async Task CollectPackagesConfigAsync(
        string root,
        string relativePath,
        string memberId,
        Dictionary<string, PackageAccumulator> packageMap,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(absolute);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
            foreach (var pkg in doc.Descendants("package"))
            {
                var name = (string?)pkg.Attribute("id");
                var version = (string?)pkg.Attribute("version") ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddPackage(packageMap, name.Trim(), "nuget", memberId, version);
                }
            }
        }
        catch
        {
            // skip
        }
    }

    private static async Task CollectPackageJsonAsync(
        string root,
        string relativePath,
        string memberId,
        Dictionary<string, PackageAccumulator> packageMap,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
            // Lightweight: match "name": "version" under dependencies-like sections without full JSON DOM.
            foreach (Match match in PackageJsonDependencyRegex().Matches(text))
            {
                var name = match.Groups["name"].Value;
                var version = match.Groups["version"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AddPackage(packageMap, name, "npm", memberId, version);
                }
            }
        }
        catch
        {
            // skip
        }
    }

    private static void AddPackage(
        Dictionary<string, PackageAccumulator> map,
        string packageId,
        string ecosystem,
        string memberId,
        string version)
    {
        var key = $"{ecosystem}:{packageId}";
        if (!map.TryGetValue(key, out var acc))
        {
            acc = new PackageAccumulator(packageId, ecosystem);
            map[key] = acc;
        }

        acc.MemberIds.Add(memberId);
        if (!string.IsNullOrWhiteSpace(version))
        {
            acc.Versions.Add(version.Trim());
        }
    }

    private static IEnumerable<string> EnumerateHeuristicFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        string[] patterns =
        [
            "*.csproj", "*.fsproj", "packages.config", "package.json",
            "CODEOWNERS", "openapi*.json", "openapi*.yaml", "openapi*.yml",
            "swagger*.json", "*.proto", "asyncapi*.json", "asyncapi*.yaml"
        ];

        foreach (var pattern in patterns)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                // Skip common junk
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return Path.GetRelativePath(root, f).Replace('\\', '/');
            }
        }

        // CODEOWNERS often under .github/
        var codeowners = new[]
        {
            Path.Combine(root, "CODEOWNERS"),
            Path.Combine(root, ".github", "CODEOWNERS"),
            Path.Combine(root, "docs", "CODEOWNERS")
        };
        foreach (var path in codeowners)
        {
            if (File.Exists(path))
            {
                yield return Path.GetRelativePath(root, path).Replace('\\', '/');
            }
        }
    }

    private static bool IsOwnershipFile(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return string.Equals(name, "CODEOWNERS", StringComparison.OrdinalIgnoreCase)
               || relativePath.EndsWith("/CODEOWNERS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContractFile(string relativePath)
    {
        var name = Path.GetFileName(relativePath).ToLowerInvariant();
        return name.Contains("openapi", StringComparison.Ordinal)
               || name.Contains("swagger", StringComparison.Ordinal)
               || name.Contains("asyncapi", StringComparison.Ordinal)
               || name.EndsWith(".proto", StringComparison.Ordinal)
               || relativePath.Contains("/contracts/", StringComparison.OrdinalIgnoreCase)
               || relativePath.Contains("/schemas/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyContract(string relativePath)
    {
        var name = Path.GetFileName(relativePath).ToLowerInvariant();
        if (name.EndsWith(".proto", StringComparison.Ordinal))
        {
            return "protobuf";
        }

        if (name.Contains("asyncapi", StringComparison.Ordinal))
        {
            return "asyncapi";
        }

        if (name.Contains("swagger", StringComparison.Ordinal) || name.Contains("openapi", StringComparison.Ordinal))
        {
            return "openapi";
        }

        if (relativePath.Contains("/schemas/", StringComparison.OrdinalIgnoreCase))
        {
            return "schema";
        }

        return "contract";
    }

    private static async Task<string> ReadExcerptAsync(
        string root,
        string relativePath,
        int maxLines,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
        {
            return "";
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(absolute, cancellationToken).ConfigureAwait(false);
            return string.Join(
                Environment.NewLine,
                lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')).Take(maxLines));
        }
        catch
        {
            return "";
        }
    }

    [GeneratedRegex(
        "\"(?<name>[^\"\\s]+)\"\\s*:\\s*\"(?<version>[^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex PackageJsonDependencyRegex();

    private sealed class PackageAccumulator(string packageId, string ecosystem)
    {
        public string PackageId { get; } = packageId;
        public string Ecosystem { get; } = ecosystem;
        public HashSet<string> MemberIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
