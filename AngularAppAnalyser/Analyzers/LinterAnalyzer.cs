using System.Text.Json;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

/// <summary>
/// Runs ESLint per folder under src/app/ for incremental progress feedback.
/// Falls back to a single whole-project run if per-folder fails.
/// </summary>
internal static class LinterAnalyzer
{
    private const int PerFolderTimeoutMs = 60_000;
    private const int WholeProjectTimeoutMs = 180_000;

    public static LintResult Run(AnalysisContext ctx)
    {
        var appPath = ctx.AppPath;
        var result = new LintResult();

        // Determine which tool is available
        var tool = DetectTool(appPath);
        if (tool is null)
        {
            result.Error = "Neither ng lint nor ESLint available. Install @angular-eslint: ng add @angular-eslint/schematics";
            return result;
        }

        result.ToolUsed = tool.Value.label;

        // Resolve app folder for per-folder linting
        var appDir = ctx.ResolveAppDirectory();
        if (appDir is null)
        {
            // No src/app — run on whole project
            Console.WriteLine("   No src/app/ found, linting entire project...");
            return RunWholeProject(appPath, tool.Value, result);
        }

        // Collect folders to lint: root files + each sub-folder
        var folders = new List<(string path, string label)>();

        // Root-level files in src/app/
        var rootTs = Directory.GetFiles(appDir, "*.ts", SearchOption.TopDirectoryOnly);
        if (rootTs.Length > 0)
            folders.Add((appDir, "(root)"));

        foreach (var dir in Directory.GetDirectories(appDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            folders.Add((dir, name));
        }

        if (folders.Count == 0)
            return RunWholeProject(appPath, tool.Value, result);

        Console.WriteLine($"   Linting {folders.Count} folder(s) under {Path.GetRelativePath(appPath, appDir)}/");

        var folderIndex = 0;
        foreach (var (folderPath, folderLabel) in folders)
        {
            folderIndex++;
            var relativeFolderPath = Path.GetRelativePath(appPath, folderPath);

            var (stdout, stderr, exitCode) = CliRunner.Run(
                appPath,
                tool.Value.cmd,
                tool.Value.BuildArgs(relativeFolderPath),
                PerFolderTimeoutMs,
                progressLabel: $"[{folderIndex}/{folders.Count}] {folderLabel}");

            if (string.IsNullOrWhiteSpace(stdout) || !stdout.Contains('['))
            {
                if (exitCode == -2) // timed out
                    Console.WriteLine($"      \u26a0\ufe0f {folderLabel} — timed out, skipping");
                continue;
            }

            var before = result.Files.Count;
            ParseEslintJson(stdout, appPath, result);
            var added = result.Files.Count - before;
            var folderErrors = result.Files.Skip(before).Sum(f => f.Issues.Count(i => i.LintSeverity == "error"));
            var folderWarnings = result.Files.Skip(before).Sum(f => f.Issues.Count(i => i.LintSeverity == "warning"));

            if (added > 0)
                Console.WriteLine($"      \ud83d\udcc1 {folderLabel}: {added} file(s), {folderErrors}E / {folderWarnings}W");
        }

        result.WasRun = true;
        result.TotalIssues = result.ErrorCount + result.WarningCount;
        return result;
    }

    // ?? Tool detection ?????????????????????????????????????????

    private readonly record struct LintTool(string label, string cmd, Func<string, string> BuildArgs);

    private static LintTool? DetectTool(string appPath)
    {
        // Quick check: does angular.json have a lint target?
        var angularJson = Path.Combine(appPath, "angular.json");
        var hasNgLint = false;
        if (File.Exists(angularJson))
        {
            try
            {
                var content = File.ReadAllText(angularJson);
                hasNgLint = content.Contains("\"lint\"", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        if (hasNgLint)
        {
            return new LintTool(
                "ng lint (ESLint)",
                "npx",
                folder => $"ng lint --format json --lint-file-patterns \"{folder}/**/*.ts\"");
        }

        // Check for ESLint config files
        var eslintConfigs = new[] { ".eslintrc.json", ".eslintrc.js", ".eslintrc.yml", ".eslintrc", "eslint.config.js", "eslint.config.mjs" };
        var hasEslint = eslintConfigs.Any(c => File.Exists(Path.Combine(appPath, c)));

        if (hasEslint)
        {
            return new LintTool(
                "ESLint (direct)",
                "npx",
                folder => $"eslint \"{folder}\" --format json --ext .ts,.html --no-error-on-unmatched-pattern");
        }

        return null;
    }

    // ?? Fallback: whole-project lint ???????????????????????????

    private static LintResult RunWholeProject(string appPath, LintTool tool, LintResult result)
    {
        var args = tool.label.Contains("ng lint")
            ? "ng lint --format json"
            : "eslint . --format json --ext .ts,.html --no-error-on-unmatched-pattern";

        var (stdout, _, _) = CliRunner.Run(appPath, tool.cmd, args, WholeProjectTimeoutMs, progressLabel: "linting project");

        if (!string.IsNullOrWhiteSpace(stdout) && stdout.Contains('['))
        {
            ParseEslintJson(stdout, appPath, result);
            result.WasRun = true;
            result.TotalIssues = result.ErrorCount + result.WarningCount;
        }
        else
        {
            result.Error = "Lint produced no parseable output";
        }

        return result;
    }

    // ?? JSON parser ????????????????????????????????????????????

    private static void ParseEslintJson(string json, string appPath, LintResult result)
    {
        try
        {
            // ESLint JSON output can be preceded by Angular CLI warnings — find the array start
            var arrayStart = json.IndexOf('[');
            if (arrayStart < 0) return;
            json = json[arrayStart..];

            // Also trim any trailing garbage after the array
            var arrayEnd = json.LastIndexOf(']');
            if (arrayEnd >= 0) json = json[..(arrayEnd + 1)];

            using var doc = JsonDocument.Parse(json);

            foreach (var fileElement in doc.RootElement.EnumerateArray())
            {
                var filePath = "";
                if (fileElement.TryGetProperty("filePath", out var fp))
                    filePath = fp.GetString() ?? "";

                var relativePath = string.IsNullOrEmpty(filePath) ? filePath
                    : Path.GetRelativePath(appPath, filePath);

                if (!fileElement.TryGetProperty("messages", out var messages) ||
                    messages.GetArrayLength() == 0)
                    continue;

                var fileResult = new LintFileResult { FilePath = relativePath };

                foreach (var msg in messages.EnumerateArray())
                {
                    var issue = new LintIssue();

                    if (msg.TryGetProperty("line", out var line))
                        issue.Line = line.GetInt32();
                    if (msg.TryGetProperty("column", out var col))
                        issue.Column = col.GetInt32();
                    if (msg.TryGetProperty("ruleId", out var rule) && rule.ValueKind == JsonValueKind.String)
                        issue.RuleId = rule.GetString() ?? "";
                    if (msg.TryGetProperty("message", out var message))
                        issue.Message = message.GetString() ?? "";
                    if (msg.TryGetProperty("severity", out var sev))
                        issue.LintSeverity = sev.GetInt32() == 2 ? "error" : "warning";

                    fileResult.Issues.Add(issue);
                }

                result.Files.Add(fileResult);

                if (fileElement.TryGetProperty("errorCount", out var ec))
                    result.ErrorCount += ec.GetInt32();
                if (fileElement.TryGetProperty("warningCount", out var wc))
                    result.WarningCount += wc.GetInt32();
            }
        }
        catch { /* silently skip unparseable output from this folder */ }
    }
}
