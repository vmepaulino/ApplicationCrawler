using AngularAppAnalyser.Abstractions;

namespace AngularAppAnalyser.Analyzers;

public sealed class ApiCommunicationStep : IAnalysisStep
{
    public string Name => "Analyzing API communication & extracting endpoints";
    public string Icon => "\ud83c\udf10";
    public int Order => 40;

    public void Execute(AnalysisContext context)
    {
        context.Report.ApiFindings = ApiCommunicationAnalyzer.Analyze(context);
        context.Report.ApiEndpoints = ApiCommunicationAnalyzer.ExtractEndpoints(context);
    }

    public string GetSummary(AnalysisContext context) =>
        $"{context.Report.ApiFindings.Count} finding(s), {context.Report.ApiEndpoints.Count} API endpoint(s)";
}

public sealed class DesignStep : IAnalysisStep
{
    public string Name => "Analyzing application design & browser resources";
    public string Icon => "\ud83c\udfd7\ufe0f";
    public int Order => 50;

    public void Execute(AnalysisContext context)
    {
        context.Report.DesignFindings = DesignAnalyzer.Analyze(context);
    }

    public string GetSummary(AnalysisContext context) =>
        $"{context.Report.DesignFindings.Count} design finding(s)";
}

public sealed class AppStructureStep : IAnalysisStep
{
    public string Name => "Analyzing app structure, services & components";
    public string Icon => "\ud83e\udde9";
    public int Order => 55;

    public void Execute(AnalysisContext context)
    {
        context.Report.FunctionalAreas = AppStructureAnalyzer.Analyze(context);
    }

    public string GetSummary(AnalysisContext context)
    {
        var areas = context.Report.FunctionalAreas;
        var ts = areas.Sum(a => a.TsFileCount);
        var html = areas.Sum(a => a.HtmlFileCount);
        var svc = areas.Sum(a => a.Services.Count);
        var comp = areas.Sum(a => a.Components.Count);
        var issues = areas.Sum(a => a.Services.Sum(s => s.Issues.Count));
        var allComps = areas.SelectMany(a => a.Components).ToList();
        var avg = allComps.Count > 0
            ? allComps.Average(c => c.HtmlScore > 0 ? (c.TsScore + c.HtmlScore) / 2.0 : c.TsScore) : 0;
        return $"{areas.Count} area(s), {ts} .ts, {html} .html, {comp} component(s) avg {avg:F1}/5, {svc} service(s), {issues} issue(s)";
    }
}

public sealed class LibraryHealthStep : IAnalysisStep
{
    public string Name => "Analyzing library health";
    public string Icon => "\ud83d\udcda";
    public int Order => 60;

    public void Execute(AnalysisContext context)
    {
        context.Report.LibraryFindings = LibraryAnalyzer.Analyze(context.Report.ProjectMetadata);
    }

    public string GetSummary(AnalysisContext context) =>
        $"{context.Report.LibraryFindings.Count} finding(s)";
}

public sealed class LibraryVersionStep : IAnalysisStep
{
    public string Name => "Checking library versions (npm outdated)";
    public string Icon => "\ud83d\udd04";
    public int Order => 70;

    public void Execute(AnalysisContext context)
    {
        context.Report.LibraryVersions = LibraryAnalyzer.CheckVersions(context.AppPath);
    }

    public string GetSummary(AnalysisContext context)
    {
        var v = context.Report.LibraryVersions;
        return $"{v.Count} outdated, {v.Count(x => x.UpdateType == "major")} major";
    }
}

public sealed class SecurityPostureStep : IAnalysisStep
{
    public string Name => "Building security posture summary";
    public string Icon => "\ud83d\udd10";
    public int Order => 80;

    public void Execute(AnalysisContext context)
    {
        context.Report.SecurityPosture = SecurityPostureAnalyzer.Build(context);
    }

    public string GetSummary(AnalysisContext context)
    {
        var p = context.Report.SecurityPosture;
        return $"{p.Count(x => x.InPlace)}/{p.Count} security controls in place";
    }
}

// ????????????????????????????????????????????????????????????
// Tool-based analysis steps (npm audit, ESLint, tsc)
// ????????????????????????????????????????????????????????????

public sealed class NpmAuditStep : IAnalysisStep
{
    public string Name => "Running npm audit (dependency vulnerabilities)";
    public string Icon => "\ud83d\udd12";
    public int Order => 82;

    public void Execute(AnalysisContext context)
    {
        context.Report.NpmAudit = NpmAuditAnalyzer.Run(context.AppPath);
    }

    public string GetSummary(AnalysisContext context)
    {
        var r = context.Report.NpmAudit;
        if (!r.WasRun) return r.Error ?? "skipped";
        var critical = r.Vulnerabilities.Count(v => v.Severity is "critical");
        var high = r.Vulnerabilities.Count(v => v.Severity is "high");
        return $"{r.Vulnerabilities.Count} vulnerable package(s) \u2014 {critical} critical, {high} high";
    }
}

public sealed class LinterStep : IAnalysisStep
{
    public string Name => "Running ESLint / ng lint (code quality)";
    public string Icon => "\ud83e\uddf9";
    public int Order => 84;

    public void Execute(AnalysisContext context)
    {
        context.Report.LintResult = LinterAnalyzer.Run(context);
    }

    public string GetSummary(AnalysisContext context)
    {
        var r = context.Report.LintResult;
        if (!r.WasRun) return r.Error ?? "skipped";
        return $"{r.TotalIssues} issue(s) in {r.Files.Count} file(s) \u2014 {r.ErrorCount} errors, {r.WarningCount} warnings ({r.ToolUsed})";
    }
}

public sealed class TypeCheckStep : IAnalysisStep
{
    public string Name => "Running TypeScript type check (tsc --noEmit)";
    public string Icon => "\ud83d\udccf";
    public int Order => 86;

    public void Execute(AnalysisContext context)
    {
        context.Report.TypeCheckResult = TypeCheckAnalyzer.Run(context.AppPath);
    }

    public string GetSummary(AnalysisContext context)
    {
        var r = context.Report.TypeCheckResult;
        if (!r.WasRun) return r.Error ?? "skipped";
        return r.ErrorCount == 0 ? "clean \u2014 no type errors" : $"{r.ErrorCount} type error(s)";
    }
}
