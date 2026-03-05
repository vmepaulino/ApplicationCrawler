using System.Text.RegularExpressions;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

public sealed class SecurityStep : IAnalysisStep
{
    public string Name => "Scanning for security vulnerabilities";
    public string Icon => "\ud83d\udee1\ufe0f";
    public int Order => 20;

    private static readonly List<SecurityPattern> TsPatterns =
    [
        new("innerHTML\\s*=", "Direct innerHTML assignment", "Direct innerHTML assignment bypasses Angular sanitization. Use [innerHTML] binding or DomSanitizer.", Severity.Critical, "Security"),
        new("bypassSecurityTrustHtml", "bypassSecurityTrustHtml usage", "bypassSecurityTrustHtml disables XSS protection. Ensure input is properly validated before bypassing.", Severity.Critical, "Security"),
        new("bypassSecurityTrustScript", "bypassSecurityTrustScript usage", "bypassSecurityTrustScript disables script sanitization. This is extremely dangerous.", Severity.Critical, "Security"),
        new("bypassSecurityTrustUrl", "bypassSecurityTrustUrl usage", "bypassSecurityTrustUrl disables URL sanitization. Validate URLs before bypassing.", Severity.High, "Security"),
        new("bypassSecurityTrustResourceUrl", "bypassSecurityTrustResourceUrl usage", "bypassSecurityTrustResourceUrl disables resource URL sanitization.", Severity.High, "Security"),
        new("bypassSecurityTrustStyle", "bypassSecurityTrustStyle usage", "bypassSecurityTrustStyle disables style sanitization.", Severity.Medium, "Security"),
        new(@"document\.cookie", "Direct cookie access", "Direct document.cookie access. Use a secure cookie service or HttpOnly cookies set by the server.", Severity.High, "Security"),
        new(@"eval\s*\(", "eval() usage", "eval() executes arbitrary code and is a major XSS vector. Remove eval() calls.", Severity.Critical, "Security"),
        new(@"new\s+Function\s*\(", "new Function() usage", "new Function() is equivalent to eval(). Remove dynamic function creation.", Severity.Critical, "Security"),
        new(@"document\.write", "document.write usage", "document.write can introduce XSS vulnerabilities. Use Angular rendering.", Severity.High, "Security"),
        new(@"window\.open\s*\(", "window.open usage", "window.open can be used for phishing. Validate target URLs.", Severity.Medium, "Security"),
        new(@"\bhttp://", "Insecure HTTP URL", "HTTP URLs transmit data in plaintext. Use HTTPS for all API and resource URLs.", Severity.High, "Security"),
        new(@"password.*=.*['""][^'""]+['""]", "Hardcoded password", "Possible hardcoded password detected. Store credentials securely, never in source code.", Severity.Critical, "Security"),
        new(@"(api[_-]?key|apikey|secret[_-]?key|access[_-]?token)\s*[:=]\s*['""][^'""]+['""]", "Hardcoded API key/secret", "Possible hardcoded API key or secret. Use environment variables.", Severity.Critical, "Security"),
        new(@"(?<!//).*(?:Bearer\s+)[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+", "Hardcoded JWT token", "Possible hardcoded JWT token. Tokens should never be stored in source code.", Severity.Critical, "Security"),
        new(@"allowJs\s*:\s*true", "TypeScript allowJs enabled", "allowJs weakens type safety. Consider migrating JS files to TypeScript.", Severity.Low, "Security"),
        new(@"@HostListener\s*\(\s*['""]message['""]", "postMessage listener", "postMessage listener detected. Validate message origin to prevent cross-origin attacks.", Severity.High, "Security"),
        new(@"window\.addEventListener\s*\(\s*['""]message['""]", "postMessage listener (raw)", "Raw postMessage event listener. Validate event.origin before processing.", Severity.High, "Security"),
        new(@"this\.http\.(get|post|put|delete|patch)\s*\(\s*[`'""]http://", "Insecure HTTP API call", "API call uses HTTP instead of HTTPS. All API calls should use HTTPS.", Severity.Critical, "Security"),
        new(@"\.subscribe\s*\(\s*\)", "Empty subscribe", "Empty subscribe() with no error handler. Unhandled HTTP errors can leak information.", Severity.Medium, "Security"),
    ];

