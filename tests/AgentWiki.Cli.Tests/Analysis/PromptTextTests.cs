using AgentWiki.Core.Analysis;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class PromptTextTests
{
    [Fact]
    public void TruncateForLlm_LeavesShortTextUnchanged()
    {
        PromptText.TruncateForLlm("hello", 100).ShouldBe("hello");
    }

    [Fact]
    public void TruncateForLlm_TruncatesLongText()
    {
        var text = new string('a', 1000);
        var result = PromptText.TruncateForLlm(text, 100);
        result.Length.ShouldBeLessThanOrEqualTo(100);
        result.ShouldContain("truncated");
    }

    [Fact]
    public void BuildForLlm_RespectsMaxChars()
    {
        var files = Enumerable.Range(0, 200)
            .Select(i => new AgentWiki.Core.Models.RepoFile
            {
                RelativePath = $"src/File{i}.cs",
                AbsolutePath = $"/tmp/src/File{i}.cs",
                Category = AgentWiki.Core.Models.FileCategory.SourceCode,
                SizeBytes = 10,
                Extension = ".cs",
                Language = "C#",
                LineCount = 5,
                SelectedForAnalysis = true
            })
            .ToList();

        var stats = new AgentWiki.Core.Models.RepoStats
        {
            TotalFiles = files.Count,
            SelectedFiles = files.Count,
            TotalSizeBytes = 2000,
            TotalLines = 1000,
            FilesByCategory = new Dictionary<AgentWiki.Core.Models.FileCategory, int>
            {
                [AgentWiki.Core.Models.FileCategory.SourceCode] = files.Count
            },
            FilesByExtension = new Dictionary<string, int> { [".cs"] = files.Count },
            FilesByLanguage = new Dictionary<string, int> { ["C#"] = files.Count },
            TopFolders = [new AgentWiki.Core.Models.FolderStat("src", files.Count, 2000)],
            DetectedLanguages = ["C#"]
        };

        var summary = RepoSummaryBuilder.BuildForLlm("repo", "/tmp", stats, files, maxChars: 500);
        summary.Length.ShouldBeLessThanOrEqualTo(500);
    }
}
