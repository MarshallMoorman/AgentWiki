using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class WikiPostProcessorTests
{
    private readonly WikiPostProcessor _sut = new();
    private readonly string _repoRoot = Path.Combine(Path.GetTempPath(), "agentwiki-postproc-repo");

    private WikiPostProcessContext Context(
        WikiPostProcessingMode mode = WikiPostProcessingMode.Lenient,
        bool obsolete = false,
        IReadOnlySet<string>? wikiPages = null) =>
        new()
        {
            RepoRoot = _repoRoot,
            WikiOutputRoot = Path.Combine(_repoRoot, "docs", "wiki"),
            Mode = mode,
            SourceHasObsoleteMarkers = obsolete,
            KnownWikiPages = wikiPages ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "architecture.md",
                "index.md",
                "modules/cli.md"
            },
            KnownRepoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "src/Cli/Program.cs",
                "src/Core/Models/Foo.cs"
            }
        };

    [Fact]
    public void ProcessModule_RewritesAbsolutePathsToRepoRelative()
    {
        var abs = Path.Combine(_repoRoot, "src", "Cli", "Program.cs");
        var doc = new ModuleDocument
        {
            Id = "cli",
            Title = "CLI",
            Purpose = $"Entry is at {abs}",
            EntryPoints = [abs],
            RelatedFiles = [Path.Combine(_repoRoot, "src", "Core", "Models", "Foo.cs")]
        };

        var result = _sut.ProcessModule(doc, Context());

        result.CorrectionCount.ShouldBeGreaterThan(0);
        result.Corrections.ShouldContain(c => c.RuleId == "path-relative");
        doc.EntryPoints[0].ShouldBe("src/Cli/Program.cs");
        doc.RelatedFiles[0].ShouldBe("src/Core/Models/Foo.cs");
        doc.Purpose.ShouldNotContain(_repoRoot);
        doc.Purpose.ShouldContain("src/Cli/Program.cs");
    }

    [Fact]
    public void ProcessModule_NormalizesFreeFormDependencyObjects()
    {
        var doc = new ModuleDocument
        {
            Id = "loans",
            Title = "Loans",
            Purpose = "Handles loans.",
            Dependencies =
            [
                "customers: Customer API; shared: Shared kernel",
                "customers: Customer API; shared: Shared kernel" // duplicate
            ]
        };

        var result = _sut.ProcessModule(doc, Context());

        result.Corrections.ShouldContain(c => c.RuleId == "deps-normalize");
        doc.Dependencies.Count.ShouldBe(2);
        doc.Dependencies.ShouldContain(d => d.Contains("customers", StringComparison.OrdinalIgnoreCase));
        doc.Dependencies.ShouldContain(d => d.Contains("shared", StringComparison.OrdinalIgnoreCase));
        // No raw "key: value; key: value" blob left
        doc.Dependencies.ShouldAllBe(d => !d.Contains(';'));
    }

    [Fact]
    public void ProcessModule_Lenient_NeutralizesInventedDeprecationLanguage()
    {
        var doc = new ModuleDocument
        {
            Id = "api",
            Title = "API",
            Purpose = "This module is deprecated and uses legacy patterns.",
            Gotchas = ["This is obsolete and will be removed soon."]
        };

        var result = _sut.ProcessModule(doc, Context(mode: WikiPostProcessingMode.Lenient, obsolete: false));

        result.Corrections.ShouldContain(c => c.RuleId == "deprecation-lenient");
        doc.Purpose.ShouldNotContain("deprecated", Case.Insensitive);
        doc.Purpose.ShouldContain("verify", Case.Insensitive);
        doc.Gotchas[0].ShouldNotContain("obsolete", Case.Insensitive);
    }

    [Fact]
    public void ProcessModule_Strict_DropsUnverifiedDeprecationClaims()
    {
        var doc = new ModuleDocument
        {
            Id = "api",
            Title = "API",
            Purpose = "Serves HTTP requests.",
            Gotchas = ["This API is deprecated."]
        };

        var result = _sut.ProcessModule(doc, Context(mode: WikiPostProcessingMode.Strict, obsolete: false));

        result.Corrections.ShouldContain(c => c.RuleId == "deprecation-strict");
        doc.Gotchas.ShouldBeEmpty();
        doc.Purpose.ShouldContain("HTTP");
    }

    [Fact]
    public void ProcessModule_KeepsDeprecationLanguage_WhenSourceHasObsoleteMarkers()
    {
        var doc = new ModuleDocument
        {
            Id = "api",
            Title = "API",
            Purpose = "This surface is deprecated in favor of v2.",
            Gotchas = ["Marked obsolete."]
        };

        var result = _sut.ProcessModule(doc, Context(obsolete: true));

        result.Corrections.ShouldNotContain(c => c.RuleId.StartsWith("deprecation", StringComparison.Ordinal));
        doc.Purpose.ShouldContain("deprecated", Case.Insensitive);
    }

    [Fact]
    public void ProcessSections_FixesMissingMdExtensionAndAbsoluteLinks()
    {
        var absArch = Path.Combine(_repoRoot, "docs", "wiki", "architecture.md");
        var sections = new List<WikiSection>
        {
            new(
                "index",
                "Index",
                "index.md",
                $"""
                # Index

                See [Architecture](architecture) and [CLI](modules/cli.md).
                Also [abs]({absArch}).
                """)
        };

        var (cleaned, result) = _sut.ProcessSections(sections, Context());

        result.CorrectionCount.ShouldBeGreaterThan(0);
        cleaned[0].Content.ShouldContain("[Architecture](architecture.md)");
        cleaned[0].Content.ShouldNotContain(absArch);
        cleaned[0].Content.ShouldContain("architecture.md");
    }

    [Fact]
    public void ProcessArchitecture_CleansFullMarkdownBlob()
    {
        var abs = Path.Combine(_repoRoot, "src", "App");
        var doc = new ArchitectureDocument
        {
            Title = "Arch",
            FullMarkdown = $"# Overview\n\nRoot at {abs}. This is a legacy system.\n"
        };

        var result = _sut.ProcessArchitecture(doc, Context());

        result.CorrectionCount.ShouldBeGreaterThan(0);
        doc.FullMarkdown.ShouldNotContain(_repoRoot);
        doc.FullMarkdown.ShouldContain("src/App");
        doc.FullMarkdown.ShouldNotContain("legacy", Case.Insensitive);
    }

    [Fact]
    public void ParseMode_DefaultsToLenient()
    {
        WikiPostProcessor.ParseMode(null).ShouldBe(WikiPostProcessingMode.Lenient);
        WikiPostProcessor.ParseMode("").ShouldBe(WikiPostProcessingMode.Lenient);
        WikiPostProcessor.ParseMode("strict").ShouldBe(WikiPostProcessingMode.Strict);
        WikiPostProcessor.ParseMode("STRICT").ShouldBe(WikiPostProcessingMode.Strict);
        WikiPostProcessor.ParseMode("lenient").ShouldBe(WikiPostProcessingMode.Lenient);
    }

    [Fact]
    public void ProcessModule_UnixHomeStylePaths_BecomeRelative()
    {
        // Simulates LLM inventing /Users/... paths under the repo root shape.
        var fakeAbs = "/Users/runner/work/repo/src/Cli/Program.cs";
        // Repo root that makes ToRepoRelative fall back to file name when outside root.
        var doc = new ModuleDocument
        {
            Id = "cli",
            Title = "CLI",
            Purpose = $"See {fakeAbs}",
            EntryPoints = [fakeAbs]
        };

        var result = _sut.ProcessModule(doc, Context());

        result.Corrections.ShouldContain(c => c.RuleId == "path-relative");
        // Outside repo → PathUtility falls back to file name
        doc.EntryPoints[0].ShouldBe("Program.cs");
        doc.Purpose.ShouldNotContain("/Users/");
    }
}
