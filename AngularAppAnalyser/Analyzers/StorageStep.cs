using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

/// <summary>
/// Wraps the remaining analysis methods from the original monolith.
/// Each can be extracted to its own IAnalysisStep file when ready.
/// </summary>
public sealed class StorageStep : IAnalysisStep
{
    public string Name => "Analyzing storage usage";
    public string Icon => "\ud83d\udcbe";
    public int Order => 30;

    private static readonly List<SecurityPattern> Patterns =
    [
        new(@"localStorage\.setItem\s*\(", "localStorage.setItem", "Data stored in localStorage is accessible to any JavaScript on the page. Never store tokens or sensitive data.", Severity.High, "Storage"),
        new(@"localStorage\.getItem\s*\(", "localStorage.getItem", "Reading from localStorage. If this retrieves auth tokens, consider HttpOnly cookies.", Severity.Medium, "Storage"),
        new(@"sessionStorage\.setItem\s*\(", "sessionStorage.setItem", "Data in sessionStorage is accessible to page JavaScript. Prefer HttpOnly cookies for auth tokens.", Severity.Medium, "Storage"),
        new(@"sessionStorage\.getItem\s*\(", "sessionStorage.getItem", "Reading from sessionStorage. Ensure no sensitive data is stored.", Severity.Low, "Storage"),
        new(@"document\.cookie\s*=", "Direct cookie write", "Setting cookies via document.cookie. Use a cookie service and ensure Secure, HttpOnly, and SameSite flags.", Severity.High, "Storage"),
        new(@"IndexedDB|indexedDB\.open", "IndexedDB usage", "IndexedDB stores data client-side. Ensure no sensitive/PII data is stored unencrypted.", Severity.Medium, "Storage"),
        new(@"\.setItem\s*\(\s*['""](?:token|auth|jwt|access_token|refresh_token|session)", "Auth token in storage", "Auth token being stored in browser storage. Use HttpOnly cookies.", Severity.Critical, "Storage"),
        new(@"\.getItem\s*\(\s*['""](?:token|auth|jwt|access_token|refresh_token|session)", "Auth token read from storage", "Reading auth token from browser storage. Consider HttpOnly cookie-based auth.", Severity.High, "Storage"),
        new(@"(localStorage|sessionStorage)\s*\.\s*clear\s*\(\s*\)", "Storage clear on logout", "Ensure this is part of a complete logout flow that invalidates server-side sessions.", Severity.Low, "Storage"),
    ];

    public void Execute(AnalysisContext context)
    {
        context.Report.StorageFindings = AnalysisContext.ScanFiles(
            context.GetSourceFiles("*.ts"), context.AppPath, Patterns);
    }

    public string GetSummary(AnalysisContext context) =>
        $"{context.Report.StorageFindings.Count} storage usage(s) found";
}
