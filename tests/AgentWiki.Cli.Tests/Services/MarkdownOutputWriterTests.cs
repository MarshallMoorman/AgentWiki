using AgentWiki.App.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class MarkdownOutputWriterTests
{
    [Fact]
    public async Task WriteAsync_DryRun_ClassifiesCreateUpdateUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "agentwiki-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var existing = Path.Combine(root, "architecture.md");
            await File.WriteAllTextAsync(existing, "# Architecture\n\nold\n");

            var unchangedPath = Path.Combine(root, "index.md");
            var unchangedContent = "# Index\n\nstable\n";
            await File.WriteAllTextAsync(unchangedPath, unchangedContent);

            var writer = new MarkdownOutputWriter(NullLogger<MarkdownOutputWriter>.Instance);
            var result = await writer.WriteAsync(
                root,
                [
                    new WikiSection("index", "Index", "index.md", unchangedContent),
                    new WikiSection("architecture", "Arch", "architecture.md", "# Architecture\n\nnew\n"),
                    new WikiSection("api", "API", "api-endpoints.md", "# API\n\nroutes\n")
                ],
                dryRun: true);

            result.IsDryRun.ShouldBeTrue();
            result.WouldCreate.ShouldContain("api-endpoints.md");
            result.WouldUpdate.ShouldContain("architecture.md");
            result.Unchanged.ShouldContain("index.md");
            result.ChangeCount.ShouldBe(2);
            File.Exists(Path.Combine(root, "api-endpoints.md")).ShouldBeFalse();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }
}
