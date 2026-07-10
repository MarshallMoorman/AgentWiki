using AgentWiki.Cli.Services;

namespace AgentWiki.Cli.Tests.Services;

public sealed class DotEnvLoaderTests
{
    [Fact]
    public void Load_SetsUnsetVariablesOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), "agentwiki-dotenv-" + Guid.NewGuid().ToString("N") + ".env");
        var keyA = "AGENTWIKI_TEST_DOTENV_A_" + Guid.NewGuid().ToString("N")[..8];
        var keyB = "AGENTWIKI_TEST_DOTENV_B_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            Environment.SetEnvironmentVariable(keyB, "from-shell");
            File.WriteAllText(path,
                $"""
                # comment
                {keyA}=from-dotenv
                {keyB}=from-dotenv-should-not-win
                export AGENTWIKI_TEST_EXPORT_X=1
                """);

            var count = DotEnvLoader.Load(path);
            count.ShouldBeGreaterThanOrEqualTo(1);

            Environment.GetEnvironmentVariable(keyA).ShouldBe("from-dotenv");
            Environment.GetEnvironmentVariable(keyB).ShouldBe("from-shell");
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyA, null);
            Environment.SetEnvironmentVariable(keyB, null);
            Environment.SetEnvironmentVariable("AGENTWIKI_TEST_EXPORT_X", null);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
