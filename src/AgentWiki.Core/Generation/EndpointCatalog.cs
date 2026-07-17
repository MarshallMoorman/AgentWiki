using System.Text.RegularExpressions;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Filters, normalizes, and assigns discovered endpoints to modules / wiki pages.
/// </summary>
public static partial class EndpointCatalog
{
    /// <summary>
    /// Returns endpoints from static analysis after noise removal and config include/exclude filters.
    /// Also expands <c>[controller]</c> tokens when still present.
    /// </summary>
    public static IReadOnlyList<EndpointInfo> Filter(
        IReadOnlyList<EndpointInfo>? endpoints,
        AgentWikiConfig config)
    {
        if (endpoints is null || endpoints.Count == 0 || !config.EnableApiEndpointDocs)
        {
            return [];
        }

        var list = endpoints
            .Select(NormalizeEndpoint)
            .Where(e => !IsNoiseEndpoint(e))
            .AsEnumerable();

        if (config.EndpointIncludePatterns.Count > 0)
        {
            list = list.Where(e =>
                config.EndpointIncludePatterns.Any(p => Matches(e, p)));
        }

        if (config.EndpointExcludePatterns.Count > 0)
        {
            list = list.Where(e =>
                !config.EndpointExcludePatterns.Any(p => Matches(e, p)));
        }

        return list
            .GroupBy(e => $"{e.Kind}|{e.HttpMethod}|{e.Route}|{e.HandlerName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HttpMethod, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HandlerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Endpoints that belong to a module via related files, controller/handler names,
    /// route tokens, or path under a non-shared root — never the whole catalog.
    /// </summary>
    public static IReadOnlyList<EndpointInfo> ForModule(
        ModuleDescriptor descriptor,
        IReadOnlyList<EndpointInfo> catalog,
        IReadOnlyList<ModuleDescriptor>? allDescriptors = null)
    {
        if (catalog.Count == 0)
        {
            return [];
        }

        var sharedRoots = ComputeSharedRoots(allDescriptors ?? [descriptor]);
        var scored = catalog
            .Select(e => (Endpoint: e, Score: ScoreModuleMatch(e, descriptor, sharedRoots)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Endpoint.Route, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Endpoint)
            .ToList();

        return scored;
    }

    /// <summary>
    /// Assigns each catalog endpoint to at most one module (highest score wins).
    /// Prevents shared route tokens (e.g. <c>/loans/{id}/rewards</c>) from polluting every module.
    /// </summary>
    public static void AttachToModules(
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<ModuleDescriptor> descriptors,
        IReadOnlyList<EndpointInfo> catalog)
    {
        var byId = descriptors.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var sharedRoots = ComputeSharedRoots(descriptors);

        // Resolve descriptors aligned to each module document
        var resolved = modules.Select(module =>
        {
            if (!byId.TryGetValue(module.Id, out var descriptor))
            {
                descriptor = new ModuleDescriptor
                {
                    Id = module.Id,
                    Name = module.Title,
                    RootPaths = [],
                    RelatedFiles = module.RelatedFiles
                        .Select(LlmTextCleanup.ExtractPathFromRelatedFile)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Cast<string>()
                        .ToList()
                };
            }

            return (Module: module, Descriptor: descriptor);
        }).ToList();

        foreach (var pair in resolved)
        {
            pair.Module.Endpoints = [];
        }

        const int minScore = 40; // ignore weak path-only noise under shared roots
        foreach (var endpoint in catalog)
        {
            ModuleDocument? bestModule = null;
            var bestScore = 0;
            foreach (var (module, descriptor) in resolved)
            {
                var score = ScoreModuleMatch(endpoint, descriptor, sharedRoots);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestModule = module;
                }
            }

            if (bestModule is not null && bestScore >= minScore)
            {
                bestModule.Endpoints.Add(endpoint);
            }
        }

        foreach (var pair in resolved)
        {
            pair.Module.Endpoints = pair.Module.Endpoints
                .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.HttpMethod, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Fills empty descriptions with deterministic heuristics.</summary>
    public static void EnsureDefaultDescriptions(IEnumerable<EndpointInfo> endpoints)
    {
        foreach (var ep in endpoints)
        {
            if (!string.IsNullOrWhiteSpace(ep.Description))
            {
                continue;
            }

            ep.Description = InferDescription(ep);
        }
    }

    public static string InferDescription(EndpointInfo ep)
    {
        var route = ep.Route ?? "";
        var handler = ep.HandlerName ?? "";
        var method = (ep.HttpMethod ?? "ANY").ToUpperInvariant();

        if (route.Contains("health", StringComparison.OrdinalIgnoreCase)
            || handler.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            return "Health / readiness probe.";
        }

        if (route.Contains("swagger", StringComparison.OrdinalIgnoreCase)
            || route.Contains("openapi", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAPI / Swagger metadata surface.";
        }

        if (ep.Kind.Equals("function", StringComparison.OrdinalIgnoreCase))
        {
            return $"Azure Function HTTP trigger ({method}).";
        }

        if (ep.Kind.Equals("minimal-api", StringComparison.OrdinalIgnoreCase))
        {
            return $"Minimal API endpoint ({method} {route}).";
        }

        var action = handler.Contains('.', StringComparison.Ordinal)
            ? handler[(handler.LastIndexOf('.') + 1)..]
            : handler;

        return $"Controller action `{action}` ({method}).";
    }

    /// <summary>
    /// Noise rows: bare Map false positives, catch-alls, empty routes, etc.
    /// </summary>
    public static bool IsNoiseEndpoint(EndpointInfo endpoint)
    {
        var route = (endpoint.Route ?? "").Trim();
        var handler = endpoint.HandlerName ?? "";
        var kind = endpoint.Kind ?? "";

        if (string.IsNullOrWhiteSpace(route) || route is "/")
        {
            // Allow intentional root health only if named as such
            if (!handler.Contains("health", StringComparison.OrdinalIgnoreCase)
                && !route.Contains("health", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (route.Contains("{**", StringComparison.Ordinal)
            || route.Contains("{*", StringComparison.Ordinal))
        {
            return true;
        }

        // Bare "Map" extension methods (AutoMapper, DI) mis-tagged as minimal-api
        if (kind.Equals("minimal-api", StringComparison.OrdinalIgnoreCase))
        {
            var leaf = handler.Contains('.', StringComparison.Ordinal)
                ? handler[(handler.LastIndexOf('.') + 1)..]
                : handler;
            if (leaf.Equals("Map", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("MapControllers", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("MapRazorPages", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("MapFallbackToFile", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Management / Proxy classes that only have Map helpers, not HTTP verbs
            if ((handler.Contains("Management.Map", StringComparison.OrdinalIgnoreCase)
                 || handler.Contains("Proxy.Map", StringComparison.OrdinalIgnoreCase)
                 || handler.Contains("Controller.Map", StringComparison.OrdinalIgnoreCase))
                && (route is "/" or ""))
            {
                return true;
            }
        }

        return false;
    }

    public static EndpointInfo NormalizeEndpoint(EndpointInfo endpoint)
    {
        var route = ExpandControllerTokens(endpoint.Route, endpoint.HandlerName);
        route = NormalizeRoutePath(route);
        if (string.Equals(route, endpoint.Route, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(endpoint.Route))
        {
            return endpoint;
        }

        return new EndpointInfo
        {
            HttpMethod = endpoint.HttpMethod,
            Route = route,
            HandlerName = endpoint.HandlerName,
            RelativePath = endpoint.RelativePath,
            Kind = endpoint.Kind,
            AuthHints = endpoint.AuthHints,
            Parameters = endpoint.Parameters,
            ProjectName = endpoint.ProjectName,
            Description = endpoint.Description
        };
    }

    /// <summary>
    /// Score how strongly an endpoint belongs to a module. 0 = no match.
    /// </summary>
    internal static int ScoreModuleMatch(
        EndpointInfo endpoint,
        ModuleDescriptor descriptor,
        HashSet<string> sharedRoots)
    {
        var score = 0;
        var path = NormalizePath(endpoint.RelativePath);
        var handler = endpoint.HandlerName ?? "";
        var controller = ExtractControllerName(handler);
        var related = descriptor.RelatedFiles
            .Select(LlmTextCleanup.ExtractPathFromRelatedFile)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => NormalizePath(p!))
            .ToList();

        var tokens = ModuleTokens(descriptor);
        var hostLike = LooksLikeHostOrPlatformModule(descriptor);
        var relatedControllerCount = related.Count(f =>
            f.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase));

        // Exact related-file match — strong, but host modules that list every controller
        // must not monopolize feature endpoints.
        if (related.Any(f => f.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
            if (hostLike && relatedControllerCount > 2
                && path.EndsWith("Controller.cs", StringComparison.OrdinalIgnoreCase))
            {
                score -= 75;
            }
        }

        // Related file is the same controller type (partial path / filename)
        if (!string.IsNullOrEmpty(controller)
            && related.Any(f =>
                Path.GetFileNameWithoutExtension(f)
                    .Equals(controller, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(f)
                    .Equals(controller + "Controller", StringComparison.OrdinalIgnoreCase)))
        {
            score += 80;
            if (hostLike && relatedControllerCount > 2)
            {
                score -= 50;
            }
        }

        // Module id / name tokens in handler (e.g. loan → LoansController)
        if (tokens.Count > 0)
        {
            if (tokens.Any(t =>
                    handler.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || (controller?.Contains(t, StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                score += 90; // prefer feature modules named after the controller
            }

            // Route segment match is weaker than handler/related-file (nested routes like
            // /loans/{id}/rewards should not beat RewardsController for the rewards module).
            var route = endpoint.Route ?? "";
            var segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(t =>
                    segments.Any(s => s.Equals(t, StringComparison.OrdinalIgnoreCase))))
            {
                score += 25;
            }
        }

        // Health endpoints → host/platform/observability modules
        if (path.Contains("Health", StringComparison.OrdinalIgnoreCase)
            || handler.Contains("Health", StringComparison.OrdinalIgnoreCase)
            || (endpoint.Route?.Contains("health", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            if (hostLike
                || (descriptor.Id + descriptor.Name).Contains("health", StringComparison.OrdinalIgnoreCase)
                || (descriptor.Id + descriptor.Name).Contains("observ", StringComparison.OrdinalIgnoreCase))
            {
                score += 70;
            }
        }

        // Path under a non-shared root (unique to this module)
        foreach (var root in descriptor.RootPaths)
        {
            var prefix = NormalizePath(root).TrimEnd('/') + "/";
            if (prefix is "./" or "/" or "")
            {
                continue; // never match-all
            }

            if (sharedRoots.Contains(prefix.TrimEnd('/')))
            {
                // Shared root alone is not enough — only boost if we already have another signal
                if (score > 0
                    && (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                {
                    score += 10;
                }

                continue;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || path.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
        }

        // Project name alone is weak when many modules share one API project — only with token
        if (!string.IsNullOrWhiteSpace(endpoint.ProjectName)
            && tokens.Count > 0
            && tokens.Any(t => endpoint.ProjectName!.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            score += 15;
        }

        return score;
    }

    private static HashSet<string> ComputeSharedRoots(IReadOnlyList<ModuleDescriptor> descriptors)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in descriptors)
        {
            foreach (var root in d.RootPaths)
            {
                var key = NormalizePath(root).TrimEnd('/');
                if (key is "" or "." or "./")
                {
                    continue;
                }

                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHostOrPlatformModule(ModuleDescriptor descriptor)
    {
        var blob = $"{descriptor.Id} {descriptor.Name}".ToLowerInvariant();
        return blob.Contains("host", StringComparison.Ordinal)
               || blob.Contains("composition", StringComparison.Ordinal)
               || blob.Contains("platform", StringComparison.Ordinal)
               || blob.Contains("bootstrap", StringComparison.Ordinal)
               || blob.Contains("http surface", StringComparison.Ordinal)
               || blob.Contains("cross-cutting", StringComparison.Ordinal)
               || blob.Contains("cross cutting", StringComparison.Ordinal);
    }

    private static List<string> ModuleTokens(ModuleDescriptor descriptor)
    {
        var raw = new[] { descriptor.Id, descriptor.Name }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .SelectMany(s => SplitTokens(s!))
            .Where(t => t.Length >= 3)
            .Where(t => t is not ("module" or "api" or "host" or "app" or "core" or "common" or "shared"
                or "service" or "services" or "view" or "lms" or "elevate" or "and" or "the"
                or "management" or "configuration" or "application" or "integration" or "contracts"
                or "error" or "handling" or "observability" or "health" or "branding" or "customization"
                or "surface" or "http" or "platform" or "cross" or "cutting" or "loyalty" or "external"
                or "clients" or "concerns"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return raw;
    }

    private static IEnumerable<string> SplitTokens(string value)
    {
        foreach (var part in value.Replace('-', ' ').Replace('_', ' ')
                     .Split([' ', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
            // CamelCase / PascalCase split
            var camel = CamelSplitRegex().Split(part).Where(p => p.Length > 0);
            foreach (var c in camel)
            {
                yield return c;
            }
        }
    }

    private static string? ExtractControllerName(string handler)
    {
        if (string.IsNullOrWhiteSpace(handler))
        {
            return null;
        }

        var typePart = handler.Contains('.', StringComparison.Ordinal)
            ? handler[..handler.IndexOf('.')]
            : handler;
        return typePart;
    }

    private static string ExpandControllerTokens(string? route, string? handler)
    {
        route ??= "";
        if (!route.Contains('[', StringComparison.Ordinal))
        {
            return route;
        }

        var controller = ExtractControllerName(handler ?? "");
        if (string.IsNullOrEmpty(controller))
        {
            return route;
        }

        var token = controller.EndsWith("Controller", StringComparison.OrdinalIgnoreCase)
            ? controller[..^"Controller".Length]
            : controller;
        var action = handler!.Contains('.', StringComparison.Ordinal)
            ? handler[(handler.LastIndexOf('.') + 1)..]
            : "";

        return route
            .Replace("[controller]", token, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", action, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoutePath(string route)
    {
        route = (route ?? "").Trim().Replace('\\', '/');
        if (route.Length == 0)
        {
            return "/";
        }

        if (!route.StartsWith('/'))
        {
            route = "/" + route;
        }

        while (route.Contains("//", StringComparison.Ordinal))
        {
            route = route.Replace("//", "/", StringComparison.Ordinal);
        }

        return route;
    }

    private static string NormalizePath(string path) =>
        (path ?? "").Replace('\\', '/').Trim();

    private static bool Matches(EndpointInfo endpoint, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var haystacks = new[]
        {
            endpoint.Route,
            endpoint.HandlerName,
            endpoint.RelativePath,
            endpoint.HttpMethod,
            endpoint.Kind,
            endpoint.ProjectName ?? ""
        };

        var p = pattern.Trim();
        if (!p.Contains('*') && !p.Contains('?'))
        {
            return haystacks.Any(h =>
                h.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        var regex = "^" + Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return haystacks.Any(h => Regex.IsMatch(h, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelSplitRegex();
}
