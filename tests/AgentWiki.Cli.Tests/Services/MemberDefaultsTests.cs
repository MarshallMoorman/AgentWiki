using System.Text.Json;
using AgentWiki.App.Services;
using AgentWiki.Core;
using AgentWiki.Core.Generation;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class MemberDefaultsTests
{
    [Fact]
    public void CreateFullTemplate_HasCompleteConfigSurface()
    {
        var cfg = AgentWikiConfigDefaults.CreateFullTemplate();
        cfg.RepoPath.ShouldBe(".");
        cfg.OutputPath.ShouldBe(Constants.Paths.DefaultOutputPath);
        cfg.DefaultModel.ShouldBe(Constants.Config.DefaultModel);
        cfg.Provider.ShouldBe(Constants.Config.DefaultProvider);
        cfg.AgentMdPath.ShouldBe(Constants.Paths.DefaultAgentMdPath);
        cfg.MaxFilesToAnalyze.ShouldBe(Constants.Config.MaxFilesToAnalyze);
        cfg.LlmTimeoutSeconds.ShouldBe(Constants.Config.LlmTimeoutSeconds);
        cfg.MaxModules.ShouldBe(Constants.Config.MaxModules);
        cfg.EnableRoslynAnalysis.ShouldBeTrue();
        cfg.EnableApiEndpointDocs.ShouldBeTrue();
        cfg.IgnorePatterns.Count.ShouldBeGreaterThan(0);
        cfg.AzureOpenAI.ShouldNotBeNull();
        cfg.AzureOpenAI.Endpoint.ShouldNotBeNullOrWhiteSpace();
        cfg.OpenAI.ShouldNotBeNull();
        cfg.OpenAI.Model.ShouldBe(Constants.Config.DefaultModel);
    }

    [Fact]
    public void CreateFullTemplate_RoundTripsThroughJson()
    {
        var original = AgentWikiConfigDefaults.CreateFullTemplate();
        original.MaxModules = 12;
        original.ModuleRoots = ["src/Api"];
        original.ModelPricing["gpt-test"] = new ModelPricingEntry
        {
            InputUsdPerMillion = 1m,
            OutputUsdPerMillion = 2m
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        var json = JsonSerializer.Serialize(original, options);
        var round = JsonSerializer.Deserialize<AgentWikiConfig>(json, options);
        round.ShouldNotBeNull();
        round!.MaxModules.ShouldBe(12);
        round.ModuleRoots.ShouldContain("src/Api");
        round.ModelPricing.ContainsKey("gpt-test").ShouldBeTrue();
        round.AzureOpenAI.DeploymentName.ShouldBe(original.AzureOpenAI.DeploymentName);
        round.IgnorePatterns.Count.ShouldBe(original.IgnorePatterns.Count);
    }

    [Fact]
    public void CloneForMember_ForcesRepoPathDot()
    {
        var source = AgentWikiConfigDefaults.CreateFullTemplate();
        source.RepoPath = "/some/path";
        source.DefaultModel = "gpt-custom";
        var clone = AgentWikiConfigDefaults.CloneForMember(source);
        clone.RepoPath.ShouldBe(".");
        clone.DefaultModel.ShouldBe("gpt-custom");
    }

    [Fact]
    public void DescribeSecretsPresent_DetectsApiKeysWithoutValues()
    {
        var cfg = AgentWikiConfigDefaults.CreateFullTemplate();
        cfg.AzureOpenAI.ApiKey = "secret-value-not-logged";
        var notes = AgentWikiConfigDefaults.DescribeSecretsPresent(cfg);
        notes.ShouldContain(n => n.Contains("azureOpenAI.apiKey", StringComparison.Ordinal));
        notes.ShouldNotContain(n => n.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspaceInit_SeedsFullMemberDefaults()
    {
        var root = CreateTempDir();
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var init = new WorkspaceInitService(loader, NullLogger<WorkspaceInitService>.Instance);
            var result = await init.InitializeAsync(root, name: "Estate");
            result.Success.ShouldBeTrue(result.Error);

            var loaded = await loader.LoadAsync(root);
            loaded.Success.ShouldBeTrue(loaded.Error);
            loaded.Config!.MemberDefaults.ShouldNotBeNull();
            loaded.Config.MemberDefaults!.OutputPath.ShouldBe(Constants.Paths.DefaultOutputPath);
            loaded.Config.MemberDefaults.MaxModules.ShouldBe(Constants.Config.MaxModules);
            loaded.Config.MemberDefaults.AzureOpenAI.ShouldNotBeNull();
            loaded.Config.MemberWikiPolicy.ShouldNotBeNull();
            loaded.Config.MemberWikiPolicy.EnsureMissing.ShouldBeTrue();
            loaded.Config.MemberWikiPolicy.UpdateMembers.ShouldBe(Constants.Workspace.UpdateMembersNever);

            // JSON on disk should include memberDefaults section
            var json = await File.ReadAllTextAsync(loaded.Config.ConfigFilePath!);
            json.ShouldContain("memberDefaults");
            json.ShouldContain("memberWikiPolicy");
            json.ShouldContain("maxModules");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ApplyToMember_InitWritesDefaults_SecondCallDoesNotOverwrite()
    {
        var member = CreateTempDir();
        try
        {
            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var defaults = AgentWikiConfigDefaults.CreateFullTemplate();
            defaults.DefaultModel = "model-from-defaults";

            var first = await applier.ApplyToMemberAsync(member, defaults, forceReplace: false);
            first.Success.ShouldBeTrue(first.Error);
            first.Wrote.ShouldBeTrue();
            File.Exists(Path.Combine(member, ".agentwiki", "config.json")).ShouldBeTrue();

            var json = await File.ReadAllTextAsync(Path.Combine(member, ".agentwiki", "config.json"));
            json.ShouldContain("model-from-defaults");

            // User edits config
            defaults.DefaultModel = "should-not-apply";
            await File.WriteAllTextAsync(
                Path.Combine(member, ".agentwiki", "config.json"),
                """{"repoPath":".","defaultModel":"user-edited","provider":"offline"}""");

            var second = await applier.ApplyToMemberAsync(member, defaults, forceReplace: false);
            second.Success.ShouldBeTrue();
            second.Skipped.ShouldBeTrue();
            second.Wrote.ShouldBeFalse();

            var after = await File.ReadAllTextAsync(Path.Combine(member, ".agentwiki", "config.json"));
            after.ShouldContain("user-edited");
            after.ShouldNotContain("should-not-apply");
        }
        finally
        {
            TryDelete(member);
        }
    }

    [Fact]
    public async Task ApplyToMember_ForceReplace_Overwrites()
    {
        var member = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(member, ".agentwiki"));
            await File.WriteAllTextAsync(
                Path.Combine(member, ".agentwiki", "config.json"),
                """{"defaultModel":"old"}""");

            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var defaults = AgentWikiConfigDefaults.CreateFullTemplate();
            defaults.DefaultModel = "new-model";

            var result = await applier.ApplyToMemberAsync(member, defaults, forceReplace: true);
            result.Success.ShouldBeTrue(result.Error);
            result.Wrote.ShouldBeTrue();

            var json = await File.ReadAllTextAsync(Path.Combine(member, ".agentwiki", "config.json"));
            json.ShouldContain("new-model");
            json.ShouldNotContain("\"old\"");
        }
        finally
        {
            TryDelete(member);
        }
    }

    [Fact]
    public async Task ApplyToMember_DryRun_NoWrite()
    {
        var member = CreateTempDir();
        try
        {
            var applier = new MemberConfigApplier(NullLogger<MemberConfigApplier>.Instance);
            var result = await applier.ApplyToMemberAsync(
                member,
                AgentWikiConfigDefaults.CreateFullTemplate(),
                dryRun: true);
            result.Success.ShouldBeTrue();
            result.WouldWrite.ShouldBeTrue();
            result.Wrote.ShouldBeFalse();
            File.Exists(Path.Combine(member, ".agentwiki", "config.json")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(member);
        }
    }

    [Fact]
    public async Task SaveAndReload_MemberDefaultsRoundTrip()
    {
        var root = CreateTempDir();
        try
        {
            var loader = new WorkspaceConfigLoader(NullLogger<WorkspaceConfigLoader>.Instance);
            var path = Path.Combine(root, ".agentwiki", "workspace.json");
            var defaults = AgentWikiConfigDefaults.CreateFullTemplate();
            defaults.MaxModules = 9;
            var config = new WorkspaceConfig
            {
                Name = "RT",
                MemberDefaults = defaults,
                Members = [new WorkspaceMember { Id = "Elevate-LMS-LoanView", Path = "../Elevate-LMS-LoanView" }]
            };

            await loader.SaveAsync(config, path);
            var loaded = await loader.LoadAsync(root);
            loaded.Success.ShouldBeTrue(loaded.Error);
            loaded.Config!.MemberDefaults.ShouldNotBeNull();
            loaded.Config.MemberDefaults!.MaxModules.ShouldBe(9);
            loaded.Config.Members[0].Id.ShouldBe("Elevate-LMS-LoanView");
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-md-" + Guid.NewGuid().ToString("N"));
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
