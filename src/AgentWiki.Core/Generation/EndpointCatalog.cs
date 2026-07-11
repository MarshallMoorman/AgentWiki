using System.Text.RegularExpressions;
using AgentWiki.Core.Models;

namespace AgentWiki.Core.Generation;

/// <summary>
/// Filters and assigns discovered endpoints to modules / wiki pages.
/// </summary>
public static partial class EndpointCatalog
{
    /// <summary>
    /// Returns endpoints from static analysis after config include/exclude filters.
    /// </summary>
    public static IReadOnlyList<EndpointInfo> Filter(
        IReadOnlyList<EndpointInfo>? endpoints,
        AgentWikiConfig config)
    {
        if (endpoints is null || endpoints.Count == 0 || !config.EnableApiEndpointDocs)
        {
            return [];
        }

        var list = endpoints.AsEnumerable();

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
            .OrderBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HttpMethod, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.HandlerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Endpoints that belong to a module by project name, path prefix, or related files.
    /// </summary>
    public static IReadOnlyList<EndpointInfo> ForModule(
        ModuleDescriptor descriptor,
        IReadOnlyList<EndpointInfo> catalog)
    {
        if (catalog.Count == 0)
        {
            return [];
        }

        return catalog
            .Where(e => BelongsToModule(e, descriptor))
            .ToList();
    }

    /// <summary>
    /// Assigns catalog endpoints onto module documents (mutates <see cref="ModuleDocument.Endpoints"/>).
    /// </summary>
    public static void AttachToModules(
        IReadOnlyList<ModuleDocument> modules,
        IReadOnlyList<ModuleDescriptor> descriptors,
        IReadOnlyList<EndpointInfo> catalog)
    {
        var byId = descriptors.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            if (!byId.TryGetValue(module.Id, out var descriptor))
            {
                descriptor = new ModuleDescriptor
                {
                    Id = module.Id,
                    Name = module.Title,
                    RootPaths = [],
                    RelatedFiles = module.RelatedFiles
                };
            }

            module.Endpoints = ForModule(descriptor, catalog).ToList();
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

    private static bool BelongsToModule(EndpointInfo endpoint, ModuleDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ProjectName)
            && (endpoint.ProjectName.Equals(descriptor.Name, StringComparison.OrdinalIgnoreCase)
                || endpoint.ProjectName.Equals(descriptor.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var path = endpoint.RelativePath.Replace('\\', '/');
        foreach (var root in descriptor.RootPaths)
        {
            var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
            if (prefix is "./" or "/")
            {
                return true;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || path.Equals(root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return descriptor.RelatedFiles.Any(f =>
            f.Equals(endpoint.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

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

        // Simple glob → regex (* and ?)
        var regex = "^" + Regex.Escape(p).Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return haystacks.Any(h => Regex.IsMatch(h, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }
}
