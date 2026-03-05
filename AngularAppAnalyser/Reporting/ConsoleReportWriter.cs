using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Reporting;

public sealed class ConsoleReportWriter : IReportWriter
{
    public void Write(AnalysisReport report, string? outputPath)
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("\ud83d\udcca ANALYSIS SUMMARY");
        Console.WriteLine(new string('=', 60));

        var allFindings = report.AllFindings.ToList();

        Console.WriteLine($"\n   Total findings: {allFindings.Count}");

        foreach (var group in allFindings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key))
        {
            var icon = group.Key switch { Severity.Critical => "\ud83d\udd34", Severity.High => "\ud83d\udfe0", Severity.Medium => "\ud83d\udfe1", Severity.Low => "\ud83d\udfe2", _ => "\u2139\ufe0f" };
            var color = group.Key switch { Severity.Critical => ConsoleColor.Red, Severity.High => ConsoleColor.DarkYellow, Severity.Medium => ConsoleColor.Yellow, Severity.Low => ConsoleColor.Green, _ => ConsoleColor.Gray };
            Console.ForegroundColor = color;
            Console.WriteLine($"   {icon} {group.Key}: {group.Count()}");
            Console.ResetColor();
        }

        Console.WriteLine();
        foreach (var group in allFindings.GroupBy(f => f.Category).OrderBy(g => g.Key))
            Console.WriteLine($"   \ud83d\udccb {group.Key}: {group.Count()} finding(s)");

        var criticalFindings = allFindings.Where(f => f.Severity is Severity.Critical or Severity.High).Take(10).ToList();
        if (criticalFindings.Count > 0)
        {
            Console.WriteLine($"\n   \ud83d\udd25 Top Critical/High Findings:");
            foreach (var f in criticalFindings)
            {
                var icon = f.Severity == Severity.Critical ? "\ud83d\udd34" : "\ud83d\udfe0";
                Console.WriteLine($"      {icon} [{f.Category}] {f.Title}");
                if (!string.IsNullOrEmpty(f.File))
                    Console.WriteLine($"         \ud83d\udcc4 {f.File}{(f.Line > 0 ? $":{f.Line}" : "")}");
            }
        }

        if (report.SecurityPosture.Count > 0)
        {
            Console.WriteLine($"\n   \ud83d\udd10 Security Posture:");
            foreach (var item in report.SecurityPosture)
            {
                Console.ForegroundColor = item.InPlace ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"      {(item.InPlace ? "\u2705" : "\u274c")} [{item.Area}] {item.Check}");
                Console.ResetColor();
            }
        }

        if (report.ApiEndpoints.Count > 0)
        {
            Console.WriteLine($"\n   \ud83c\udf10 Discovered API Endpoints ({report.ApiEndpoints.Count}):");
            foreach (var ep in report.ApiEndpoints.GroupBy(e => e.Url).Take(15))
            {
                var methods = string.Join("/", ep.Select(e => e.HttpMethod).Distinct());
                Console.WriteLine($"      {methods,-8} {ep.Key}");
            }
            if (report.ApiEndpoints.GroupBy(e => e.Url).Count() > 15)
                Console.WriteLine($"      ... and {report.ApiEndpoints.GroupBy(e => e.Url).Count() - 15} more");
        }

        if (report.LibraryVersions.Count > 0)
        {
            var majors = report.LibraryVersions.Where(v => v.UpdateType == "major").ToList();
            if (majors.Count > 0)
            {
                Console.WriteLine($"\n   \ud83d\udd04 Major Version Updates Available ({majors.Count}):");
                foreach (var v in majors.Take(10))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"      {v.Name}");
                    Console.ResetColor();
                    Console.WriteLine($"  {v.Current} \u2192 {v.Latest}");
                }
            }
        }

        if (report.FunctionalAreas.Count > 0)
        {
            var svcTotal = report.FunctionalAreas.Sum(a => a.Services.Count);
            var issueTotal = report.FunctionalAreas.Sum(a => a.Services.Sum(s => s.Issues.Count));
            var compTotal = report.FunctionalAreas.Sum(a => a.Components.Count);
            var tsTotal = report.FunctionalAreas.Sum(a => a.TsFileCount);
            var htmlTotal = report.FunctionalAreas.Sum(a => a.HtmlFileCount);
            Console.WriteLine($"\n   \ud83c\udfd7\ufe0f Application Structure ({report.FunctionalAreas.Count} area(s), {tsTotal} .ts, {htmlTotal} .html, {compTotal} component(s), {svcTotal} service(s), {issueTotal} issue(s)):");
            foreach (var area in report.FunctionalAreas)
            {
                var areaIssues = area.Services.Sum(s => s.Issues.Count);
                var areaAvg = area.Components.Count > 0
                    ? area.Components.Average(c => c.HtmlScore > 0 ? (c.TsScore + c.HtmlScore) / 2.0 : c.TsScore) : 0;
                Console.WriteLine($"      \ud83d\udcc1 {area.Name}/  {area.TsFileCount} .ts, {area.HtmlFileCount} .html, {area.ComponentCount} comp" +
                    (area.Components.Count > 0 ? $" (score {areaAvg:F1}/5)" : "") +
                    $", {area.Services.Count} svc" +
                    (areaIssues > 0 ? $", {areaIssues} issue(s)" : ""));
                if (area.ScannedFolders.Count > 0)
                    Console.WriteLine($"         \u2514 scanned: {string.Join(", ", area.ScannedFolders)}");
                foreach (var svc in area.Services.Where(s => s.Issues.Count > 0).Take(5))
                {
                    var worst = svc.Issues.Max(i => i.Severity);
                    Console.ForegroundColor = worst >= Severity.High ? ConsoleColor.Red : ConsoleColor.Yellow;
                    Console.WriteLine($"         \u2514 {svc.Name}.service  ({svc.Issues.Count} issue(s))");
                    Console.ResetColor();
                }
                foreach (var comp in area.Components.Where(c => (c.HtmlScore > 0 ? (c.TsScore + c.HtmlScore) / 2.0 : c.TsScore) <= 2).Take(3))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"         \u2514 {comp.Name}.component  score {(comp.HtmlScore > 0 ? (comp.TsScore + comp.HtmlScore) / 2.0 : comp.TsScore):F1}/5");
                    Console.ResetColor();
                }
            }
        }

        // npm audit
        if (report.NpmAudit.WasRun && report.NpmAudit.Vulnerabilities.Count > 0)
        {
            Console.WriteLine($"\n   \ud83d\udd12 npm audit ({report.NpmAudit.Vulnerabilities.Count} vulnerable packages):");
            foreach (var v in report.NpmAudit.Vulnerabilities
                .OrderByDescending(v => v.Severity == "critical")
                .ThenByDescending(v => v.Severity == "high")
                .Take(10))
            {
                var color = v.Severity switch { "critical" => ConsoleColor.Red, "high" => ConsoleColor.DarkYellow, "moderate" => ConsoleColor.Yellow, _ => ConsoleColor.Gray };
                Console.ForegroundColor = color;
                Console.Write($"      [{v.Severity}]");
                Console.ResetColor();
                Console.WriteLine($" {v.Name}{(string.IsNullOrEmpty(v.Title) ? "" : $" \u2014 {v.Title}")}{(v.IsDirect ? " (direct)" : "")}");
            }
            if (report.NpmAudit.Vulnerabilities.Count > 10)
                Console.WriteLine($"      ... and {report.NpmAudit.Vulnerabilities.Count - 10} more");
        }

        // ESLint / ng lint
        if (report.LintResult.WasRun && report.LintResult.TotalIssues > 0)
        {
            Console.WriteLine($"\n   \ud83e\uddf9 Lint ({report.LintResult.ToolUsed}): {report.LintResult.ErrorCount} errors, {report.LintResult.WarningCount} warnings in {report.LintResult.Files.Count} file(s):");
            foreach (var file in report.LintResult.Files
                .OrderByDescending(f => f.Issues.Count(i => i.LintSeverity == "error"))
                .Take(8))
            {
                var errors = file.Issues.Count(i => i.LintSeverity == "error");
                var warnings = file.Issues.Count(i => i.LintSeverity == "warning");
                Console.ForegroundColor = errors > 0 ? ConsoleColor.Red : ConsoleColor.Yellow;
                Console.WriteLine($"      {file.FilePath}  ({errors}E / {warnings}W)");
                Console.ResetColor();
            }
            if (report.LintResult.Files.Count > 8)
                Console.WriteLine($"      ... and {report.LintResult.Files.Count - 8} more file(s)");
        }

        // tsc type check
        if (report.TypeCheckResult.WasRun && report.TypeCheckResult.ErrorCount > 0)
        {
            Console.WriteLine($"\n   \ud83d\udccf TypeScript type errors ({report.TypeCheckResult.ErrorCount}):");
            foreach (var err in report.TypeCheckResult.Errors.Take(8))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"      {err.Code}");
                Console.ResetColor();
                Console.WriteLine($" {err.FilePath}:{err.Line} \u2014 {(err.Message.Length > 100 ? err.Message[..100] + "..." : err.Message)}");
            }
            if (report.TypeCheckResult.ErrorCount > 8)
                Console.WriteLine($"      ... and {report.TypeCheckResult.ErrorCount - 8} more");
        }
        else if (report.TypeCheckResult.WasRun)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n   \ud83d\udccf TypeScript: \u2705 clean \u2014 no type errors");
            Console.ResetColor();
        }
    }
}
