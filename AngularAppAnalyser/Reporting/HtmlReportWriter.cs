using System.Text;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Reporting;

public sealed class HtmlReportWriter : IReportWriter
{
    public void Write(AnalysisReport report, string? outputPath)
    {
        if (string.IsNullOrEmpty(outputPath)) return;

        Console.WriteLine("\ud83d\udcdd Generating HTML report...");
        var html = Build(report);
        File.WriteAllText(outputPath, html);
        Console.WriteLine($"\ud83d\udcc4 Report saved: {Path.GetFullPath(outputPath)}");
    }

    private static string Build(AnalysisReport report)
    {
        var html = new StringBuilder();
        var allFindings = report.AllFindings.ToList();
        var criticalCount = allFindings.Count(f => f.Severity == Severity.Critical);
        var highCount = allFindings.Count(f => f.Severity == Severity.High);
        var mediumCount = allFindings.Count(f => f.Severity == Severity.Medium);
        var lowCount = allFindings.Count(f => f.Severity == Severity.Low);

        AppendHead(html, report, criticalCount, highCount, mediumCount, lowCount, allFindings.Count);
        AppendPosture(html, report);
        AppendNpmAudit(html, report);
        AppendLintResults(html, report);
        AppendTypeCheckResults(html, report);
        AppendEndpoints(html, report);
        AppendLibraryVersions(html, report);
        AppendAppStructure(html, report);
        AppendCategoryFindings(html, allFindings);
        AppendDependencies(html, report);
        AppendFooter(html);

        return html.ToString();
    }

    // ?? Head + Summary Cards ???????????????????????????????????

    private static void AppendHead(StringBuilder html, AnalysisReport report, int critical, int high, int medium, int low, int total)
    {
        html.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='UTF-8'><meta name='viewport' content='width=device-width,initial-scale=1.0'>");
        html.AppendLine($"<title>Angular Security &amp; Health Report \u2014 {report.ProjectMetadata.Name}</title>");
        html.AppendLine("<style>");
        html.AppendLine(Css);
        html.AppendLine("</style></head><body><div class='container'>");
        html.AppendLine($"<div class='header'><h1>\ud83d\udee1\ufe0f Angular Security &amp; Health Report</h1><div class='header-meta'>{report.ProjectMetadata.Name} \u00b7 Angular {report.ProjectMetadata.AngularVersion ?? "unknown"} \u00b7 {report.Timestamp:yyyy-MM-dd HH:mm}</div></div>");
        html.AppendLine("<div class='summary-grid'>");
        html.AppendLine($"<div class='summary-card critical'><div class='number'>{critical}</div><div class='label'>Critical</div></div>");
        html.AppendLine($"<div class='summary-card high'><div class='number'>{high}</div><div class='label'>High</div></div>");
        html.AppendLine($"<div class='summary-card medium'><div class='number'>{medium}</div><div class='label'>Medium</div></div>");
        html.AppendLine($"<div class='summary-card low'><div class='number'>{low}</div><div class='label'>Low</div></div>");
        html.AppendLine($"<div class='summary-card'><div class='number'>{total}</div><div class='label'>Total</div></div>");
        html.AppendLine("</div>");
    }

    // ?? Security Posture ???????????????????????????????????????

