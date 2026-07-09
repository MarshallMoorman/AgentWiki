using AgentWiki.Core.Models;

namespace AgentWiki.Core.Analysis;

/// <summary>
/// Categorizes repository files and detects languages from path/extension heuristics.
/// </summary>
public static class FileCategorizer
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".fsx", ".fsi",
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        ".py", ".pyi", ".rb", ".php", ".go", ".rs", ".java", ".kt", ".kts", ".scala",
        ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx", ".m", ".mm", ".swift",
        ".sql", ".ps1", ".psm1", ".sh", ".bash", ".zsh",
        ".r", ".jl", ".lua", ".dart", ".ex", ".exs", ".erl", ".hs", ".clj",
        ".razor", ".cshtml", ".vbhtml"
    };

    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".mdx", ".rst", ".txt", ".adoc", ".asciidoc"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf",
        ".xml", ".config", ".props", ".targets", ".ruleset",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore",
        ".env", ".env.example", ".npmrc", ".nvmrc"
    };

    private static readonly HashSet<string> DiagramExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mmd", ".mermaid", ".puml", ".plantuml", ".dot", ".drawio", ".dio"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".so", ".dylib", ".a", ".o", ".obj",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        ".pdf", ".zip", ".gz", ".tar", ".7z", ".rar", ".nupkg", ".snupkg",
        ".woff", ".woff2", ".ttf", ".eot", ".mp3", ".mp4", ".wav", ".avi",
        ".class", ".jar", ".war", ".ear", ".bin", ".dat", ".db", ".sqlite"
    };

    private static readonly Dictionary<string, string> ExtensionLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".fs"] = "F#",
        [".vb"] = "VB.NET",
        [".razor"] = "C#",
        [".cshtml"] = "C#",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".mjs"] = "JavaScript",
        [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".py"] = "Python",
        [".rb"] = "Ruby",
        [".go"] = "Go",
        [".rs"] = "Rust",
        [".java"] = "Java",
        [".kt"] = "Kotlin",
        [".swift"] = "Swift",
        [".cpp"] = "C++",
        [".cc"] = "C++",
        [".cxx"] = "C++",
        [".c"] = "C",
        [".h"] = "C/C++",
        [".hpp"] = "C++",
        [".sql"] = "SQL",
        [".ps1"] = "PowerShell",
        [".sh"] = "Shell",
        [".bash"] = "Shell",
        [".php"] = "PHP",
        [".dart"] = "Dart",
        [".md"] = "Markdown",
        [".yml"] = "YAML",
        [".yaml"] = "YAML",
        [".json"] = "JSON",
        [".xml"] = "XML",
        [".html"] = "HTML",
        [".css"] = "CSS",
        [".scss"] = "SCSS"
    };

    private static readonly HashSet<string> ConfigFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dockerfile", "makefile", "jenkinsfile", "procfile", "gemfile", "rakefile",
        "directory.build.props", "directory.build.targets", "directory.packages.props",
        "global.json", "nuget.config", "packages.config", "appsettings.json",
        "appsettings.development.json", "appsettings.production.json",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore",
        "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        "tsconfig.json", "jsconfig.json", "babel.config.js", "webpack.config.js",
        "omnisharp.json", ".tool-versions"
    };

    /// <summary>Returns true when the extension is treated as binary (skip line counts).</summary>
    public static bool IsBinaryExtension(string? extension) =>
        !string.IsNullOrEmpty(extension) && BinaryExtensions.Contains(extension);

    /// <summary>Maps a file extension to a human language name when known.</summary>
    public static string? DetectLanguage(string? extension) =>
        extension is not null && ExtensionLanguages.TryGetValue(extension, out var lang) ? lang : null;

    /// <summary>
    /// Classifies a relative path into a <see cref="FileCategory"/>.
    /// </summary>
    public static FileCategory Categorize(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var extension = Path.GetExtension(normalized);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lowerSegments = segments.Select(s => s.ToLowerInvariant()).ToArray();

        var inTestPath = lowerSegments.Any(IsTestSegment);
        var looksLikeTestFile = fileName.Contains(".Tests.", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".Test.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".spec.js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("_test.py", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("test_", StringComparison.OrdinalIgnoreCase);

        if (inTestPath || looksLikeTestFile)
        {
            return FileCategory.Tests;
        }

        if (DiagramExtensions.Contains(extension)
            || lowerSegments.Any(s => s is "diagrams" or "diagram"))
        {
            return FileCategory.Diagrams;
        }

        if (DocExtensions.Contains(extension)
            || lowerSegments.Any(s => s is "docs" or "doc" or "documentation")
            || fileName.Equals("readme.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("changelog.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("license", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("license.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("contributing.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("agents.md", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("claude.md", StringComparison.OrdinalIgnoreCase))
        {
            return FileCategory.Documentation;
        }

        if (ConfigExtensions.Contains(extension)
            || ConfigFileNames.Contains(fileName)
            || fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
        {
            return FileCategory.Configuration;
        }

        if (SourceExtensions.Contains(extension))
        {
            return FileCategory.SourceCode;
        }

        return FileCategory.Other;
    }

    private static bool IsTestSegment(string segment) =>
        segment is "test" or "tests" or "spec" or "specs" or "__tests__"
        || segment.EndsWith(".tests", StringComparison.Ordinal)
        || segment.EndsWith(".test", StringComparison.Ordinal)
        || segment.EndsWith("tests", StringComparison.Ordinal);
}
