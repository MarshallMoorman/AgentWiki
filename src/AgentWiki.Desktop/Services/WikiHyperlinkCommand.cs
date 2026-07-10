using System.Diagnostics;
using System.Windows.Input;

namespace AgentWiki.Desktop.Services;

/// <summary>
/// Handles Markdown.Avalonia link clicks: opens http(s)/mailto externally,
/// and routes relative / local wiki paths to an in-app navigation callback.
/// </summary>
public sealed class WikiHyperlinkCommand : ICommand
{
    private readonly Func<string> _getBaseDirectory;
    private readonly Func<string, Task> _openLocalWikiPathAsync;

    public WikiHyperlinkCommand(
        Func<string> getBaseDirectory,
        Func<string, Task> openLocalWikiPathAsync)
    {
        _getBaseDirectory = getBaseDirectory;
        _openLocalWikiPathAsync = openLocalWikiPathAsync;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        parameter is string s && !string.IsNullOrWhiteSpace(s);

    public void Execute(object? parameter)
    {
        if (parameter is not string raw || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        // Fire-and-forget; Markdown.Avalonia invokes ICommand synchronously.
        _ = ExecuteAsync(raw.Trim());
    }

    public async Task ExecuteAsync(string href)
    {
        try
        {
            // Fragment-only anchors (same page) — no-op for now.
            if (href.StartsWith('#'))
            {
                return;
            }

            if (IsExternal(href))
            {
                OpenExternal(href);
                return;
            }

            // file:// URIs
            if (href.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(href, UriKind.Absolute, out var fileUri)
                && fileUri.IsFile)
            {
                await _openLocalWikiPathAsync(fileUri.LocalPath).ConfigureAwait(true);
                return;
            }

            // Absolute filesystem path
            if (Path.IsPathRooted(href) && (File.Exists(href) || Directory.Exists(href)))
            {
                await _openLocalWikiPathAsync(href).ConfigureAwait(true);
                return;
            }

            // Relative path — resolve against current document directory (or wiki root).
            var baseDir = _getBaseDirectory() ?? "";
            var cleaned = href.Split('#', 2)[0].Split('?', 2)[0];
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            // URL-decode common relative markdown links
            cleaned = Uri.UnescapeDataString(cleaned).Replace('/', Path.DirectorySeparatorChar);

            var candidate = Path.GetFullPath(Path.Combine(
                string.IsNullOrWhiteSpace(baseDir) ? Directory.GetCurrentDirectory() : baseDir,
                cleaned));

            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                await _openLocalWikiPathAsync(candidate).ConfigureAwait(true);
                return;
            }

            // Try with .md if bare path
            if (!cleaned.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var withMd = candidate + ".md";
                if (File.Exists(withMd))
                {
                    await _openLocalWikiPathAsync(withMd).ConfigureAwait(true);
                    return;
                }
            }

            // Last resort: open in OS (may still work for custom schemes).
            OpenExternal(href);
        }
        catch
        {
            // Never crash the desktop host on a bad link.
        }
    }

    private static bool IsExternal(string href)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
               && uri.Scheme is not ("file" or "about");
    }

    private static void OpenExternal(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // ignore
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