    private static void AppendPosture(StringBuilder html, AnalysisReport report)
    {
        if (report.SecurityPosture.Count == 0) return;
        var pass = report.SecurityPosture.Count(p => p.InPlace);
        var fail = report.SecurityPosture.Count(p => !p.InPlace);
        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"posture\")'><div><h2>\ud83d\udd10 Security Posture</h2></div><div><span class='section-badge badge-low'>{pass} in place</span><span class='section-badge badge-critical'>{fail} missing</span><span class='toggle' id='toggle-posture'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content show' id='content-posture'><div class='posture-grid'>");
        foreach (var item in report.SecurityPosture)
        {
            var cls = item.InPlace ? "pass" : "fail";
            var icon = item.InPlace ? "\u2705" : "\u274c";
            html.AppendLine($"<div class='posture-item {cls}'><div class='posture-icon'>{icon}</div><div><div class='posture-area'>{Esc(item.Area)}</div><div class='posture-check'>{Esc(item.Check)}</div><div class='posture-detail'>{Esc(item.Details)}</div></div></div>");
        }
        html.AppendLine("</div></div></div>");
    }

    // ?? npm audit ??????????????????????????????????????????????

    private static void AppendNpmAudit(StringBuilder html, AnalysisReport report)
    {
        var r = report.NpmAudit;
        if (!r.WasRun) return;

        var critical = r.Vulnerabilities.Count(v => v.Severity == "critical");
        var high = r.Vulnerabilities.Count(v => v.Severity == "high");
        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"audit\")'><div><h2>\ud83d\udd12 npm audit</h2></div><div>");
        if (critical > 0) html.AppendLine($"<span class='section-badge badge-critical'>{critical} critical</span>");
        if (high > 0) html.AppendLine($"<span class='section-badge badge-high'>{high} high</span>");
        html.AppendLine($"<span class='section-badge badge-info'>{r.Vulnerabilities.Count} total</span><span class='toggle' id='toggle-audit'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-audit'><table class='deps-table'><thead><tr><th>Severity</th><th>Package</th><th>Advisory</th><th>Range</th><th>Direct?</th></tr></thead><tbody>");
        foreach (var v in r.Vulnerabilities.OrderByDescending(v => v.Severity == "critical").ThenByDescending(v => v.Severity == "high"))
        {
            var badge = v.Severity switch { "critical" => "badge-critical", "high" => "badge-high", "moderate" => "badge-medium", _ => "badge-low" };
            var titleLink = string.IsNullOrEmpty(v.Url) ? Esc(v.Title) : $"<a href='{Esc(v.Url)}' target='_blank'>{Esc(v.Title)}</a>";
            html.AppendLine($"<tr><td><span class='section-badge {badge}'>{Esc(v.Severity)}</span></td><td><strong>{Esc(v.Name)}</strong></td><td>{titleLink}</td><td><code>{Esc(v.Range)}</code></td><td>{(v.IsDirect ? "\u2705" : "")}</td></tr>");
        }
        html.AppendLine("</tbody></table></div></div>");
    }

    // ?? ESLint / ng lint ???????????????????????????????????????

    private static void AppendLintResults(StringBuilder html, AnalysisReport report)
    {
        var r = report.LintResult;
        if (!r.WasRun || r.TotalIssues == 0) return;

        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"lint\")'><div><h2>\ud83e\uddf9 {Esc(r.ToolUsed)}</h2></div><div>");
        if (r.ErrorCount > 0) html.AppendLine($"<span class='section-badge badge-critical'>{r.ErrorCount} errors</span>");
        html.AppendLine($"<span class='section-badge badge-medium'>{r.WarningCount} warnings</span><span class='section-badge badge-info'>{r.Files.Count} file(s)</span><span class='toggle' id='toggle-lint'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-lint'>");

        foreach (var file in r.Files.OrderByDescending(f => f.Issues.Count(i => i.LintSeverity == "error")).Take(50))
        {
            var fileId = $"lint-{file.FilePath.Replace(Path.DirectorySeparatorChar, '-').Replace('.', '-')}";
            var errors = file.Issues.Count(i => i.LintSeverity == "error");
            var warnings = file.Issues.Count(i => i.LintSeverity == "warning");
            var sevClass = errors > 0 ? "sev-high" : "sev-medium";
            html.AppendLine($"<div class='finding {sevClass}'>");
            html.AppendLine($"<div class='finding-header' onclick='toggleFinding(\"{fileId}\")'><div class='finding-title'>\ud83d\udcc4 {Esc(file.FilePath)} <span class='section-badge badge-critical'>{errors}E</span> <span class='section-badge badge-medium'>{warnings}W</span></div><span class='toggle' id='toggle-{fileId}'>\u25b6</span></div>");
            html.AppendLine($"<div class='finding-detail' id='detail-{fileId}'><table class='deps-table'><thead><tr><th>Sev</th><th>Line</th><th>Rule</th><th>Message</th></tr></thead><tbody>");
            foreach (var issue in file.Issues.OrderBy(i => i.Line))
            {
                var issueBadge = issue.LintSeverity == "error" ? "badge-critical" : "badge-medium";
                html.AppendLine($"<tr><td><span class='section-badge {issueBadge}'>{Esc(issue.LintSeverity)}</span></td><td>{issue.Line}:{issue.Column}</td><td><code>{Esc(issue.RuleId)}</code></td><td>{Esc(issue.Message)}</td></tr>");
            }
            html.AppendLine("</tbody></table></div></div>");
        }
        html.AppendLine("</div></div>");
    }

    // ?? TypeScript type check ??????????????????????????????????

    private static void AppendTypeCheckResults(StringBuilder html, AnalysisReport report)
    {
        var r = report.TypeCheckResult;
        if (!r.WasRun) return;

        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"tsc\")'><div><h2>\ud83d\udccf TypeScript Type Check</h2></div><div>");
        if (r.ErrorCount > 0) html.AppendLine($"<span class='section-badge badge-critical'>{r.ErrorCount} error(s)</span>");
        else html.AppendLine("<span class='section-badge badge-low'>\u2705 clean</span>");
        html.AppendLine("<span class='toggle' id='toggle-tsc'>\u25b6</span></div></div>");

        if (r.ErrorCount > 0)
        {
            html.AppendLine("<div class='section-content' id='content-tsc'><table class='deps-table'><thead><tr><th>Code</th><th>File</th><th>Line</th><th>Message</th></tr></thead><tbody>");
            foreach (var err in r.Errors.OrderBy(e => e.FilePath).ThenBy(e => e.Line))
                html.AppendLine($"<tr><td><code>{Esc(err.Code)}</code></td><td>{Esc(err.FilePath)}</td><td>{err.Line}:{err.Column}</td><td>{Esc(err.Message)}</td></tr>");
            html.AppendLine("</tbody></table></div>");
        }
        html.AppendLine("</div>");
    }



    // ?? API Endpoints ??????????????????????????????????????????

    private static void AppendEndpoints(StringBuilder html, AnalysisReport report)
    {
        if (report.ApiEndpoints.Count == 0) return;
        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"endpoints\")'><div><h2>\ud83c\udf10 Discovered API Endpoints</h2></div><div><span class='section-badge badge-info'>{report.ApiEndpoints.Count} call(s)</span><span class='toggle' id='toggle-endpoints'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-endpoints'><table class='deps-table'><thead><tr><th>Method</th><th>URL / Pattern</th><th>Service</th><th>File</th><th>Line</th></tr></thead><tbody>");
        foreach (var ep in report.ApiEndpoints.OrderBy(e => e.Url).ThenBy(e => e.HttpMethod))
            html.AppendLine($"<tr><td><span class='endpoint-method method-{ep.HttpMethod}'>{Esc(ep.HttpMethod)}</span></td><td><code>{Esc(ep.Url)}</code></td><td>{Esc(ep.ServiceName)}</td><td>{Esc(ep.File ?? "")}</td><td>{ep.Line}</td></tr>");
        html.AppendLine("</tbody></table></div></div>");
    }

    // ?? Library Versions ???????????????????????????????????????

    private static void AppendLibraryVersions(StringBuilder html, AnalysisReport report)
    {
        if (report.LibraryVersions.Count == 0) return;
        var major = report.LibraryVersions.Count(v => v.UpdateType == "major");
        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"versions\")'><div><h2>\ud83d\udd04 Library Versions</h2></div><div>");
        if (major > 0) html.AppendLine($"<span class='section-badge badge-critical'>{major} major</span>");
        html.AppendLine($"<span class='section-badge badge-info'>{report.LibraryVersions.Count} outdated</span><span class='toggle' id='toggle-versions'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-versions'><table class='deps-table'><thead><tr><th>Package</th><th>Current</th><th>Wanted</th><th>Latest</th><th>Update</th></tr></thead><tbody>");
        foreach (var v in report.LibraryVersions.OrderByDescending(v => v.UpdateType == "major").ThenBy(v => v.Name))
        {
            var badge = v.UpdateType switch { "major" => "version-major", "minor" => "version-minor", _ => "version-patch" };
            html.AppendLine($"<tr><td><strong>{Esc(v.Name)}</strong></td><td>{Esc(v.Current)}</td><td>{Esc(v.Wanted)}</td><td>{Esc(v.Latest)}</td><td><span class='version-badge {badge}'>{Esc(v.UpdateType)}</span></td></tr>");
        }
        html.AppendLine("</tbody></table></div></div>");
    }

    // ?? Application Structure ??????????????????????????????????

    private static void AppendAppStructure(StringBuilder html, AnalysisReport report)
    {
        if (report.FunctionalAreas.Count == 0) return;
        var svcTotal = report.FunctionalAreas.Sum(a => a.Services.Count);
        var issueTotal = report.FunctionalAreas.Sum(a => a.Services.Sum(s => s.Issues.Count));
        var compTotal = report.FunctionalAreas.Sum(a => a.Components.Count);
        var allComps = report.FunctionalAreas.SelectMany(a => a.Components).ToList();
        var avgScore = allComps.Count > 0 ? allComps.Average(c => c.HtmlScore > 0 ? (c.TsScore + c.HtmlScore) / 2.0 : c.TsScore) : 0;

        html.AppendLine("<div class='section'>");
        html.AppendLine($"<div class='section-header' onclick='toggleSection(\"appstruct\")'><div><h2>\ud83c\udfd7\ufe0f Application Structure &amp; Services</h2></div><div><span class='section-badge badge-info'>{report.FunctionalAreas.Count} area(s)</span><span class='section-badge badge-info'>{svcTotal} svc</span><span class='section-badge badge-info'>{compTotal} comp</span>");
        if (compTotal > 0) html.AppendLine($"<span class='section-badge badge-{(avgScore >= 4 ? "low" : avgScore >= 3 ? "medium" : "high")}'>avg {avgScore:F1}/5</span>");
        if (issueTotal > 0) html.AppendLine($"<span class='section-badge badge-high'>{issueTotal} issue(s)</span>");
        html.AppendLine("<span class='toggle' id='toggle-appstruct'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-appstruct'>");

        foreach (var area in report.FunctionalAreas)
        {
            html.AppendLine($"<div style='margin-bottom:20px;'><h3 style='margin-bottom:8px;'>\ud83d\udcc1 {Esc(area.Name)}/</h3>");
            html.Append($"<div style='font-size:13px;color:#7f8c8d;margin-bottom:12px;'><strong>{area.TsFileCount} .ts, {area.HtmlFileCount} .html</strong> &mdash; {area.ComponentCount} comp, {area.Services.Count} svc, {area.DirectiveCount} dir, {area.PipeCount} pipe, {area.GuardCount} guard, {area.ModuleCount} mod");
            if (area.ScannedFolders.Count > 0)
                html.Append($"<br/><small>Scanned sub-folders: {Esc(string.Join(", ", area.ScannedFolders))}</small>");
            html.AppendLine("</div>");

            // Services
            foreach (var svc in area.Services)
            {
                var worstSev = svc.Issues.Count > 0 ? svc.Issues.Max(i => i.Severity) : Severity.Info;
                var svcId = $"svc-{area.Name}-{svc.Name}".Replace("(", "").Replace(")", "");
                html.AppendLine($"<div class='finding sev-{(svc.Issues.Count > 0 ? worstSev.ToString().ToLower() : "info")}'>");
                html.AppendLine($"<div class='finding-header' onclick='toggleFinding(\"{svcId}\")'><div class='finding-title'>{Esc(svc.Name)}.service {(svc.Issues.Count > 0 ? $"<span class='section-badge badge-{worstSev.ToString().ToLower()}'>{svc.Issues.Count} issue(s)</span>" : "<span class='section-badge badge-low'>\u2713</span>")}</div><span class='toggle' id='toggle-{svcId}'>\u25b6</span></div>");
                html.AppendLine($"<div class='finding-detail' id='detail-{svcId}'>");
                html.AppendLine($"<div style='display:flex;gap:24px;flex-wrap:wrap;font-size:13px;color:#7f8c8d;margin-bottom:10px;'><span>\ud83d\udcc4 {Esc(svc.File)}</span><span>{(svc.IsProvidedInRoot ? "\u2705" : "\u274c")} providedIn:root</span><span>\ud83d\udd27 {svc.MethodCount} methods</span>{(svc.InjectedDependencies.Count > 0 ? $"<span>\ud83d\udc89 {Esc(string.Join(", ", svc.InjectedDependencies))}</span>" : "")}</div>");
                if (svc.Issues.Count > 0)
                {
                    html.AppendLine("<table class='deps-table'><thead><tr><th>Severity</th><th>Issue</th><th>Description</th><th>Line</th></tr></thead><tbody>");
                    foreach (var issue in svc.Issues.OrderByDescending(i => i.Severity))
                        html.AppendLine($"<tr><td><span class='section-badge badge-{issue.Severity.ToString().ToLower()}'>{issue.Severity}</span></td><td><strong>{Esc(issue.Title)}</strong></td><td>{Esc(issue.Description)}</td><td>{(issue.Line > 0 ? issue.Line.ToString() : "")}</td></tr>");
                    html.AppendLine("</tbody></table>");
                }
                html.AppendLine("</div></div>");
            }

            // Components
            if (area.Components.Count > 0)
            {
                html.AppendLine($"<h4 style='margin:16px 0 8px;'>\ud83e\udde9 Components ({area.Components.Count})</h4><div class='comp-grid'>");
                foreach (var comp in area.Components.OrderBy(c => (c.TsScore + c.HtmlScore) / 2.0))
                {
                    var avg = comp.HtmlScore > 0 ? (comp.TsScore + comp.HtmlScore) / 2.0 : comp.TsScore;
                    var compId = $"comp-{area.Name}-{comp.Name}".Replace("(", "").Replace(")", "");
                    var sev = avg >= 4 ? "low" : avg >= 3 ? "medium" : "high";
                    var rounded = (int)Math.Round(avg);
                    html.AppendLine($"<div class='finding sev-{sev}' style='margin-bottom:8px;'>");
                    html.AppendLine($"<div class='finding-header' onclick='toggleFinding(\"{compId}\")'><div class='finding-title'>{Esc(comp.Name)} <span class='score-bar' title='TS:{comp.TsScore}/5 HTML:{comp.HtmlScore}/5'>");
                    for (var p = 1; p <= 5; p++) html.Append(p <= rounded ? $"<span class='score-pip s{rounded}'></span>" : "<span class='score-pip'></span>");
                    html.Append($" <small>{avg:F1}/5</small></span>");
                    if (comp.IsStandalone) html.Append(" <span class='section-badge badge-low'>standalone</span>");
                    if (comp.UsesOnPush) html.Append(" <span class='section-badge badge-info'>OnPush</span>");
                    html.AppendLine($"</div><span class='toggle' id='toggle-{compId}'>\u25b6</span></div>");
                    html.AppendLine($"<div class='finding-detail' id='detail-{compId}'>");
                    html.Append($"<div style='font-size:13px;color:#7f8c8d;margin-bottom:8px;'>\ud83d\udcc4 {Esc(comp.TsFile)}");
                    if (comp.HtmlFile is not null) html.Append($" \u00b7 {Esc(comp.HtmlFile)}");
                    html.AppendLine("</div>");
                    html.AppendLine("<div style='display:flex;gap:32px;margin-bottom:10px;'>");
                    html.Append($"<div><strong>TS:</strong> <span class='score-bar'>");
                    for (var p = 1; p <= 5; p++) html.Append(p <= comp.TsScore ? $"<span class='score-pip s{comp.TsScore}'></span>" : "<span class='score-pip'></span>");
                    html.AppendLine($" {comp.TsScore}/5</span></div>");
                    if (comp.HtmlScore > 0)
                    {
                        html.Append("<div><strong>HTML:</strong> <span class='score-bar'>");
                        for (var p = 1; p <= 5; p++) html.Append(p <= comp.HtmlScore ? $"<span class='score-pip s{comp.HtmlScore}'></span>" : "<span class='score-pip'></span>");
                        html.AppendLine($" {comp.HtmlScore}/5</span></div>");
                    }
                    html.AppendLine("</div>");
                    if (comp.Dependencies.Count > 0) html.AppendLine($"<div style='font-size:12px;margin-bottom:8px;'>\ud83d\udc89 {Esc(string.Join(", ", comp.Dependencies))}</div>");
                    if (comp.TsModernTraits.Count + comp.HtmlModernTraits.Count > 0)
                        html.AppendLine($"<div class='trait-modern'>\u2705 Modern: {Esc(string.Join(", ", comp.TsModernTraits.Concat(comp.HtmlModernTraits)))}</div>");
                    if (comp.TsLegacyTraits.Count + comp.HtmlLegacyTraits.Count > 0)
                        html.AppendLine($"<div class='trait-legacy'>\u26a0\ufe0f Legacy: {Esc(string.Join(", ", comp.TsLegacyTraits.Concat(comp.HtmlLegacyTraits)))}</div>");
                    html.AppendLine("</div></div>");
                }
                html.AppendLine("</div>");
            }
            html.AppendLine("</div>");
        }
        html.AppendLine("</div></div>");
    }

    // ?? Category Findings ??????????????????????????????????????

    private static void AppendCategoryFindings(StringBuilder html, List<Finding> allFindings)
    {
        var categories = new[] { "Security", "Storage", "API Communication", "Application Design", "Libraries" };
        foreach (var category in categories)
        {
            var findings = allFindings.Where(f => f.Category == category).OrderByDescending(f => f.Severity).ToList();
            if (findings.Count == 0) continue;

            var catId = category.Replace(" ", "-").ToLower();
            var catCritical = findings.Count(f => f.Severity == Severity.Critical);
            var catHigh = findings.Count(f => f.Severity == Severity.High);
            var catIcon = category switch { "Security" => "\ud83d\udee1\ufe0f", "Storage" => "\ud83d\udcbe", "API Communication" => "\ud83c\udf10", "Application Design" => "\ud83c\udfd7\ufe0f", "Libraries" => "\ud83d\udcda", _ => "\ud83d\udccb" };

            html.AppendLine($"<div class='section'><div class='section-header' onclick='toggleSection(\"{catId}\")'><div><h2>{catIcon} {category}</h2></div><div>");
            if (catCritical > 0) html.AppendLine($"<span class='section-badge badge-critical'>{catCritical} critical</span>");
            if (catHigh > 0) html.AppendLine($"<span class='section-badge badge-high'>{catHigh} high</span>");
            html.AppendLine($"<span class='section-badge badge-info'>{findings.Count} total</span><span class='toggle' id='toggle-{catId}'>\u25b6</span></div></div>");
            html.AppendLine($"<div class='section-content' id='content-{catId}'>");

            for (var i = 0; i < findings.Count; i++)
            {
                var f = findings[i];
                var fId = $"{catId}-f{i + 1}";
                html.AppendLine($"<div class='finding sev-{f.Severity.ToString().ToLower()}'>");
                html.AppendLine($"<div class='finding-header' onclick='toggleFinding(\"{fId}\")'><div class='finding-title'><span class='section-badge badge-{f.Severity.ToString().ToLower()}'>{f.Severity}</span> {Esc(f.Title)}</div><span class='toggle' id='toggle-{fId}'>\u25b6</span></div>");
                html.AppendLine($"<div class='finding-detail' id='detail-{fId}'><p>{Esc(f.Description)}</p>");
                if (!string.IsNullOrEmpty(f.File)) html.AppendLine($"<div class='finding-meta'>\ud83d\udcc4 {Esc(f.File)}{(f.Line > 0 ? $":{f.Line}" : "")}</div>");
                if (!string.IsNullOrEmpty(f.CodeSnippet)) html.AppendLine($"<div class='code-snippet'>{Esc(f.CodeSnippet)}</div>");
                html.AppendLine("</div></div>");
            }
            html.AppendLine("</div></div>");
        }
    }

    // ?? Dependencies ???????????????????????????????????????????

    private static void AppendDependencies(StringBuilder html, AnalysisReport report)
    {
        html.AppendLine("<div class='section'><div class='section-header' onclick='toggleSection(\"deps\")'><div><h2>\ud83d\udce6 Dependencies</h2></div>");
        html.AppendLine($"<div><span class='section-badge badge-info'>{report.ProjectMetadata.Dependencies.Count + report.ProjectMetadata.DevDependencies.Count} packages</span><span class='toggle' id='toggle-deps'>\u25b6</span></div></div>");
        html.AppendLine("<div class='section-content' id='content-deps'>");
        html.AppendLine("<h3 style='margin-bottom:12px;'>Production</h3><table class='deps-table'><thead><tr><th>Package</th><th>Version</th></tr></thead><tbody>");
        foreach (var dep in report.ProjectMetadata.Dependencies.OrderBy(d => d.Key))
            html.AppendLine($"<tr><td>{Esc(dep.Key)}</td><td>{Esc(dep.Value)}</td></tr>");
        html.AppendLine("</tbody></table><h3 style='margin:20px 0 12px;'>Dev</h3><table class='deps-table'><thead><tr><th>Package</th><th>Version</th></tr></thead><tbody>");
        foreach (var dep in report.ProjectMetadata.DevDependencies.OrderBy(d => d.Key))
            html.AppendLine($"<tr><td>{Esc(dep.Key)}</td><td>{Esc(dep.Value)}</td></tr>");
        html.AppendLine("</tbody></table></div></div>");
    }

    // ?? Footer + JS ????????????????????????????????????????????

    private static void AppendFooter(StringBuilder html)
    {
        html.AppendLine("</div><script>");
        html.AppendLine("function toggleSection(id){var c=document.getElementById('content-'+id),t=document.getElementById('toggle-'+id);c.classList.toggle('show');t.classList.toggle('open');}");
        html.AppendLine("function toggleFinding(id){var d=document.getElementById('detail-'+id),t=document.getElementById('toggle-'+id);d.classList.toggle('show');t.classList.toggle('open');}");
        html.AppendLine("</script></body></html>");
    }

    private static string Esc(string text) =>
        text?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;") ?? "";

    // ?? CSS ????????????????????????????????????????????????????

    private const string Css = """
        :root { --critical:#e74c3c; --high:#e67e22; --medium:#f1c40f; --low:#27ae60; --info:#3498db; }
        * { box-sizing:border-box; margin:0; padding:0; }
        body { font-family:'Segoe UI',Tahoma,sans-serif; background:#f0f2f5; color:#2c3e50; padding:20px; }
        .container { max-width:1400px; margin:0 auto; }
        .header { background:linear-gradient(135deg,#dd1b16 0%,#c3002f 100%); color:#fff; padding:30px; border-radius:12px; margin-bottom:24px; }
        .header h1 { font-size:28px; margin-bottom:8px; } .header-meta { opacity:.85; font-size:14px; }
        .summary-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); gap:16px; margin-bottom:24px; }
        .summary-card { background:#fff; border-radius:10px; padding:20px; text-align:center; box-shadow:0 2px 8px rgba(0,0,0,.08); border-top:4px solid var(--info); }
        .summary-card.critical { border-top-color:var(--critical); } .summary-card.high { border-top-color:var(--high); }
        .summary-card.medium { border-top-color:var(--medium); } .summary-card.low { border-top-color:var(--low); }
        .summary-card .number { font-size:42px; font-weight:700; }
        .summary-card.critical .number { color:var(--critical); } .summary-card.high .number { color:var(--high); }
        .summary-card.medium .number { color:var(--medium); } .summary-card.low .number { color:var(--low); }
        .summary-card .label { font-size:13px; color:#7f8c8d; margin-top:4px; }
        .section { background:#fff; border-radius:10px; box-shadow:0 2px 8px rgba(0,0,0,.08); margin-bottom:24px; overflow:hidden; }
        .section-header { padding:18px 24px; cursor:pointer; display:flex; justify-content:space-between; align-items:center; }
        .section-header:hover { background:#f8f9fa; } .section-header h2 { font-size:20px; }
        .section-badge { display:inline-block; padding:3px 12px; border-radius:20px; font-size:12px; font-weight:600; color:#fff; margin-left:10px; }
        .badge-critical { background:var(--critical); } .badge-high { background:var(--high); } .badge-medium { background:var(--medium); } .badge-low { background:var(--low); } .badge-info { background:var(--info); }
        .section-content { display:none; padding:0 24px 24px; } .section-content.show { display:block; }
        .finding { border:1px solid #e9ecef; border-radius:8px; margin-bottom:12px; overflow:hidden; }
        .finding-header { padding:14px 18px; display:flex; justify-content:space-between; align-items:center; cursor:pointer; }
        .finding-header:hover { background:#f8f9fa; }
        .finding.sev-critical { border-left:4px solid var(--critical); } .finding.sev-high { border-left:4px solid var(--high); }
        .finding.sev-medium { border-left:4px solid var(--medium); } .finding.sev-low { border-left:4px solid var(--low); }
        .finding.sev-info { border-left:4px solid var(--info); }
        .finding-title { font-weight:600; font-size:15px; }
        .finding-detail { display:none; padding:0 18px 14px; font-size:14px; color:#555; } .finding-detail.show { display:block; }
        .finding-meta { font-size:12px; color:#95a5a6; margin-top:6px; }
        .code-snippet { background:#2d2d2d; color:#f8f8f2; padding:10px 14px; border-radius:6px; font-family:Consolas,monospace; font-size:13px; overflow-x:auto; margin-top:8px; white-space:pre; }
        .toggle { font-size:18px; transition:transform .2s; color:#95a5a6; } .toggle.open { transform:rotate(90deg); }
        .deps-table { width:100%; border-collapse:collapse; }
        .deps-table th { background:#34495e; color:#fff; padding:10px 14px; text-align:left; font-size:13px; }
        .deps-table td { padding:8px 14px; border-bottom:1px solid #ecf0f1; font-size:13px; }
        .deps-table tr:hover { background:#f8f9fa; }
        .posture-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(340px,1fr)); gap:12px; }
        .posture-item { display:flex; align-items:center; gap:12px; padding:14px; background:#fff; border-radius:8px; border:1px solid #e9ecef; }
        .posture-item.pass { border-left:4px solid var(--low); } .posture-item.fail { border-left:4px solid var(--critical); }
        .posture-icon { font-size:22px; } .posture-check { font-weight:600; font-size:14px; }
        .posture-area { font-size:11px; color:#95a5a6; text-transform:uppercase; letter-spacing:.5px; }
        .posture-detail { font-size:12px; color:#7f8c8d; margin-top:2px; }
        .endpoint-method { display:inline-block; padding:2px 8px; border-radius:4px; font-size:11px; font-weight:700; color:#fff; min-width:55px; text-align:center; }
        .method-GET { background:#27ae60; } .method-POST { background:#2980b9; } .method-PUT { background:#e67e22; } .method-DELETE { background:#e74c3c; } .method-PATCH { background:#8e44ad; } .method-FETCH { background:#7f8c8d; }
        .version-badge { display:inline-block; padding:2px 10px; border-radius:10px; font-size:11px; font-weight:600; }
        .version-major { background:#fde8e8; color:#c0392b; } .version-minor { background:#fff3cd; color:#856404; } .version-patch { background:#d4edda; color:#155724; }
        .score-bar { display:inline-flex; gap:3px; align-items:center; }
        .score-pip { width:16px; height:16px; border-radius:3px; background:#e9ecef; }
        .score-pip.s1 { background:var(--critical); } .score-pip.s2 { background:var(--high); } .score-pip.s3 { background:var(--medium); } .score-pip.s4 { background:#7dcea0; } .score-pip.s5 { background:var(--low); }
        .trait-modern { color:var(--low); font-size:12px; } .trait-legacy { color:var(--high); font-size:12px; }
        .comp-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(400px,1fr)); gap:12px; margin-top:12px; }
        """;
}
