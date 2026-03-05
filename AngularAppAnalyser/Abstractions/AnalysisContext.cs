using System.Text.RegularExpressions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Abstractions;

/// <summary>
/// Shared state passed to every <see cref="IAnalysisStep"/>.
/// Contains the report being built, the app path, and reusable utility methods.
/// </summary>
public sealed class AnalysisContext
{
    public string AppPath { get; }
    public AnalysisReport Report { get; }

    public AnalysisContext(string appPath)
    {
        AppPath = appPath;
        Report = new AnalysisReport { AppPath = appPath, Timestamp = DateTime.Now };
    }

    // ?? Shared file utilities ??????????????????????????????????

    public List<string> GetSourceFiles(string pattern)
    {
        var srcDir = Path.Combine(AppPath, "src");
        var searchDir = Directory.Exists(srcDir) ? srcDir : AppPath;

        try
        {
            return Directory.GetFiles(searchDir, pattern, SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}.angular{Path.DirectorySeparatorChar}"))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public string? FindFile(string fileName)
    {
        var srcPath = Path.Combine(AppPath, "src", fileName);
        if (File.Exists(srcPath)) return srcPath;

        var rootPath = Path.Combine(AppPath, fileName);
        if (File.Exists(rootPath)) return rootPath;

        try
        {
            var files = Directory.GetFiles(Path.Combine(AppPath, "src"), fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolve the app/ directory (checks {root}/app then {root}/src/app).</summary>
    public string? ResolveAppDirectory()
    {
        var appDir = Path.Combine(AppPath, "app");
        if (Directory.Exists(appDir)) return appDir;

        appDir = Path.Combine(AppPath, "src", "app");
        return Directory.Exists(appDir) ? appDir : null;
    }

    // ?? Shared pattern-scanning helper ?????????????????????????

    public static List<Finding> ScanFiles(
        IEnumerable<string> files,
        string appPath,
        IReadOnlyList<SecurityPattern> patterns,
        RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Multiline,
        string commentPrefix = "//")
    {
        var findings = new List<Finding>();

        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                var relativePath = Path.GetRelativePath(appPath, file);
                var lines = content.Split('\n');

                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(content, pattern.Pattern, options);
                    foreach (Match match in matches)
                    {
                        var lineNumber = content[..match.Index].Count(c => c == '\n') + 1;
                        var lineContent = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "";

                        if (lineContent.TrimStart().StartsWith(commentPrefix) ||
                            lineContent.TrimStart().StartsWith("*") ||
                            lineContent.TrimStart().StartsWith("<!--"))
                            continue;

                        findings.Add(new Finding
                        {
                            Title = pattern.Title,
                            Description = pattern.Description,
                            Severity = pattern.Severity,
                            Category = pattern.Category,
                            File = relativePath,
                            Line = lineNumber,
                            CodeSnippet = lineContent.Length > 120 ? lineContent[..120] + "..." : lineContent
                        });
                    }
                }
            }
            catch { }
        }

        return findings;
    }
}
