using System.Diagnostics;
using System.Text.RegularExpressions;
using AgentWiki.Core.Abstractions;
using AgentWiki.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Optional syntax-only C# analysis via Roslyn (no project compilation).
/// Offline-safe: returns empty/skipped results for non-.NET repos or on failure.
/// </summary>
public sealed partial class RoslynStaticAnalyzer(ILogger<RoslynStaticAnalyzer> logger) : IStaticAnalyzer
{
    private static readonly HashSet<string> DiMethodNames = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient", "AddHttpClient",
        "AddDbContext", "AddHostedService", "AddOptions", "Configure",
        "AddControllers", "AddControllersWithViews", "AddRazorPages",
        "AddAuthentication", "AddAuthorization", "AddSwaggerGen",
        "AddOpenApi", "AddHealthChecks", "AddMemoryCache", "AddStackExchangeRedisCache"
    };

    private static readonly HashSet<string> MapMethods = new(StringComparer.Ordinal)
    {
        "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods", "Map"
    };

    private static readonly HashSet<string> HttpVerbAttributes = new(StringComparer.Ordinal)
    {
        "HttpGet", "HttpPost", "HttpPut", "HttpDelete", "HttpPatch", "HttpHead", "HttpOptions", "AcceptVerbs", "Route"
    };

    /// <inheritdoc />
    public Task<StaticAnalysisResult> AnalyzeAsync(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(config);

        if (!config.EnableRoslynAnalysis)
        {
            return Task.FromResult(StaticAnalysisResult.Skipped("Roslyn analysis disabled via config."));
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = AnalyzeCore(analysis, config, cancellationToken);
            sw.Stop();
            result = new StaticAnalysisResult
            {
                Enabled = result.Enabled,
                Succeeded = result.Succeeded,
                UsedRoslyn = result.UsedRoslyn,
                Summary = result.Summary,
                Warnings = result.Warnings,
                Projects = result.Projects,
                PublicTypes = result.PublicTypes,
                Endpoints = result.Endpoints,
                EntryPoints = result.EntryPoints,
                DiRegistrations = result.DiRegistrations,
                ObsoleteSymbols = result.ObsoleteSymbols,
                FilesAnalyzed = result.FilesAnalyzed,
                Duration = sw.Elapsed
            };

            logger.LogInformation(
                "Static analysis complete: files={Files}, types={Types}, endpoints={Endpoints}, entryPoints={Entry}, di={Di}, roslyn={Roslyn}, durationMs={Ms}",
                result.FilesAnalyzed,
                result.PublicTypes.Count,
                result.Endpoints.Count,
                result.EntryPoints.Count,
                result.DiRegistrations.Count,
                result.UsedRoslyn,
                (int)sw.Elapsed.TotalMilliseconds);

            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex, "Roslyn static analysis failed; continuing without enrichment");
            return Task.FromResult(new StaticAnalysisResult
            {
                Enabled = true,
                Succeeded = false,
                UsedRoslyn = false,
                Summary = "Static analysis failed: " + ex.Message,
                Warnings = ["Static analysis failed; offline wiki uses inventory heuristics only."],
                Duration = sw.Elapsed
            });
        }
    }

    private StaticAnalysisResult AnalyzeCore(
        RepoAnalysisResult analysis,
        AgentWikiConfig config,
        CancellationToken cancellationToken)
    {
        var maxFiles = config.MaxSourceFilesForRoslyn > 0 ? config.MaxSourceFilesForRoslyn : 500;
        var maxProjects = config.MaxProjectsToAnalyze > 0 ? config.MaxProjectsToAnalyze : 20;

        var projectFiles = analysis.Files
            .Where(f => f.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                        || f.RelativePath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxProjects)
            .ToList();

        var csFiles = analysis.Files
            .Where(f => string.Equals(f.Extension, ".cs", StringComparison.OrdinalIgnoreCase)
                        && f.SelectedForAnalysis
                        && f.Category is FileCategory.SourceCode or FileCategory.Tests or FileCategory.Other)
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();

        if (csFiles.Count == 0)
        {
            return StaticAnalysisResult.Empty(
                projectFiles.Count == 0
                    ? "No C# sources selected; static analysis skipped (non-.NET or empty inventory)."
                    : "Project files found but no C# sources selected for analysis.");
        }

        var projectRoots = BuildProjectRoots(projectFiles, analysis.RepoPath);
        var types = new List<TypeSymbolInfo>();
        var endpoints = new List<EndpointInfo>();
        var entryPoints = new List<string>();
        var di = new List<string>();
        var obsolete = new List<string>();
        var warnings = new List<string>();
        var filesAnalyzed = 0;
        var usedRoslyn = false;

        foreach (var file in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolute = ResolvePath(analysis.RepoPath, file);
            if (absolute is null || !File.Exists(absolute))
            {
                continue;
            }

            string text;
            try
            {
                var info = new FileInfo(absolute);
                if (info.Length > 1_000_000)
                {
                    warnings.Add($"Skipped large file for Roslyn: {file.RelativePath}");
                    continue;
                }

                text = File.ReadAllText(absolute);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read {file.RelativePath}: {ex.Message}");
                continue;
            }

            filesAnalyzed++;
            var projectName = FindProjectName(file.RelativePath, projectRoots);

            try
            {
                var tree = CSharpSyntaxTree.ParseText(text, path: file.RelativePath, cancellationToken: cancellationToken);
                var root = tree.GetCompilationUnitRoot(cancellationToken);
                usedRoslyn = true;

                var walker = new SymbolWalker(file.RelativePath, projectName);
                walker.Visit(root);

                types.AddRange(walker.Types);
                endpoints.AddRange(walker.Endpoints);
                di.AddRange(walker.DiRegistrations);
                obsolete.AddRange(walker.ObsoleteSymbols);

                if (IsEntryPointFile(file.RelativePath))
                {
                    entryPoints.Add(file.RelativePath);
                }

                entryPoints.AddRange(walker.EntryPointHints);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fallback: light regex extraction so we still get something.
                var fallback = HeuristicExtract(file.RelativePath, projectName, text);
                types.AddRange(fallback.Types);
                endpoints.AddRange(fallback.Endpoints);
                if (IsEntryPointFile(file.RelativePath))
                {
                    entryPoints.Add(file.RelativePath);
                }

                warnings.Add($"Roslyn parse fallback for {file.RelativePath}: {ex.Message}");
            }
        }

        // Prefer Program.cs / Startup.cs from inventory even if not walked.
        foreach (var file in analysis.Files.Where(f => IsEntryPointFile(f.RelativePath)))
        {
            if (!entryPoints.Contains(file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                entryPoints.Add(file.RelativePath);
            }
        }

        var publicTypes = types
            .Where(t => t.IsPublic)
            .GroupBy(t => $"{t.Kind}:{t.Namespace}.{t.Name}:{t.RelativePath}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var distinctEndpoints = endpoints
            .GroupBy(e => $"{e.Kind}|{e.HttpMethod}|{e.Route}|{e.HandlerName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HttpMethod, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var distinctDi = di.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        var distinctObsolete = obsolete.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Take(40).ToList();
        var distinctEntry = entryPoints.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        var projects = BuildProjectSummaries(projectFiles, projectRoots, publicTypes, distinctEndpoints, distinctEntry);

        var summary =
            $"Analyzed {filesAnalyzed} C# file(s)" +
            (projectFiles.Count > 0 ? $" across {Math.Min(projectFiles.Count, maxProjects)} project(s)" : "") +
            $": {publicTypes.Count} public type(s), {distinctEndpoints.Count} endpoint(s), " +
            $"{distinctEntry.Count} entry point(s), {distinctDi.Count} DI registration hint(s)" +
            (usedRoslyn ? " (Roslyn syntax)." : " (heuristic).");

        if (filesAnalyzed >= maxFiles)
        {
            warnings.Add($"Hit MaxSourceFilesForRoslyn={maxFiles}; remaining C# files were not parsed.");
        }

        return new StaticAnalysisResult
        {
            Enabled = true,
            Succeeded = true,
            UsedRoslyn = usedRoslyn,
            Summary = summary,
            Warnings = warnings,
            Projects = projects,
            PublicTypes = publicTypes,
            Endpoints = distinctEndpoints,
            EntryPoints = distinctEntry,
            DiRegistrations = distinctDi,
            ObsoleteSymbols = distinctObsolete,
            FilesAnalyzed = filesAnalyzed
        };
    }

    private static List<AnalyzedProject> BuildProjectSummaries(
        List<RepoFile> projectFiles,
        List<(string Dir, string Name)> projectRoots,
        List<TypeSymbolInfo> types,
        List<EndpointInfo> endpoints,
        List<string> entryPoints)
    {
        if (projectFiles.Count == 0)
        {
            return
            [
                new AnalyzedProject
                {
                    Name = "(repository)",
                    RelativePath = ".",
                    Kind = InferRepoKind(types, endpoints),
                    EntryPoints = entryPoints.Take(10).ToList(),
                    PublicTypeCount = types.Count,
                    EndpointCount = endpoints.Count
                }
            ];
        }

        var list = new List<AnalyzedProject>();
        foreach (var proj in projectFiles)
        {
            var dir = Path.GetDirectoryName(proj.RelativePath)?.Replace('\\', '/') ?? "";
            var name = Path.GetFileNameWithoutExtension(proj.RelativePath);
            var prefix = string.IsNullOrEmpty(dir) ? "" : dir + "/";
            var projTypes = types.Where(t =>
                string.Equals(t.ProjectName, name, StringComparison.OrdinalIgnoreCase)
                || t.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            var projEndpoints = endpoints.Where(e =>
                string.Equals(e.ProjectName, name, StringComparison.OrdinalIgnoreCase)
                || e.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            var projEntry = entryPoints.Where(p =>
                string.IsNullOrEmpty(prefix)
                    ? !p.Contains('/', StringComparison.Ordinal)
                    : p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            list.Add(new AnalyzedProject
            {
                Name = name,
                RelativePath = proj.RelativePath,
                Kind = InferProjectKind(name, proj.RelativePath, projTypes, projEndpoints),
                EntryPoints = projEntry.Take(10).ToList(),
                PublicTypeCount = projTypes.Count,
                EndpointCount = projEndpoints.Count
            });
        }

        return list;
    }

    private static string InferRepoKind(List<TypeSymbolInfo> types, List<EndpointInfo> endpoints)
    {
        if (endpoints.Count > 0)
        {
            return "web";
        }

        if (types.Any(t => t.Attributes.Any(a => a.Contains("Function", StringComparison.OrdinalIgnoreCase))))
        {
            return "function";
        }

        return "library";
    }

    private static string InferProjectKind(
        string name,
        string path,
        List<TypeSymbolInfo> types,
        List<EndpointInfo> endpoints)
    {
        var n = name.ToLowerInvariant();
        var p = path.ToLowerInvariant();
        if (n.Contains("test", StringComparison.Ordinal) || p.Contains("/tests/", StringComparison.Ordinal))
        {
            return "test";
        }

        if (endpoints.Any(e => e.Kind is "function")
            || n.Contains("function", StringComparison.Ordinal)
            || types.Any(t => t.Attributes.Any(a => a.Contains("Function", StringComparison.OrdinalIgnoreCase))))
        {
            return "function";
        }

        if (endpoints.Count > 0
            || types.Any(t => t.Name.EndsWith("Controller", StringComparison.Ordinal)
                              || t.Attributes.Any(a => a.Contains("ApiController", StringComparison.OrdinalIgnoreCase))))
        {
            return "web";
        }

        if (n.Contains("cli", StringComparison.Ordinal) || n.Contains("console", StringComparison.Ordinal))
        {
            return "console";
        }

        return "library";
    }

    private static List<(string Dir, string Name)> BuildProjectRoots(List<RepoFile> projectFiles, string repoRoot)
    {
        var roots = new List<(string Dir, string Name)>();
        foreach (var proj in projectFiles)
        {
            var dir = Path.GetDirectoryName(proj.RelativePath)?.Replace('\\', '/') ?? "";
            var name = Path.GetFileNameWithoutExtension(proj.RelativePath);
            roots.Add((dir, name));
        }

        return roots;
    }

    private static string? FindProjectName(string relativePath, List<(string Dir, string Name)> projectRoots)
    {
        var normalized = relativePath.Replace('\\', '/');
        string? bestDir = null;
        string? bestName = null;
        foreach (var (dir, name) in projectRoots)
        {
            if (string.IsNullOrEmpty(dir))
            {
                if (bestDir is null)
                {
                    bestDir = dir;
                    bestName = name;
                }

                continue;
            }

            var prefix = dir.TrimEnd('/') + "/";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (bestDir is null || dir.Length > bestDir.Length))
            {
                bestDir = dir;
                bestName = name;
            }
        }

        return bestName;
    }

    private static string? ResolvePath(string repoRoot, RepoFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.AbsolutePath) && File.Exists(file.AbsolutePath))
        {
            return file.AbsolutePath;
        }

        var combined = Path.Combine(repoRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(combined) ? combined : null;
    }

    private static bool IsEntryPointFile(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase)
               || name.Equals("FunctionApp.cs", StringComparison.OrdinalIgnoreCase)
               || name.Equals("host.json", StringComparison.OrdinalIgnoreCase);
    }

    private static (List<TypeSymbolInfo> Types, List<EndpointInfo> Endpoints) HeuristicExtract(
        string relativePath,
        string? projectName,
        string text)
    {
        var types = new List<TypeSymbolInfo>();
        foreach (Match m in TypeDeclRegex().Matches(text))
        {
            var vis = m.Groups["vis"].Value;
            var kind = m.Groups["kind"].Value;
            var name = m.Groups["name"].Value;
            types.Add(new TypeSymbolInfo
            {
                Name = name,
                Kind = kind,
                RelativePath = relativePath,
                IsPublic = vis.Equals("public", StringComparison.OrdinalIgnoreCase),
                ProjectName = projectName,
                Attributes = []
            });
        }

        var endpoints = new List<EndpointInfo>();
        foreach (Match m in MapGetRegex().Matches(text))
        {
            endpoints.Add(new EndpointInfo
            {
                HttpMethod = MapMethodToHttp(m.Groups["method"].Value),
                Route = m.Groups["route"].Value,
                HandlerName = Path.GetFileNameWithoutExtension(relativePath),
                RelativePath = relativePath,
                Kind = "minimal-api",
                ProjectName = projectName,
                AuthHints = []
            });
        }

        return (types, endpoints);
    }

    private static string MapMethodToHttp(string mapName) => mapName switch
    {
        "MapGet" => "GET",
        "MapPost" => "POST",
        "MapPut" => "PUT",
        "MapDelete" => "DELETE",
        "MapPatch" => "PATCH",
        _ => "ANY"
    };

    private sealed class SymbolWalker(string relativePath, string? projectName) : CSharpSyntaxWalker
    {
        private string? _currentNamespace;
        public List<TypeSymbolInfo> Types { get; } = [];
        public List<EndpointInfo> Endpoints { get; } = [];
        public List<string> DiRegistrations { get; } = [];
        public List<string> ObsoleteSymbols { get; } = [];
        public List<string> EntryPointHints { get; } = [];

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            _currentNamespace = node.Name.ToString();
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var previous = _currentNamespace;
            _currentNamespace = node.Name.ToString();
            base.VisitNamespaceDeclaration(node);
            _currentNamespace = previous;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) =>
            VisitType(node, "class", node.Identifier.Text, node.Modifiers, node.AttributeLists, node.Members);

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
            VisitType(node, "interface", node.Identifier.Text, node.Modifiers, node.AttributeLists, node.Members);

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) =>
            VisitType(node, "record", node.Identifier.Text, node.Modifiers, node.AttributeLists, node.Members);

        public override void VisitStructDeclaration(StructDeclarationSyntax node) =>
            VisitType(node, "struct", node.Identifier.Text, node.Modifiers, node.AttributeLists, node.Members);

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var name = GetInvocationName(node);
            if (name is not null && MapMethods.Contains(name))
            {
                var route = ExtractFirstStringArg(node) ?? "/";
                var http = MapMethodToHttp(name);
                var auth = ExtractAuthFromNearby(node);
                Endpoints.Add(new EndpointInfo
                {
                    HttpMethod = http,
                    Route = route,
                    HandlerName = Path.GetFileNameWithoutExtension(relativePath) + "." + name,
                    RelativePath = relativePath,
                    Kind = "minimal-api",
                    AuthHints = auth,
                    ProjectName = projectName
                });
            }

            if (name is not null && DiMethodNames.Contains(name))
            {
                var typeHint = ExtractGenericOrTypeArg(node);
                DiRegistrations.Add(string.IsNullOrWhiteSpace(typeHint) ? name : $"{name}<{typeHint}>");
            }

            base.VisitInvocationExpression(node);
        }

        private void VisitType(
            SyntaxNode node,
            string kind,
            string name,
            SyntaxTokenList modifiers,
            SyntaxList<AttributeListSyntax> attributeLists,
            SyntaxList<MemberDeclarationSyntax> members)
        {
            var attrs = ExtractAttributeNames(attributeLists);
            var isPublic = modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            Types.Add(new TypeSymbolInfo
            {
                Name = name,
                Kind = kind,
                RelativePath = relativePath,
                Namespace = _currentNamespace,
                IsPublic = isPublic,
                Attributes = attrs,
                ProjectName = projectName
            });

            if (attrs.Any(a => a.Equals("Obsolete", StringComparison.OrdinalIgnoreCase)))
            {
                ObsoleteSymbols.Add($"{kind} {FormatTypeName(name)}");
            }

            // Controllers
            var isController = name.EndsWith("Controller", StringComparison.Ordinal)
                               || attrs.Any(a => a.Equals("ApiController", StringComparison.OrdinalIgnoreCase)
                                                 || a.Equals("Controller", StringComparison.OrdinalIgnoreCase)
                                                 || a.Equals("Route", StringComparison.OrdinalIgnoreCase));

            var routePrefix = ExtractRouteTemplate(attributeLists) ?? "";

            if (isController)
            {
                foreach (var member in members.OfType<MethodDeclarationSyntax>())
                {
                    CollectControllerEndpoints(member, name, routePrefix);
                }
            }

            // Azure Functions on methods
            foreach (var member in members.OfType<MethodDeclarationSyntax>())
            {
                CollectFunctionEndpoints(member, name);
                if (member.AttributeLists.SelectMany(a => a.Attributes)
                    .Any(a => GetAttributeName(a).Equals("Obsolete", StringComparison.OrdinalIgnoreCase)))
                {
                    ObsoleteSymbols.Add($"method {FormatTypeName(name)}.{member.Identifier.Text}");
                }
            }

            // Recurse into nested types via base walker
            foreach (var child in members)
            {
                Visit(child);
            }
        }

        private void CollectControllerEndpoints(MethodDeclarationSyntax method, string controllerName, string routePrefix)
        {
            var methodAttrs = ExtractAttributeNames(method.AttributeLists);
            string? http = null;
            string? template = null;

            foreach (var attr in method.AttributeLists.SelectMany(a => a.Attributes))
            {
                var attrName = GetAttributeName(attr);
                if (attrName is "HttpGet" or "HttpPost" or "HttpPut" or "HttpDelete" or "HttpPatch" or "HttpHead" or "HttpOptions")
                {
                    http = attrName["Http".Length..].ToUpperInvariant();
                    template = ExtractAttributeStringArg(attr) ?? "";
                }
                else if (attrName is "Route" or "AcceptVerbs")
                {
                    template ??= ExtractAttributeStringArg(attr);
                    if (attrName == "AcceptVerbs")
                    {
                        http ??= "ANY";
                    }
                }
            }

            if (http is null && !methodAttrs.Any(a => HttpVerbAttributes.Contains(a)))
            {
                return;
            }

            http ??= "ANY";
            var route = CombineRoutes(routePrefix, template ?? "");
            if (string.IsNullOrWhiteSpace(route))
            {
                route = "/" + controllerName.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);
            }

            var auth = ExtractAuthAttributes(method.AttributeLists);
            Endpoints.Add(new EndpointInfo
            {
                HttpMethod = http,
                Route = route,
                HandlerName = $"{controllerName}.{method.Identifier.Text}",
                RelativePath = relativePath,
                Kind = "controller",
                AuthHints = auth,
                Parameters = ExtractParameters(method),
                ProjectName = projectName
            });
        }

        private void CollectFunctionEndpoints(MethodDeclarationSyntax method, string typeName)
        {
            string? functionName = null;
            string? httpMethod = null;
            string? route = null;
            var auth = new List<string>();

            foreach (var attr in method.AttributeLists.SelectMany(a => a.Attributes))
            {
                var attrName = GetAttributeName(attr);
                if (attrName is "FunctionName" or "Function")
                {
                    functionName = ExtractAttributeStringArg(attr) ?? method.Identifier.Text;
                }

                if (attrName is "HttpTrigger")
                {
                    httpMethod = ExtractNamedOrFirstString(attr, "methods") ?? "GET";
                    route = ExtractNamedOrFirstString(attr, "Route") ?? ExtractNamedOrFirstString(attr, "route");
                }

                if (attrName is "Authorize" or "AllowAnonymous")
                {
                    auth.Add(attrName);
                }
            }

            // Also scan parameters for HttpTrigger
            foreach (var param in method.ParameterList.Parameters)
            {
                foreach (var attr in param.AttributeLists.SelectMany(a => a.Attributes))
                {
                    if (GetAttributeName(attr) is "HttpTrigger")
                    {
                        httpMethod ??= ExtractNamedOrFirstString(attr, "methods") ?? "GET";
                        route ??= ExtractNamedOrFirstString(attr, "Route")
                                  ?? ExtractNamedOrFirstString(attr, "route");
                    }
                }
            }

            if (functionName is null && httpMethod is null)
            {
                return;
            }

            Endpoints.Add(new EndpointInfo
            {
                HttpMethod = (httpMethod ?? "ANY").ToUpperInvariant(),
                Route = route ?? ("/api/" + (functionName ?? method.Identifier.Text)),
                HandlerName = $"{typeName}.{method.Identifier.Text}",
                RelativePath = relativePath,
                Kind = "function",
                AuthHints = auth,
                Parameters = ExtractParameters(method),
                ProjectName = projectName
            });

            EntryPointHints.Add(relativePath);
        }

        private string FormatTypeName(string name) =>
            string.IsNullOrWhiteSpace(_currentNamespace) ? name : $"{_currentNamespace}.{name}";

        private static List<string> ExtractParameters(MethodDeclarationSyntax method)
        {
            var list = new List<string>();
            foreach (var param in method.ParameterList.Parameters)
            {
                var type = param.Type?.ToString() ?? "?";
                var name = param.Identifier.Text;
                var attrs = param.AttributeLists
                    .SelectMany(a => a.Attributes)
                    .Select(GetAttributeName)
                    .Where(a => a is "FromRoute" or "FromQuery" or "FromBody" or "FromHeader" or "FromServices"
                        or "HttpTrigger" or "FromForm")
                    .ToList();
                var prefix = attrs.Count > 0 ? $"[{string.Join(",", attrs)}] " : "";
                list.Add($"{prefix}{type} {name}");
            }

            return list;
        }

        private static List<string> ExtractAttributeNames(SyntaxList<AttributeListSyntax> lists) =>
            lists.SelectMany(l => l.Attributes).Select(GetAttributeName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static List<string> ExtractAuthAttributes(SyntaxList<AttributeListSyntax> lists)
        {
            var result = new List<string>();
            foreach (var attr in lists.SelectMany(l => l.Attributes))
            {
                var name = GetAttributeName(attr);
                if (name is "Authorize" or "AllowAnonymous" or "AuthorizeAttribute")
                {
                    result.Add(name.Replace("Attribute", "", StringComparison.Ordinal));
                }
            }

            return result;
        }

        private static List<string> ExtractAuthFromNearby(InvocationExpressionSyntax node)
        {
            // app.MapGet(...).RequireAuthorization() chain
            var result = new List<string>();
            var current = node.Parent;
            while (current is MemberAccessExpressionSyntax or InvocationExpressionSyntax)
            {
                if (current is InvocationExpressionSyntax inv)
                {
                    var n = GetInvocationName(inv);
                    if (n is "RequireAuthorization" or "AllowAnonymous")
                    {
                        result.Add(n);
                    }
                }

                current = current.Parent;
            }

            return result;
        }

        private static string GetAttributeName(AttributeSyntax attr)
        {
            var name = attr.Name.ToString();
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
            {
                name = name[..^"Attribute".Length];
            }

            // Qualified names: Microsoft.AspNetCore.Mvc.HttpGet
            var lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                name = name[(lastDot + 1)..];
            }

            return name;
        }

        private static string? ExtractRouteTemplate(SyntaxList<AttributeListSyntax> lists)
        {
            foreach (var attr in lists.SelectMany(l => l.Attributes))
            {
                if (GetAttributeName(attr) is "Route" or "RoutePrefix")
                {
                    return ExtractAttributeStringArg(attr);
                }
            }

            return null;
        }

        private static string? ExtractAttributeStringArg(AttributeSyntax attr)
        {
            if (attr.ArgumentList is null || attr.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return lit.Token.ValueText;
                }
            }

            return null;
        }

        private static string? ExtractNamedOrFirstString(AttributeSyntax attr, string name)
        {
            if (attr.ArgumentList is null)
            {
                return null;
            }

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.NameEquals is not null
                    && arg.NameEquals.Name.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && arg.Expression is LiteralExpressionSyntax namedLit
                    && namedLit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return namedLit.Token.ValueText;
                }

                if (arg.NameColon is not null
                    && arg.NameColon.Name.Identifier.Text.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && arg.Expression is LiteralExpressionSyntax colonLit
                    && colonLit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return colonLit.Token.ValueText;
                }
            }

            return ExtractAttributeStringArg(attr);
        }

        private static string? GetInvocationName(InvocationExpressionSyntax node) =>
            node.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                GenericNameSyntax g => g.Identifier.Text,
                MemberBindingExpressionSyntax bind => bind.Name.Identifier.Text,
                _ => null
            };

        private static string? ExtractFirstStringArg(InvocationExpressionSyntax node)
        {
            foreach (var arg in node.ArgumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    return lit.Token.ValueText;
                }
            }

            return null;
        }

        private static string? ExtractGenericOrTypeArg(InvocationExpressionSyntax node)
        {
            if (node.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax generic })
            {
                return string.Join(", ", generic.TypeArgumentList.Arguments.Select(t => t.ToString()));
            }

            if (node.Expression is GenericNameSyntax g)
            {
                return string.Join(", ", g.TypeArgumentList.Arguments.Select(t => t.ToString()));
            }

            foreach (var arg in node.ArgumentList.Arguments)
            {
                if (arg.Expression is TypeOfExpressionSyntax typeOf)
                {
                    return typeOf.Type.ToString();
                }
            }

            return null;
        }

        private static string CombineRoutes(string prefix, string template)
        {
            prefix = (prefix ?? "").Trim();
            template = (template ?? "").Trim();
            if (prefix.Length == 0)
            {
                return template.StartsWith('/') ? template : "/" + template.TrimStart('/');
            }

            if (template.Length == 0)
            {
                return prefix.StartsWith('/') ? prefix : "/" + prefix.TrimStart('/');
            }

            var combined = prefix.TrimEnd('/') + "/" + template.TrimStart('/');
            return combined.StartsWith('/') ? combined : "/" + combined;
        }
    }

    [GeneratedRegex(
        @"\b(?<vis>public|internal|private|protected)?\s*(?:static\s+|partial\s+|sealed\s+|abstract\s+)*(?<kind>class|interface|record|struct)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TypeDeclRegex();

    [GeneratedRegex(
        @"\.(?<method>MapGet|MapPost|MapPut|MapDelete|MapPatch)\s*\(\s*""(?<route>[^""]+)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex MapGetRegex();
}