    private static readonly List<SecurityPattern> HtmlPatterns =
    [
        new(@"\[innerHTML\]\s*=", "[innerHTML] binding", "[innerHTML] binding detected. Ensure bound content is sanitized.", Severity.Medium, "Security"),
        new(@"<\s*iframe", "iframe usage", "iframe detected. Ensure src is validated and consider sandbox attribute.", Severity.High, "Security"),
        new(@"<\s*object\b", "object tag usage", "HTML object tag detected. This can load external content and is a security risk.", Severity.High, "Security"),
        new(@"<\s*embed\b", "embed tag usage", "HTML embed tag detected. This can load external content and is a security risk.", Severity.High, "Security"),
        new(@"javascript\s*:", "javascript: URL scheme", "javascript: URL scheme is an XSS vector. Use Angular routing instead.", Severity.Critical, "Security"),
        new(@"on\w+\s*=\s*['""]", "Inline event handler", "Inline event handlers bypass Angular's security context. Use Angular event bindings.", Severity.High, "Security"),
        new(@"<\s*form[^>]*action\s*=\s*['""]http://", "Insecure form action", "Form action uses HTTP. Use HTTPS for form submissions.", Severity.High, "Security"),
        new(@"style\s*=\s*['""].*expression\s*\(", "CSS expression", "CSS expression() detected. This is an XSS vector in older browsers.", Severity.High, "Security"),
    ];

    public void Execute(AnalysisContext context)
    {
        var tsFiles = context.GetSourceFiles("*.ts");
        var htmlFiles = context.GetSourceFiles("*.html");
        Console.WriteLine($"   Scanning {tsFiles.Count + htmlFiles.Count} file(s)...");

        var findings = AnalysisContext.ScanFiles(tsFiles, context.AppPath, TsPatterns);
        findings.AddRange(AnalysisContext.ScanFiles(htmlFiles, context.AppPath, HtmlPatterns));

        CheckSecurityConfig(context, findings);
        context.Report.SecurityFindings = findings;
    }

    public string GetSummary(AnalysisContext context)
    {
        var f = context.Report.SecurityFindings;
        return $"{f.Count} finding(s) \u2014 {f.Count(x => x.Severity == Severity.Critical)} critical, {f.Count(x => x.Severity == Severity.High)} high";
    }

    private static void CheckSecurityConfig(AnalysisContext context, List<Finding> findings)
    {
        var appPath = context.AppPath;

        var indexHtmlPath = context.FindFile("index.html");
        if (indexHtmlPath is not null)
        {
            var content = File.ReadAllText(indexHtmlPath);
            if (!content.Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
                findings.Add(new Finding { Title = "Missing Content-Security-Policy", Description = "No CSP meta tag found in index.html. CSP helps prevent XSS attacks.", Severity = Severity.High, Category = "Security", File = Path.GetRelativePath(appPath, indexHtmlPath) });
        }

        foreach (var envFile in context.GetSourceFiles("environment*.ts"))
        {
            try
            {
                var content = File.ReadAllText(envFile);
                if (Regex.IsMatch(content, @"(password|secret|private[_-]?key)\s*[:=]", RegexOptions.IgnoreCase))
                    findings.Add(new Finding { Title = "Secrets in environment file", Description = "Possible secrets in environment file. These are bundled into client-side code.", Severity = Severity.Critical, Category = "Security", File = Path.GetRelativePath(appPath, envFile) });
            }
            catch { }
        }

        var gitignorePath = Path.Combine(appPath, ".gitignore");
        if (File.Exists(gitignorePath) && !File.ReadAllText(gitignorePath).Contains("environment", StringComparison.OrdinalIgnoreCase))
            findings.Add(new Finding { Title = "Environment files not in .gitignore", Description = "Consider adding environment.prod.ts to .gitignore.", Severity = Severity.Medium, Category = "Security", File = ".gitignore" });

        if (!File.Exists(Path.Combine(appPath, "package-lock.json")) &&
            !File.Exists(Path.Combine(appPath, "yarn.lock")) &&
            !File.Exists(Path.Combine(appPath, "pnpm-lock.yaml")))
            findings.Add(new Finding { Title = "No lock file found", Description = "Lock files ensure reproducible builds and prevent supply chain attacks.", Severity = Severity.High, Category = "Security" });
    }
}
