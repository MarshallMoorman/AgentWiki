using AgentWiki.App.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class WorkspaceConfigLoaderTests
{
    [Fact]
    public async Task LoadAsync_ValidConfig_Succeeds()
    {
        var root = CreateTempDir();
        try
        {
            var dir = Path.Combine(root, ".agentwiki");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "workspace.json"),
                """
                {
                  "name": "Lending Core",
                  "description": "Test",
                  "outputPath": "docs/knowledge-base",
                  "members": [
                    { "id": "a", "path": "../A", "label": "Service A", "role": "service" },
                    { "id": "b", "remote": "https://github.com/org/B.git", "branch": "main" }
                  ]
                }
                """);

            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var result = await loader.LoadAsync(root);

            result.Success.ShouldBeTrue(result.Error);
            result.Config.ShouldNotBeNull();
            result.Config!.Name.ShouldBe("Lending Core");
            result.Config.Members.Count.ShouldBe(2);
            result.Config.Members[0].Id.ShouldBe("a");
            result.Config.Members[1].Remote.ShouldContain("github.com");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingFile_Fails()
    {
        var root = CreateTempDir();
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var result = await loader.LoadAsync(root);
            result.Success.ShouldBeFalse();
            result.Error.ShouldContain("not found");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Validate_DuplicateIds_Fails()
    {
        var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
        var config = new WorkspaceConfig
        {
            Name = "X",
            Members =
            [
                new WorkspaceMember { Id = "dup", Path = "../A" },
                new WorkspaceMember { Id = "dup", Path = "../B" }
            ]
        };

        var result = loader.Validate(config);
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("Duplicate");
    }

    [Fact]
    public void Validate_MissingPathAndRemote_Fails()
    {
        var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
        var config = new WorkspaceConfig
        {
            Name = "X",
            Members = [new WorkspaceMember { Id = "orphan" }]
        };

        var result = loader.Validate(config);
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("path");
    }

    [Fact]
    public async Task SaveAndReload_RoundTrips()
    {
        var root = CreateTempDir();
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var path = Path.Combine(root, ".agentwiki", "workspace.json");
            var config = new WorkspaceConfig
            {
                Name = "RoundTrip",
                Description = "desc",
                Members =
                [
                    new WorkspaceMember { Id = "svc", Path = "./svc", Role = "service" }
                ]
            };

            await loader.SaveAsync(config, path);
            var loaded = await loader.LoadAsync(root);
            loaded.Success.ShouldBeTrue(loaded.Error);
            loaded.Config!.Name.ShouldBe("RoundTrip");
            loaded.Config.Members.Single().Id.ShouldBe("svc");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "aw-ws-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // ignore
        }
    }
}
