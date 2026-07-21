using AgentWiki.App.Services;
using AgentWiki.Cli.Commands;

namespace AgentWiki.Cli.Tests.Services;

public sealed class WorkspaceMemberIdTests
{
    [Theory]
    [InlineData("https://github.com/org/SharedDomain.git", "SharedDomain")]
    [InlineData("git@github.com:org/LoanService.git", "LoanService")]
    [InlineData("https://github.com/org/my_repo.git", "my_repo")]
    [InlineData("../LoanService", "LoanService")]
    [InlineData("/Users/me/dev/elevate/Payment.Api", "Payment.Api")]
    [InlineData("../Elevate-LMS-LoanView", "Elevate-LMS-LoanView")]
    public void DeriveMemberId_ExactRepoName(string source, string expected)
    {
        WorkspaceInitService.DeriveMemberId(source).ShouldBe(expected);
    }

    [Fact]
    public void DeriveMemberId_UniquesAgainstExisting()
    {
        var id = WorkspaceInitService.DeriveMemberId("../LoanService", ["LoanService"]);
        id.ShouldBe("LoanService-2");
    }

    [Fact]
    public void DeriveMemberId_PreservesCaseAndDots()
    {
        WorkspaceInitService.DeriveMemberId("../Elevate.LMS.LoanView")
            .ShouldBe("Elevate.LMS.LoanView");
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
