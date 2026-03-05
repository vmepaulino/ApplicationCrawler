using System.Text.Json;
using System.Text.Json.Serialization;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Reporting;

public sealed class JsonReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public void Write(AnalysisReport report, string? outputPath)
    {
        // Derive the .json path: report.html ? report.json, or fall back to app directory
        var jsonPath = DeriveJsonPath(report, outputPath);

        Console.WriteLine("\ud83d\udcc4 Generating JSON report...");

        var payload = BuildPayload(report);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(jsonPath, json);
        Console.WriteLine($"\ud83d\udcc4 JSON report saved: {Path.GetFullPath(jsonPath)}");
    }

    private static string DeriveJsonPath(AnalysisReport report, string? outputPath)
    {
        if (!string.IsNullOrEmpty(outputPath))
            return Path.ChangeExtension(outputPath, ".json");

        return Path.Combine(report.AppPath, "angular-analysis-report.json");
    }

    /// <summary>
    /// Build a serialization-friendly payload that includes summary stats
    /// alongside the raw data, making it easy for AI models to consume.
    /// </summary>
    private static object BuildPayload(AnalysisReport report)
    {
        var allFindings = report.AllFindings.ToList();

        return new
        {
            report.AppPath,
            report.Timestamp,

            Summary = new
            {
                AngularVersion = report.ProjectMetadata.AngularVersion,
                TotalFindings = allFindings.Count,
                Critical = allFindings.Count(f => f.Severity == Severity.Critical),
                High = allFindings.Count(f => f.Severity == Severity.High),
                Medium = allFindings.Count(f => f.Severity == Severity.Medium),
                Low = allFindings.Count(f => f.Severity == Severity.Low),
                Info = allFindings.Count(f => f.Severity == Severity.Info),
                FunctionalAreas = report.FunctionalAreas.Count,
                TotalComponents = report.FunctionalAreas.Sum(a => a.Components.Count),
                TotalServices = report.FunctionalAreas.Sum(a => a.Services.Count),
                TotalTsFiles = report.FunctionalAreas.Sum(a => a.TsFileCount),
                TotalHtmlFiles = report.FunctionalAreas.Sum(a => a.HtmlFileCount),
                SecurityPostureScore = report.SecurityPosture.Count > 0
                    ? $"{report.SecurityPosture.Count(p => p.InPlace)}/{report.SecurityPosture.Count}"
                    : null,
                NpmAuditVulnerabilities = report.NpmAudit.WasRun ? report.NpmAudit.Vulnerabilities.Count : (int?)null,
                LintIssues = report.LintResult.WasRun ? report.LintResult.TotalIssues : (int?)null,
                TypeErrors = report.TypeCheckResult.WasRun ? report.TypeCheckResult.ErrorCount : (int?)null,
                OutdatedPackages = report.LibraryVersions.Count,
            },

            report.ProjectMetadata,
            report.SecurityFindings,
            report.StorageFindings,
            report.ApiFindings,
            report.DesignFindings,
            report.LibraryFindings,
            report.ApiEndpoints,
            report.SecurityPosture,
            report.LibraryVersions,
            report.FunctionalAreas,
            report.NpmAudit,
            report.LintResult,
            report.TypeCheckResult,
        };
    }
}
