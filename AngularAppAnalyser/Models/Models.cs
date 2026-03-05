namespace AngularAppAnalyser.Models;

public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public record SecurityPattern(string Pattern, string Title, string Description, Severity Severity, string Category);

public class AnalysisReport
{
    public required string AppPath { get; set; }
    public DateTime Timestamp { get; set; }
    public ProjectMetadata ProjectMetadata { get; set; } = new();
    public List<Finding> SecurityFindings { get; set; } = [];
    public List<Finding> StorageFindings { get; set; } = [];
    public List<Finding> ApiFindings { get; set; } = [];
    public List<Finding> DesignFindings { get; set; } = [];
    public List<Finding> LibraryFindings { get; set; } = [];
    public List<LibraryVersionInfo> LibraryVersions { get; set; } = [];
    public List<ApiEndpointInfo> ApiEndpoints { get; set; } = [];
    public List<SecurityPostureItem> SecurityPosture { get; set; } = [];
    public List<FunctionalArea> FunctionalAreas { get; set; } = [];

    // Tool-based analysis results
    public NpmAuditResult NpmAudit { get; set; } = new();
    public LintResult LintResult { get; set; } = new();
    public TypeCheckResult TypeCheckResult { get; set; } = new();

    public IEnumerable<Finding> AllFindings =>
        SecurityFindings
            .Concat(StorageFindings)
            .Concat(ApiFindings)
            .Concat(DesignFindings)
            .Concat(LibraryFindings);
}

public class ProjectMetadata
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? AngularVersion { get; set; }
    public Dictionary<string, string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DevDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AngularProjects { get; set; } = [];
    public bool HasBudgets { get; set; }
    public bool HasTsConfig { get; set; }
    public bool HasTsConfigApp { get; set; }
}

public class Finding
{
    public required string Title { get; set; }
    public required string Description { get; set; }
    public Severity Severity { get; set; }
    public string Category { get; set; } = "";
    public string? File { get; set; }
    public int Line { get; set; }
    public string? CodeSnippet { get; set; }
}

public class LibraryVersionInfo
{
    public string Name { get; set; } = "";
    public string Current { get; set; } = "";
    public string Wanted { get; set; } = "";
    public string Latest { get; set; } = "";
    public string UpdateType { get; set; } = "unknown";
}

public class ApiEndpointInfo
{
    public string HttpMethod { get; set; } = "";
    public string Url { get; set; } = "";
    public string? File { get; set; }
    public int Line { get; set; }
    public string ServiceName { get; set; } = "";
}

public class SecurityPostureItem
{
    public string Area { get; set; } = "";
    public string Check { get; set; } = "";
    public bool InPlace { get; set; }
    public string Details { get; set; } = "";
}

public class FunctionalArea
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<ServiceAnalysis> Services { get; set; } = [];
    public List<ComponentAnalysis> Components { get; set; } = [];
    public int TsFileCount { get; set; }
    public int HtmlFileCount { get; set; }
    public int ComponentCount { get; set; }
    public int DirectiveCount { get; set; }
    public int PipeCount { get; set; }
    public int GuardCount { get; set; }
    public int InterceptorCount { get; set; }
    public int ModuleCount { get; set; }
    /// <summary>Nested sub-folder paths that were scanned (relative to app root).</summary>
    public List<string> ScannedFolders { get; set; } = [];
}

public class ServiceAnalysis
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public List<ServiceIssue> Issues { get; set; } = [];
    public List<string> InjectedDependencies { get; set; } = [];
    public bool IsProvidedInRoot { get; set; }
    public int MethodCount { get; set; }
}

public class ServiceIssue
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Severity Severity { get; set; }
    public int Line { get; set; }
    public string CodeSnippet { get; set; } = "";
}

public class ComponentAnalysis
{
    public string Name { get; set; } = "";
    public string TsFile { get; set; } = "";
    public string? HtmlFile { get; set; }
    public int TsScore { get; set; }
    public int HtmlScore { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public List<string> TsModernTraits { get; set; } = [];
    public List<string> TsLegacyTraits { get; set; } = [];
    public List<string> HtmlModernTraits { get; set; } = [];
    public List<string> HtmlLegacyTraits { get; set; } = [];
    public bool IsStandalone { get; set; }
    public bool UsesOnPush { get; set; }
}

// ?? Tool-based analysis models ?????????????????????????????

public class NpmAuditResult
{
    public bool WasRun { get; set; }
    public string? Error { get; set; }
    public int TotalVulnerabilities { get; set; }
    public List<NpmAuditVulnerability> Vulnerabilities { get; set; } = [];
}

public class NpmAuditVulnerability
{
    public string Name { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Range { get; set; } = "";
    public bool IsDirect { get; set; }
    public List<string> Via { get; set; } = [];
}

public class LintResult
{
    public bool WasRun { get; set; }
    public string? Error { get; set; }
    public string ToolUsed { get; set; } = "";
    public int TotalIssues { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<LintFileResult> Files { get; set; } = [];
}

public class LintFileResult
{
    public string FilePath { get; set; } = "";
    public List<LintIssue> Issues { get; set; } = [];
}

public class LintIssue
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string RuleId { get; set; } = "";
    public string Message { get; set; } = "";
    public string LintSeverity { get; set; } = "warning";
}

public class TypeCheckResult
{
    public bool WasRun { get; set; }
    public string? Error { get; set; }
    public int ErrorCount { get; set; }
    public List<TypeCheckError> Errors { get; set; } = [];
}

public class TypeCheckError
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
