using AgentWiki.Core.Models;

namespace AgentWiki.Core.Abstractions;

/// <summary>
/// Guardrail pipeline that cleans structured generation output and rendered Markdown
/// (absolute paths, noisy dependencies, invented deprecation language, broken links).
/// Runs after LLM and offline generation steps.
/// </summary>
public interface IWikiPostProcessor
{
    /// <summary>
    /// Cleans an architecture document in place (paths, deprecation language, free-form markdown).
    /// </summary>
    WikiPostProcessResult ProcessArchitecture(ArchitectureDocument document, WikiPostProcessContext context);

    /// <summary>
    /// Cleans a module plan in place (root / related path lists).
    /// </summary>
    WikiPostProcessResult ProcessModulePlan(ModulePlan plan, WikiPostProcessContext context);

    /// <summary>
    /// Cleans a module document in place.
    /// </summary>
    WikiPostProcessResult ProcessModule(ModuleDocument document, WikiPostProcessContext context);

    /// <summary>
    /// Cleans a cross-cutting document in place.
    /// </summary>
    WikiPostProcessResult ProcessCrossCutting(CrossCuttingDocument document, WikiPostProcessContext context);

    /// <summary>
    /// Cleans rendered section Markdown. Returns a new list with updated content.
    /// </summary>
    (IReadOnlyList<WikiSection> Sections, WikiPostProcessResult Result) ProcessSections(
        IReadOnlyList<WikiSection> sections,
        WikiPostProcessContext context);
}
