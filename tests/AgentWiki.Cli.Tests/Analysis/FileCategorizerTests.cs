using AgentWiki.Core.Analysis;
using AgentWiki.Core.Models;

namespace AgentWiki.Cli.Tests.Analysis;

public sealed class FileCategorizerTests
{
    [Theory]
    [InlineData("src/Foo/Bar.cs", FileCategory.SourceCode)]
    [InlineData("src/Foo/Component.razor", FileCategory.SourceCode)]
    [InlineData("README.md", FileCategory.Documentation)]
    [InlineData("docs/guide.md", FileCategory.Documentation)]
    [InlineData("appsettings.json", FileCategory.Configuration)]
    [InlineData("src/Foo/Foo.csproj", FileCategory.Configuration)]
    [InlineData("Directory.Build.props", FileCategory.Configuration)]
    [InlineData("tests/Foo.Tests/BarTests.cs", FileCategory.Tests)]
    [InlineData("src/Foo.Tests/ServiceTests.cs", FileCategory.Tests)]
    [InlineData("diagrams/flow.mmd", FileCategory.Diagrams)]
    [InlineData("assets/logo.png", FileCategory.Other)]
    [InlineData("Policies/all-operations-policy.xml", FileCategory.Configuration)]
    [InlineData("azure-build-pipeline-api.yml", FileCategory.Configuration)]
    [InlineData(".github/workflows/ci.yml", FileCategory.Configuration)]
    public void Categorize_ReturnsExpectedCategory(string path, FileCategory expected)
    {
        FileCategorizer.Categorize(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Policies/all-operations-policy.xml", true)]
    [InlineData("azure-build-pipeline-api.yml", true)]
    [InlineData("src/Foo/Bar.cs", false)]
    public void IsInfrastructurePath_DetectsDeployArtifacts(string path, bool expected)
    {
        FileCategorizer.IsInfrastructurePath(path).ShouldBe(expected);
    }

    [Theory]
    [InlineData(".cs", "C#")]
    [InlineData(".ts", "TypeScript")]
    [InlineData(".py", "Python")]
    [InlineData(".unknown", null)]
    public void DetectLanguage_MapsKnownExtensions(string extension, string? expected)
    {
        FileCategorizer.DetectLanguage(extension).ShouldBe(expected);
    }

    [Fact]
    public void IsBinaryExtension_DetectsCommonBinaries()
    {
        FileCategorizer.IsBinaryExtension(".dll").ShouldBeTrue();
        FileCategorizer.IsBinaryExtension(".png").ShouldBeTrue();
        FileCategorizer.IsBinaryExtension(".cs").ShouldBeFalse();
    }
}
