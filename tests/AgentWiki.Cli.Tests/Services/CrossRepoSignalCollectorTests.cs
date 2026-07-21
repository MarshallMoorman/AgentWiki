using AgentWiki.App.Services;
using AgentWiki.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentWiki.Cli.Tests.Services;

public sealed class CrossRepoSignalCollectorTests
{
    [Fact]
    public async Task Collect_FindsSharedPackagesAndOwnership()
    {
        var a = CreateTempDir("a");
        var b = CreateTempDir("b");
        try
        {
            Directory.CreateDirectory(Path.Combine(a, "src"));
            Directory.CreateDirectory(Path.Combine(b, "src"));
            await File.WriteAllTextAsync(
                Path.Combine(a, "src", "A.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                    <ProjectReference Include="..\Shared\Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(b, "src", "B.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(a, "CODEOWNERS"), "* @team-a\n");
            await File.WriteAllTextAsync(
                Path.Combine(a, "openapi.json"),
                """{"openapi":"3.0.0","info":{"title":"A"}}""");

            var members = new List<WorkspaceMemberAnalysis>
            {
                MakeMember("svc-a", a),
                MakeMember("svc-b", b)
            };

            var collector = new CrossRepoSignalCollector(NullLogger<CrossRepoSignalCollector>.Instance);
            var signals = await collector.CollectAsync(members);

            signals.SharedPackages.ShouldContain(p =>
                p.PackageId == "Newtonsoft.Json" && p.MemberIds.Count == 2);
            signals.Ownership.ShouldContain(o => o.MemberId == "svc-a");
            signals.Contracts.ShouldContain(c => c.MemberId == "svc-a");
            signals.ProjectReferences.Count.ShouldBeGreaterThan(0);
        }
        finally
        {
            TryDelete(a);
            TryDelete(b);
        }
    }

    private static WorkspaceMemberAnalysis MakeMember(string id, string path) =>
        new()
        {
            Resolved = new ResolvedWorkspaceMember
            {
                Definition = new WorkspaceMember { Id = id, Path = path },
                AbsolutePath = path,
                IsRemote = false
            }
        };

    private static string CreateTempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aw-sig-{prefix}-" + Guid.NewGuid().ToString("N"));
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
