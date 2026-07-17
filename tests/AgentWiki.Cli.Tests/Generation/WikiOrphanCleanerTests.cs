using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Generation;

public sealed class WikiOrphanCleanerTests
{
    [Fact]
    public void ClearModuleAreas_RemovesMarkdownUnderModulesAndCrossCutting()
    {
        using var temp = new TempDir();
        var wiki = Path.Combine(temp.Path, "docs", "wiki");
        Directory.CreateDirectory(Path.Combine(wiki, "modules"));
        Directory.CreateDirectory(Path.Combine(wiki, "cross-cutting"));
        File.WriteAllText(Path.Combine(wiki, "index.md"), "# index\n");
        File.WriteAllText(Path.Combine(wiki, "modules", "old-a.md"), "# A\n");
        File.WriteAllText(Path.Combine(wiki, "modules", "old-b.md"), "# B\n");
        File.WriteAllText(Path.Combine(wiki, "cross-cutting", "logging.md"), "# L\n");

        var cleared = WikiOrphanCleaner.ClearModuleAreas(wiki);
        cleared.Count.ShouldBe(3);
        File.Exists(Path.Combine(wiki, "index.md")).ShouldBeTrue();
        Directory.EnumerateFiles(Path.Combine(wiki, "modules"), "*.md").Any().ShouldBeFalse();
        Directory.EnumerateFiles(Path.Combine(wiki, "cross-cutting"), "*.md").Any().ShouldBeFalse();
    }

    [Fact]
    public void RemoveOrphans_DeletesOnlyUnplannedAreaPages()
    {
        using var temp = new TempDir();
        var wiki = Path.Combine(temp.Path, "wiki");
        Directory.CreateDirectory(Path.Combine(wiki, "modules"));
        File.WriteAllText(Path.Combine(wiki, "modules", "keep.md"), "# K\n");
        File.WriteAllText(Path.Combine(wiki, "modules", "stale.md"), "# S\n");
        File.WriteAllText(Path.Combine(wiki, "index.md"), "# I\n");

        var deleted = WikiOrphanCleaner.RemoveOrphans(wiki, ["modules/keep.md", "index.md"]);
        deleted.ShouldBe(["modules/stale.md"]);
        File.Exists(Path.Combine(wiki, "modules", "keep.md")).ShouldBeTrue();
        File.Exists(Path.Combine(wiki, "modules", "stale.md")).ShouldBeFalse();
        File.Exists(Path.Combine(wiki, "index.md")).ShouldBeTrue();
    }

    [Fact]
    public void StabilizeModuleIds_ReusesExistingFilename()
    {
        using var temp = new TempDir();
        var wiki = Path.Combine(temp.Path, "wiki");
        Directory.CreateDirectory(Path.Combine(wiki, "modules"));
        File.WriteAllText(Path.Combine(wiki, "modules", "loan-management.md"), "# Loans\n");

        var plan = new ModulePlan
        {
            Modules =
            [
                new ModuleDescriptor
                {
                    Id = "loans",
                    Name = "Loan Management",
                    RelatedFiles = ["Api/Controllers/LoansController.cs"]
                }
            ]
        };

        ModuleIdStabilizer.StabilizeModuleIds(plan, wiki);
        plan.Modules[0].Id.ShouldBe("loan-management");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "agentwiki-orphan-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* ignore */ }
        }
    }
}
