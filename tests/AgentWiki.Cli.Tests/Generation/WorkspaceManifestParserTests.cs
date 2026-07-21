using AgentWiki.Core;
using AgentWiki.Core.Generation;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class WorkspaceManifestParserTests
{
    private static readonly string FullFixture = """
        # Workspace contribution manifest

        ## Purpose

        This file is the **human-owned** contract for LoanView.

        ## Maintenance rules

        1. Humans edit this file.
        2. Do not delete required section headings.

        ---

        ## Layer

        experience

        ## Team

        @elevate-lms-loanview

        ## Applications / Services

        - LoanView.Api — Experience API for loan presentation
        - LoanView.Client — Client library for LoanView.Api

        ## Brands

        Rise, Shine, Blueprint

        ## Responsibilities

        - Loan presentation and servicing views
        - Experience API over loan domain data

        ## Route work here when

        - Changing Loan View UI contracts
        - Story mentions LoanView

        ## Do not route work here when

        - Pure domain rules with no experience surface

        ## Related systems

        - Loan domain API
        - Identity / auth gateway

        ## Keywords

        loan-view, loanview, experience-api, lms

        ## Additional context

        Prefer vertical slices through Api → BusinessLogic.
        """;

    [Fact]
    public void Parse_FullFixture_ExtractsAllFields()
    {
        var doc = WorkspaceManifestParser.Parse(FullFixture);

        doc.Present.ShouldBeTrue();
        doc.Purpose.ShouldNotBeNull();
        doc.Purpose.ShouldContain("human-owned");
        doc.MaintenanceRules.ShouldNotBeNull();
        doc.Layer.ShouldBe("experience");
        doc.Team.ShouldBe("@elevate-lms-loanview");
        doc.Applications.Count.ShouldBe(2);
        doc.Applications[0].Name.ShouldBe("LoanView.Api");
        doc.Applications[0].Description.ShouldContain("Experience API");
        doc.Applications[1].Name.ShouldBe("LoanView.Client");
        doc.Brands.ShouldBe(["Rise", "Shine", "Blueprint"]);
        doc.Responsibilities.Count.ShouldBe(2);
        doc.RouteWhen.Count.ShouldBe(2);
        doc.DoNotRouteWhen.Count.ShouldBe(1);
        doc.RelatedSystems.Count.ShouldBe(2);
        doc.Keywords.ShouldContain("loan-view");
        doc.Keywords.ShouldContain("experience-api");
        doc.AdditionalContext.ShouldNotBeNull();
        doc.AdditionalContext.ShouldContain("vertical slices");
        doc.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_Brands_BulletList_NormalizesKnownCase()
    {
        var md = """
            ## Brands

            - rise
            - SHINE
            - elastic
            """;

        var doc = WorkspaceManifestParser.Parse(md);
        doc.Brands.ShouldBe(["Rise", "Shine", "Elastic"]);
    }

    [Fact]
    public void Parse_Brands_UnknownToken_PreservedWithWarning()
    {
        var md = """
            ## Brands

            Rise, CustomBrand
            """;

        var doc = WorkspaceManifestParser.Parse(md);
        doc.Brands.ShouldContain("Rise");
        doc.Brands.ShouldContain("CustomBrand");
        doc.Warnings.ShouldContain(w => w.Contains("Unknown brand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_Applications_MultiItem()
    {
        var md = """
            ## Applications / Services

            - Payments.Api — HTTP API
            - Payments.Worker
            Payments.Contracts - shared DTOs
            """;

        var doc = WorkspaceManifestParser.Parse(md);
        doc.Applications.Count.ShouldBe(3);
        doc.Applications[0].Name.ShouldBe("Payments.Api");
        doc.Applications[1].Name.ShouldBe("Payments.Worker");
        doc.Applications[1].Description.ShouldBeNull();
        doc.Applications[2].Name.ShouldBe("Payments.Contracts");
    }

    [Fact]
    public void Parse_MissingSections_EmitsWarnings()
    {
        var md = """
            # Workspace contribution manifest

            ## Purpose

            Only purpose here.
            """;

        var doc = WorkspaceManifestParser.Parse(md);
        doc.Purpose.ShouldContain("Only purpose");
        doc.Layer.ShouldBeNull();
        doc.Brands.ShouldBeEmpty();
        doc.Applications.ShouldBeEmpty();
        doc.Warnings.ShouldContain(w => w.Contains("Layer", StringComparison.OrdinalIgnoreCase));
        doc.Warnings.ShouldContain(w => w.Contains("Brands", StringComparison.OrdinalIgnoreCase));
        doc.Warnings.ShouldContain(w => w.Contains("Applications", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_EmptyLayerBrandsApps_Warnings()
    {
        var md = """
            ## Layer

            ## Team

            ## Applications / Services

            ## Brands

            ## Keywords
            """;

        var doc = WorkspaceManifestParser.Parse(md);
        doc.Layer.ShouldBeNull();
        doc.Brands.ShouldBeEmpty();
        doc.Applications.ShouldBeEmpty();
        doc.Warnings.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Parse_ScaffoldTemplate_DoesNotTreatPlaceholdersAsData()
    {
        var template = WorkspaceManifestScaffold.BuildTemplate();
        var doc = WorkspaceManifestParser.Parse(template);

        // Scaffold lists all layers/brands as hints — parser should not invent real values
        doc.Layer.ShouldBeNull();
        doc.Team.ShouldBeNull();
        doc.Applications.ShouldBeEmpty();
        doc.Brands.ShouldBeEmpty();
        doc.Keywords.ShouldBeEmpty();
    }

    [Fact]
    public void BuildTemplate_ContainsRequiredHeadingsAndBrands()
    {
        var template = WorkspaceManifestScaffold.BuildTemplate();
        template.ShouldContain("## Purpose");
        template.ShouldContain("## Maintenance rules");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingLayer}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingTeam}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingApplications}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingBrands}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingResponsibilities}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingRouteWhen}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingDoNotRoute}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingRelatedSystems}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingKeywords}");
        template.ShouldContain($"## {Constants.WorkspaceManifest.HeadingAdditionalContext}");
        foreach (var brand in Constants.WorkspaceManifest.KnownBrands)
        {
            template.ShouldContain(brand);
        }
    }

    [Fact]
    public async Task EnsureAsync_ScaffoldsOnce_NeverOverwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-manifest-" + Guid.NewGuid().ToString("N"));
        var wiki = Path.Combine(root, "docs", "wiki");
        Directory.CreateDirectory(wiki);

        try
        {
            var first = await WorkspaceManifestScaffold.EnsureAsync(wiki);
            first.Success.ShouldBeTrue();
            first.Created.ShouldBeTrue();
            File.Exists(first.Path!).ShouldBeTrue();

            var original = await File.ReadAllTextAsync(first.Path!);
            await File.WriteAllTextAsync(first.Path!, original + "\n## Layer\n\ndomain\n");

            var second = await WorkspaceManifestScaffold.EnsureAsync(wiki);
            second.Success.ShouldBeTrue();
            second.SkippedExisting.ShouldBeTrue();
            second.Created.ShouldBeFalse();

            var after = await File.ReadAllTextAsync(first.Path!);
            after.ShouldContain("domain");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureAsync_DryRun_DoesNotWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-manifest-dry-" + Guid.NewGuid().ToString("N"));
        var wiki = Path.Combine(root, "docs", "wiki");
        Directory.CreateDirectory(wiki);

        try
        {
            var result = await WorkspaceManifestScaffold.EnsureAsync(wiki, dryRun: true);
            result.Success.ShouldBeTrue();
            result.DryRun.ShouldBeTrue();
            File.Exists(result.Path!).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_PresentFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-manifest-" + Guid.NewGuid().ToString("N") + ".md");
        var doc = await WorkspaceManifestParser.LoadAsync(path);
        doc.Present.ShouldBeFalse();
        doc.Warnings.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task LoadAsync_RoundTripFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "manifest-rt-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            await File.WriteAllTextAsync(path, FullFixture);
            var doc = await WorkspaceManifestParser.LoadAsync(path);
            doc.Present.ShouldBeTrue();
            doc.Layer.ShouldBe("experience");
            doc.Brands.ShouldContain("Rise");
            doc.Applications.Count.ShouldBe(2);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ResolvePath_UsesWikiRootAndConstantFileName()
    {
        var path = WorkspaceManifestScaffold.ResolvePath("/tmp/repo/docs/wiki");
        path.Replace('\\', '/').ShouldEndWith("docs/wiki/" + Constants.WorkspaceManifest.FileName);
    }
}
