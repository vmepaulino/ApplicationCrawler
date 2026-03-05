using System.Text.RegularExpressions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

/// <summary>
/// Runs <c>npx tsc --noEmit</c> to find TypeScript compilation errors
/// without producing output files.
/// </summary>
internal static class TypeCheckAnalyzer
{
    // tsc error format: src/app/foo.ts(12,5): error TS2345: Argument of type ...
    private static readonly Regex ErrorPattern = new(
        @"^(.+?)\((\d+),(\d+)\):\s*error\s+(TS\d+):\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static TypeCheckResult Run(string appPath)
    {
        var result = new TypeCheckResult();

        if (!File.Exists(Path.Combine(appPath, "tsconfig.json")))
        {
            result.Error = "No tsconfig.json found";
            return result;
        }

        var (stdout, stderr, exitCode) = CliRunner.Run(appPath, "npx", "tsc --noEmit", 120_000, progressLabel: "tsc --noEmit");

        // tsc writes errors to stdout
        var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;

        if (string.IsNullOrWhiteSpace(output))
        {
            if (exitCode == 0)
            {
                result.WasRun = true;
                return result; // No errors — clean compile
            }

            result.Error = "tsc produced no output";
            return result;
        }

        result.WasRun = true;

        foreach (Match match in ErrorPattern.Matches(output))
        {
            var filePath = match.Groups[1].Value.Trim();
            var relativePath = filePath;
            try { relativePath = Path.GetRelativePath(appPath, Path.Combine(appPath, filePath)); }
            catch { }

            result.Errors.Add(new TypeCheckError
            {
                FilePath = relativePath,
                Line = int.TryParse(match.Groups[2].Value, out var ln) ? ln : 0,
                Column = int.TryParse(match.Groups[3].Value, out var col) ? col : 0,
                Code = match.Groups[4].Value,
                Message = match.Groups[5].Value.Trim()
            });
        }

        result.ErrorCount = result.Errors.Count;

        return result;
    }
}
