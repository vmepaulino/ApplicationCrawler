using System.Text.RegularExpressions;
using AngularAppAnalyser.Abstractions;
using AngularAppAnalyser.Models;

namespace AngularAppAnalyser.Analyzers;

internal static class SecurityPostureAnalyzer
{
    public static List<SecurityPostureItem> Build(AnalysisContext ctx)
    {
        var posture = new List<SecurityPostureItem>();
        var appPath = ctx.AppPath;
        var tsFiles = ctx.GetSourceFiles("*.ts");

        bool FileContains(string pattern) =>
            tsFiles.Any(f => { try { return Regex.IsMatch(File.ReadAllText(f), pattern, RegexOptions.IgnoreCase); } catch { return false; } });

        bool FileContainsBoth(string p1, string p2) =>
            tsFiles.Any(f => { try { var c = File.ReadAllText(f); return Regex.IsMatch(c, p1, RegexOptions.IgnoreCase) && Regex.IsMatch(c, p2, RegexOptions.IgnoreCase); } catch { return false; } });

        var hasAuthGuard = FileContains(@"canActivate|CanActivateFn|canMatch|CanMatchFn");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Route guards (canActivate)", InPlace = hasAuthGuard, Details = hasAuthGuard ? "Route guards detected" : "No route guards \u2014 authenticated routes unprotected" });

        var hasAuthInterceptor = FileContainsBoth(@"implements\s+HttpInterceptor|HttpInterceptorFn", @"(authorization|bearer|auth[_-]?token|x-auth)");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Auth token interceptor", InPlace = hasAuthInterceptor, Details = hasAuthInterceptor ? "HTTP interceptor handles auth tokens" : "No auth interceptor" });

        var hasRoleCheck = FileContains(@"(role|permission|isAdmin|hasRole|canAccess)");
        posture.Add(new SecurityPostureItem { Area = "Authentication", Check = "Role/permission checks", InPlace = hasRoleCheck, Details = hasRoleCheck ? "Role-based access patterns detected" : "No RBAC patterns detected" });

        var indexHtml = ctx.FindFile("index.html");
        var hasCsp = indexHtml is not null && File.ReadAllText(indexHtml).Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase);
        posture.Add(new SecurityPostureItem { Area = "XSS Protection", Check = "Content Security Policy", InPlace = hasCsp, Details = hasCsp ? "CSP found in index.html" : "No CSP configured" });

        posture.Add(new SecurityPostureItem { Area = "XSS Protection", Check = "DomSanitizer usage", InPlace = FileContains(@"DomSanitizer"), Details = FileContains(@"DomSanitizer") ? "DomSanitizer used" : "No DomSanitizer usage" });

        posture.Add(new SecurityPostureItem { Area = "CSRF Protection", Check = "CSRF/XSRF token handling", InPlace = FileContains(@"(X-XSRF-TOKEN|X-CSRF-TOKEN|HttpClientXsrfModule|withXsrfConfiguration|xsrfHeaderName)"), Details = "CSRF protection check" });

        posture.Add(new SecurityPostureItem { Area = "Error Handling", Check = "HTTP error interceptor", InPlace = FileContainsBoth(@"implements\s+HttpInterceptor|HttpInterceptorFn", @"(catchError|HttpErrorResponse)"), Details = "Centralized HTTP error handling" });

        posture.Add(new SecurityPostureItem { Area = "Error Handling", Check = "Global ErrorHandler", InPlace = FileContains(@"implements\s+ErrorHandler"), Details = "Custom ErrorHandler" });

        posture.Add(new SecurityPostureItem { Area = "Input Validation", Check = "Reactive forms validation", InPlace = FileContains(@"(FormBuilder|FormGroup|FormControl|Validators\.)"), Details = "Reactive forms with validators" });

        var hasLockFile = File.Exists(Path.Combine(appPath, "package-lock.json")) || File.Exists(Path.Combine(appPath, "yarn.lock")) || File.Exists(Path.Combine(appPath, "pnpm-lock.yaml"));
        posture.Add(new SecurityPostureItem { Area = "Supply Chain", Check = "Dependency lock file", InPlace = hasLockFile, Details = hasLockFile ? "Lock file present" : "No lock file" });

        var envDir = Path.Combine(appPath, "src", "environments");
        posture.Add(new SecurityPostureItem { Area = "Configuration", Check = "Environment separation", InPlace = Directory.Exists(envDir) && Directory.GetFiles(envDir, "*.ts").Length >= 2, Details = "Separate environment files" });

        var tsconfigPath = Path.Combine(appPath, "tsconfig.json");
        posture.Add(new SecurityPostureItem { Area = "Code Quality", Check = "TypeScript strict mode", InPlace = File.Exists(tsconfigPath) && Regex.IsMatch(File.ReadAllText(tsconfigPath), @"""strict""\s*:\s*true"), Details = "Strict type checking" });

        var report = ctx.Report;
        posture.Add(new SecurityPostureItem { Area = "Code Quality", Check = "Linter configured", InPlace = report.ProjectMetadata.DevDependencies.ContainsKey("eslint") || report.ProjectMetadata.DevDependencies.ContainsKey("@angular-eslint/builder"), Details = "ESLint/angular-eslint" });

        var hasInsecureUrls = tsFiles.Any(f => { try { return Regex.IsMatch(File.ReadAllText(f), @"['""`]http://(?!localhost)"); } catch { return false; } });
        posture.Add(new SecurityPostureItem { Area = "Transport Security", Check = "HTTPS-only API calls", InPlace = !hasInsecureUrls, Details = !hasInsecureUrls ? "All HTTPS" : "HTTP URLs detected" });

        posture.Add(new SecurityPostureItem { Area = "Resource Management", Check = "Subscription cleanup", InPlace = FileContains(@"(takeUntil|takeUntilDestroyed|unsubscribe|DestroyRef|AsyncPipe)"), Details = "Subscription cleanup patterns" });

        return posture;
    }
}
