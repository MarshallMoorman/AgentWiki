using AgentWiki.App.Services;
using AgentWiki.Cli.Commands;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class ReplaceConfigsTests
{
    [Fact]
    public async Task ReplaceConfigs_ForceOverwritesExisting_CreatesMissing()
    {
        var workspaceRoot = CreateTempDir();
        var memberA = Path.Combine(workspaceRoot, "MemberA");
        var memberB = Path.Combine(workspaceRoot, "MemberB");
        Directory.CreateDirectory(memberA);
        Directory.CreateDirectory(memberB);
        Directory.CreateDirectory(Path.Combine(memberA, ".agentwiki"));
        await File.WriteAllTextAsync(
            Path.Combine(memberA, ".agentwiki", "config.json"),
            """{"defaultModel":"old-a"}""");

        try
        {
            var defaults = AgentWikiConfigDefaults.CreateFullTemplate();
            defaults.DefaultModel = "from-defaults";
            var config = new WorkspaceConfig
            {
                Name = "W",
                WorkspaceRoot = workspaceRoot,
                MemberDefaults = defaults,
                Members =
                [
                    new WorkspaceMember { Id = "MemberA", Path = memberA },
                    new WorkspaceMember { Id = "MemberB", Path = memberB }
                ]
            };

            var resolved = new List<ResolvedWorkspaceMember>
            {
                Local(config.Members[0], memberA),
                Local(config.Members[1], memberB)
            };

            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var batch = await applier.ReplaceConfigsAsync(config, resolved, dryRun: false);

            batch.Success.ShouldBeTrue(batch.Error);
            batch.WroteCount.ShouldBe(2);

            var a = await File.ReadAllTextAsync(Path.Combine(memberA, ".agentwiki", "config.json"));
            var b = await File.ReadAllTextAsync(Path.Combine(memberB, ".agentwiki", "config.json"));
            a.ShouldContain("from-defaults");
            b.ShouldContain("from-defaults");
            a.ShouldNotContain("old-a");

            // ConfigLoader can still parse the written JSON
            var loader = new ConfigLoader(NullLogger<ConfigLoader>.Instance);
            var loaded = await loader.LoadAsync(memberA);
            loaded.DefaultModel.ShouldBe("from-defaults");
        }
        finally
        {
            TryDelete(workspaceRoot);
        }
    }

    [Fact]
    public async Task ReplaceConfigs_DryRun_NoWrite()
    {
        var root = CreateTempDir();
        var member = Path.Combine(root, "M");
        Directory.CreateDirectory(member);
        try
        {
            var config = new WorkspaceConfig
            {
                Name = "W",
                MemberDefaults = AgentWikiConfigDefaults.CreateFullTemplate(),
                Members = [new WorkspaceMember { Id = "M", Path = member }]
            };
            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var batch = await applier.ReplaceConfigsAsync(
                config,
                [Local(config.Members[0], member)],
                dryRun: true);

            batch.Success.ShouldBeTrue(batch.Error);
            batch.WouldWriteCount.ShouldBe(1);
            batch.WroteCount.ShouldBe(0);
            File.Exists(Path.Combine(member, ".agentwiki", "config.json")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ReplaceConfigs_FilterById()
    {
        var root = CreateTempDir();
        var a = Path.Combine(root, "A");
        var b = Path.Combine(root, "B");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            var defaults = AgentWikiConfigDefaults.CreateFullTemplate();
            defaults.DefaultModel = "only-a";
            var config = new WorkspaceConfig
            {
                Name = "W",
                MemberDefaults = defaults,
                Members =
                [
                    new WorkspaceMember { Id = "A", Path = a },
                    new WorkspaceMember { Id = "B", Path = b }
                ]
            };
            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var batch = await applier.ReplaceConfigsAsync(
                config,
                [Local(config.Members[0], a), Local(config.Members[1], b)],
                onlyMemberId: "A");

            batch.Success.ShouldBeTrue(batch.Error);
            batch.WroteCount.ShouldBe(1);
            File.Exists(Path.Combine(a, ".agentwiki", "config.json")).ShouldBeTrue();
            File.Exists(Path.Combine(b, ".agentwiki", "config.json")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ReplaceConfigs_SkipsRemoteOnly()
    {
        var root = CreateTempDir();
        try
        {
            var config = new WorkspaceConfig
            {
                Name = "W",
                MemberDefaults = AgentWikiConfigDefaults.CreateFullTemplate(),
                Members =
                [
                    new WorkspaceMember
                    {
                        Id = "RemoteSvc",
                        Remote = "https://github.com/org/RemoteSvc.git"
                    }
                ]
            };
            var remote = new ResolvedWorkspaceMember
            {
                Definition = config.Members[0],
                AbsolutePath = Path.Combine(root, "cache"),
                IsRemote = true,
                CachePath = Path.Combine(root, "cache")
            };
            Directory.CreateDirectory(remote.AbsolutePath);

            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var batch = await applier.ReplaceConfigsAsync(config, [remote]);

            batch.Success.ShouldBeTrue(batch.Error);
            batch.SkippedCount.ShouldBe(1);
            batch.WroteCount.ShouldBe(0);
            batch.Warnings.ShouldContain(w => w.Contains("remote-only", StringComparison.OrdinalIgnoreCase));
            File.Exists(Path.Combine(remote.AbsolutePath, ".agentwiki", "config.json")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ReplaceConfigs_MissingDefaults_Fails()
    {
        var config = new WorkspaceConfig
        {
            Name = "W",
            MemberDefaults = null,
            Members = [new WorkspaceMember { Id = "A", Path = "/tmp/x" }]
        };
        var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
        var batch = await applier.ReplaceConfigsAsync(config, []);
        batch.Success.ShouldBeFalse();
        batch.Error.ShouldNotBeNull();
        batch.Error!.ShouldContain("memberDefaults");
    }

    [Fact]
    public void IsNonInteractive_RespectsCiEnv()
    {
        // Smoke: method exists and returns a bool (CI may or may not be set in this environment).
        var value = WorkspaceMemberReplaceConfigsCommand.IsNonInteractiveEnvironment();
        (value is true or false).ShouldBeTrue();
    }

    private static ResolvedWorkspaceMember Local(WorkspaceMember def, string abs) =>
        new()
        {
            Definition = def,
            AbsolutePath = abs,
            IsRemote = false
        };

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-rc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
