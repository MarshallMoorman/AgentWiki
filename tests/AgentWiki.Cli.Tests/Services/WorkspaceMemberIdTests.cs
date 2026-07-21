using AgentWiki.App.Services;
using AgentWiki.Cli.Commands;

namespace AgentWiki.Cli.Tests.Services;

public sealed class WorkspaceMemberIdTests
{
    [Theory]
    [InlineData("https://github.com/org/SharedDomain.git", "shared-domain")]
    [InlineData("git@github.com:org/LoanService.git", "loan-service")]
    [InlineData("https://github.com/org/my_repo.git", "my-repo")]
    [InlineData("../LoanService", "loan-service")]
    [InlineData("/Users/me/dev/elevate/Payment.Api", "payment-api")]
    public void DeriveMemberId_FromPathOrRemote(string source, string expected)
    {
        WorkspaceInitService.DeriveMemberId(source).ShouldBe(expected);
    }

    [Fact]
    public void DeriveMemberId_UniquesAgainstExisting()
    {
        var id = WorkspaceInitService.DeriveMemberId("../LoanService", ["loan-service"]);
        id.ShouldBe("loan-service-2");
    }

    [Fact]
    public void TryResolveAddArgs_PathOnly()
    {
        var ok = WorkspaceAddCommand.TryResolveAddArgs(
            new WorkspaceAddSettings { IdOrPath = "../LoanService" },
            out var path,
            out var id,
            out var error);
        ok.ShouldBeTrue(error);
        path.ShouldBe("../LoanService");
        id.ShouldBeNull();
    }

    [Fact]
    public void TryResolveAddArgs_PathWithIdOption()
    {
        var ok = WorkspaceAddCommand.TryResolveAddArgs(
            new WorkspaceAddSettings { IdOrPath = "../LoanService", MemberId = "loan" },
            out var path,
            out var id,
            out _);
        ok.ShouldBeTrue();
        path.ShouldBe("../LoanService");
        id.ShouldBe("loan");
    }

    [Fact]
    public void TryResolveAddArgs_LegacyTwoPositionals()
    {
        var ok = WorkspaceAddCommand.TryResolveAddArgs(
            new WorkspaceAddSettings { IdOrPath = "loan-service", PathOrRemote = "../LoanService" },
            out var path,
            out var id,
            out _);
        ok.ShouldBeTrue();
        path.ShouldBe("../LoanService");
        id.ShouldBe("loan-service");
    }
}
