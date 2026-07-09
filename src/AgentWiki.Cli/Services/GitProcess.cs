using System.Diagnostics;
using System.Text;

namespace AgentWiki.Cli.Services;

/// <summary>
/// Thin wrapper around the git CLI for AgentWiki services.
/// </summary>
internal static class GitProcess
{
    public static bool IsGitRepository(string repoPath) =>
        Directory.Exists(Path.Combine(repoPath, ".git"));

    public static async Task<string?> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            return null;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask.ConfigureAwait(false);
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} exited {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    public static async Task<string?> TryGetHeadShaAsync(string repoPath, CancellationToken cancellationToken)
    {
        if (!IsGitRepository(repoPath))
        {
            return null;
        }

        try
        {
            var output = await RunAsync(repoPath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }
}
